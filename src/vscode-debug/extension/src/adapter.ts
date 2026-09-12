import { existsSync, statSync } from 'node:fs';
import { readFile } from 'node:fs/promises';
import * as path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { BreakpointState, BreakpointStore } from './breakpointStore';
import { DapRequest, Disposable, ProtocolMessage } from './dapProtocol';
import { DebugHost, HostClient, HostFactory, HostLaunch, HostMessage } from './hostClient';
import { HostFrame, HostStoppedMessage } from './debugTypes';

type Phase = 'created' | 'initialized' | 'starting' | 'configuring' | 'running' | 'paused' | 'terminated';

/** Shared implementation for VS Code's inline adapter and the standalone stdio entry. */
export class OkojoDebugSession implements Disposable {
  private phase: Phase = 'created';
  private sequence = 1;
  private nextFrameId = 1;
  private readonly frameIds = new Map<number, number>();
  private readonly listeners = new Set<(message: ProtocolMessage) => void>();
  private readonly answered = new WeakSet<object>();
  private readonly pending = new Map<number, DapRequest>();
  private readonly breakpoints = new BreakpointStore();
  private readonly knownSources = new Set<string>();
  private host?: DebugHost;
  private pendingLaunch?: DapRequest;
  private entry?: HostStoppedMessage;
  private snapshot?: HostStoppedMessage;
  private launchArgs: Record<string, any> = {};
  private cwd = process.cwd();
  private lineBase = 1;
  private columnBase = 1;
  private uriPaths = false;
  private stepGranularity: 'line' | 'instruction' = 'line';
  private exceptionFilters: string[] = [];
  private breakpointSerial: Promise<unknown> = Promise.resolve();
  private disposed = false;

  public constructor(private readonly makeHost: HostFactory = (launch, event) => new HostClient(launch, event)) {}

  public readonly onDidSendMessage = (
    listener: (message: ProtocolMessage) => void,
    thisArgs?: any,
    disposables?: Disposable[],
  ): Disposable => {
    const bound = thisArgs ? listener.bind(thisArgs) : listener;
    this.listeners.add(bound);
    const disposable = { dispose: () => { this.listeners.delete(bound); } };
    disposables?.push(disposable);
    return disposable;
  };

  public handleMessage(message: ProtocolMessage): void {
    if (this.disposed || message.type !== 'request') return;
    const request = message as DapRequest;
    if (!Number.isInteger(request.seq) || request.seq < 0 || typeof request.command !== 'string') return;
    if (this.pending.has(request.seq)) {
      this.error(request, 'A request with this sequence number is already pending.');
      return;
    }
    this.pending.set(request.seq, request);
    // Do not serialize the request loop: launch intentionally remains pending
    // while the client sends setBreakpoints and configurationDone.
    void this.dispatch(request).catch(error => this.error(request, String(error instanceof Error ? error.message : error)));
  }

  private send(message: Omit<ProtocolMessage, 'seq'>): void {
    const envelope = { ...message, seq: this.sequence++ } as ProtocolMessage;
    for (const listener of this.listeners) listener(envelope);
  }

  private respond(request: DapRequest, body?: Record<string, any>): void {
    if (this.answered.has(request)) return;
    this.answered.add(request);
    if (this.pending.get(request.seq) === request) this.pending.delete(request.seq);
    this.send({ type: 'response', request_seq: request.seq, command: request.command, success: true, ...(body ? { body } : {}) });
  }

  private error(request: DapRequest, message: string): void {
    if (this.answered.has(request)) return;
    this.answered.add(request);
    if (this.pending.get(request.seq) === request) this.pending.delete(request.seq);
    this.send({ type: 'response', request_seq: request.seq, command: request.command,
      success: false, message, body: { error: { id: 1, format: message, showUser: true } } });
  }

  private event(event: string, body?: Record<string, any>): void {
    this.send({ type: 'event', event, ...(body ? { body } : {}) });
  }

  private async dispatch(request: DapRequest): Promise<void> {
    if (request.arguments !== undefined && (!request.arguments || typeof request.arguments !== 'object' || Array.isArray(request.arguments))) {
      throw new Error('Request arguments must be an object.');
    }
    const args = request.arguments ?? {};
    if (request.command === 'initialize') {
      if (this.phase !== 'created') throw new Error('The adapter is already initialized.');
      if (args.pathFormat !== undefined && !['path', 'uri'].includes(args.pathFormat)) throw new Error('Unsupported pathFormat.');
      this.lineBase = args.linesStartAt1 === false ? 0 : 1;
      this.columnBase = args.columnsStartAt1 === false ? 0 : 1;
      this.uriPaths = args.pathFormat === 'uri';
      this.phase = 'initialized';
      this.respond(request, {
        supportsConfigurationDoneRequest: true,
        supportsTerminateRequest: true,
        supportsEvaluateForHovers: true,
        supportsExceptionInfoRequest: true,
        supportsLoadedSourcesRequest: true,
        supportsSteppingGranularity: true,
        exceptionBreakpointFilters: [{ filter: 'all', label: 'All JavaScript exceptions', default: false,
          description: 'Stops when the VM first captures an exception, before unwinding. Includes handled exceptions.' }],
      });
      return;
    }
    if (request.command === 'disconnect' || request.command === 'terminate') {
      this.respond(request);
      this.host?.stop();
      this.finish(0);
      return;
    }
    if (this.phase === 'created') throw new Error('initialize must be the first request.');
    if (this.phase === 'terminated') throw new Error('The debug session has terminated.');

    switch (request.command) {
      case 'launch':
        await this.launch(request, args);
        return;
      case 'attach':
        throw new Error('Attach is not supported. Use an Okojo launch configuration.');
      case 'configurationDone':
        if (this.phase !== 'configuring' || !this.entry || !this.pendingLaunch) throw new Error('No launch is awaiting configuration.');
        await this.breakpointSerial;
        this.respond(request);
        this.respond(this.pendingLaunch);
        this.pendingLaunch = undefined;
        if (this.launchArgs.stopOnEntry === true && !this.launchArgs.noDebug) {
          this.stopped(this.entry);
        } else {
          this.phase = 'running';
          // The host's pre-execution stop is a configuration barrier, not a
          // user-visible stop unless stopOnEntry was explicitly requested.
          void this.host!.request('resume', { mode: 'continue' }).catch(error => this.fatal(error));
        }
        this.entry = undefined;
        return;
      case 'setBreakpoints':
        await this.setBreakpoints(request, args);
        return;
      case 'setExceptionBreakpoints': {
        const filters = args.filters ?? [];
        if (!Array.isArray(filters) || filters.some(filter => filter !== 'all')) throw new Error('Only the all-exceptions filter is supported.');
        if ((args.filterOptions?.length ?? 0) || (args.exceptionOptions?.length ?? 0)) throw new Error('Exception conditions and exception options are not supported.');
        this.exceptionFilters = this.launchArgs.noDebug ? [] : filters;
        if (this.host) await this.host.request('setExceptionBreakpoints', { filters: this.exceptionFilters });
        this.respond(request, { breakpoints: [] });
        return;
      }
      case 'threads':
        this.respond(request, { threads: this.host ? [{ id: 1, name: 'Okojo Main Thread' }] : [] });
        return;
      case 'pause':
        this.thread(args);
        if (this.phase === 'paused') { this.respond(request); return; }
        if (this.phase !== 'running') throw new Error('Execution is not running.');
        await this.host!.request('pause');
        this.respond(request);
        return;
      case 'continue':
      case 'next':
      case 'stepIn':
      case 'stepOut':
        await this.resume(request, args);
        return;
      case 'stackTrace': {
        this.thread(args);
        const snapshot = this.requirePaused();
        const start = this.integer(args.startFrame ?? 0, 'startFrame', 0);
        const levels = this.integer(args.levels ?? 0, 'levels', 0);
        const frames = snapshot.stackFrames ?? (snapshot.currentFrame ? [snapshot.currentFrame] : []);
        const ids = [...this.frameIds.keys()];
        this.respond(request, {
          stackFrames: frames.slice(start, levels ? start + levels : undefined)
            .map((frame, index) => this.frame(frame, ids[start + index], start + index)),
          totalFrames: frames.length,
        });
        return;
      }
      case 'scopes': {
        this.requirePaused();
        const frameId = this.hostFrame(args.frameId);
        const body = await this.host!.request('scopes', { frameId });
        this.respond(request, body);
        return;
      }
      case 'variables':
        this.requirePaused();
        this.respond(request, await this.host!.request('variables', {
          variablesReference: this.integer(args.variablesReference, 'variablesReference', 1),
          start: this.integer(args.start ?? 0, 'start', 0),
          count: this.integer(args.count ?? 0, 'count', 0),
          ...(args.filter !== undefined ? { filter: args.filter } : {}),
        }));
        return;
      case 'evaluate':
        this.requirePaused();
        this.respond(request, await this.host!.request('evaluate', {
          expression: this.string(args.expression, 'expression'),
          frameId: args.frameId === undefined ? 1 : this.hostFrame(args.frameId),
        }));
        return;
      case 'exceptionInfo': {
        this.thread(args);
        const snapshot = this.requirePaused();
        if (snapshot.kind !== 'caught-exception') throw new Error('The current stop is not an exception.');
        this.respond(request, { exceptionId: 'JavaScript exception', breakMode: 'always', description: snapshot.summary });
        return;
      }
      case 'loadedSources': {
        if (!this.host) throw new Error('No program has been launched.');
        const body = await this.host.request('loadedSources');
        const sources = (body.sources ?? []).map((source: { path: string }) => {
          this.knownSources.add(this.normalizePath(source.path));
          return this.source(source.path);
        });
        this.respond(request, { sources });
        return;
      }
      case 'source': {
        const sourcePath = this.normalizePath(this.string(args.source?.path, 'source.path'));
        if (!this.knownSources.has(sourcePath)) throw new Error('The requested source is not known to this session.');
        const content = await readFile(sourcePath, 'utf8');
        this.respond(request, { content, mimeType: 'text/javascript' });
        return;
      }
      case 'okojo/bytecode':
        this.requirePaused();
        this.respond(request, await this.host!.request('bytecode'));
        return;
      default:
        throw new Error(`Unsupported DAP request '${request.command}'.`);
    }
  }

  private async launch(request: DapRequest, args: Record<string, any>): Promise<void> {
    if (this.phase !== 'initialized') throw new Error('Only one launch is allowed per adapter session.');
    const programValue = this.string(args.program, 'program');
    this.cwd = args.cwd === undefined ? process.cwd() : this.normalizePath(this.string(args.cwd, 'cwd'));
    if (!existsSync(this.cwd) || !statSync(this.cwd).isDirectory()) throw new Error(`Working directory does not exist: ${this.cwd}`);
    const program = this.normalizePath(programValue);
    if (!existsSync(program) || !statSync(program).isFile()) throw new Error(`Program does not exist: ${program}`);
    const interval = this.integer(args.checkInterval ?? 1024, 'checkInterval', 1);
    const granularity = args.stepGranularity ?? 'line';
    if (!['line', 'instruction'].includes(granularity)) throw new Error('stepGranularity must be line or instruction.');
    this.stepGranularity = granularity;
    this.launchArgs = { ...args, program };
    if (args.noDebug) this.exceptionFilters = [];
    this.knownSources.add(program);
    const hostArgs = ['--script', program, '--cwd', this.cwd, '--check-interval', String(interval),
      '--step-granularity', granularity, '--stop-entry', '--structured-output'];
    if (args.moduleEntry === true) hostArgs.push('--module-entry');
    else if (args.moduleEntry === false) hostArgs.push('--script-entry');
    if (args.enableSourceMaps === true) hostArgs.push('--enable-source-maps');
    if (args.noDebug || args.stopOnDebuggerStatement === false) hostArgs.push('--no-stop-debugger');
    if (args.noDebug || args.stopOnBreakpoint === false) hostArgs.push('--no-stop-breakpoint');
    for (const [key, flag] of Object.entries({ stopOnCall: '--stop-call', stopOnReturn: '--stop-return',
      stopOnPump: '--stop-pump', stopOnSuspendGenerator: '--stop-suspend', stopOnResumeGenerator: '--stop-resume', stopOnPeriodic: '--stop-periodic' })) {
      if (args[key] === true && !args.noDebug) hostArgs.push(flag);
    }
    const dotnet = args.dotnetPath === undefined ? 'dotnet' : this.string(args.dotnetPath, 'dotnetPath');
    let command = dotnet;
    let commandArgs: string[];
    if (args.debugServerPath !== undefined) {
      const server = this.normalizePath(this.string(args.debugServerPath, 'debugServerPath'));
      if (!existsSync(server) || !statSync(server).isFile()) throw new Error(`Debug host does not exist: ${server}`);
      const prefix = args.debugServerArgs ?? [];
      if (!Array.isArray(prefix) || prefix.some(item => typeof item !== 'string')) throw new Error('debugServerArgs must be an array of strings.');
      if (path.extname(server).toLowerCase() === '.dll') commandArgs = [server, ...prefix, ...hostArgs];
      else { command = server; commandArgs = [...prefix, ...hostArgs]; }
    } else {
      const project = this.findProject(args.debugServerProject);
      commandArgs = ['run', '--project', project, '--configuration', 'Release', '--no-launch-profile', '--verbosity', 'quiet'];
      if (args.noBuild === true) commandArgs.push('--no-build');
      commandArgs.push('--', ...hostArgs);
    }
    const env: NodeJS.ProcessEnv = { ...process.env };
    if (args.env !== undefined) {
      if (!args.env || typeof args.env !== 'object' || Array.isArray(args.env)) throw new Error('env must be an object.');
      for (const [name, value] of Object.entries(args.env)) {
        if (value === null) delete env[name];
        else if (typeof value === 'string') env[name] = value;
        else throw new Error(`Environment variable ${name} must be a string or null.`);
      }
    }
    if (args.traceBreakpoints) env.OKOJO_DEBUG_TRACE_BREAKPOINTS = '1';
    this.pendingLaunch = request;
    this.phase = 'starting';
    try {
      const launch: HostLaunch = { command, args: commandArgs, cwd: this.cwd, env,
        startupTimeout: this.integer(args.startupTimeout ?? 120_000, 'startupTimeout', 1),
        requestTimeout: this.integer(args.requestTimeout ?? 10_000, 'requestTimeout', 1) };
      if (args.traceAdapter) this.event('output', { category: 'console', output: `[okojo] ${command} ${commandArgs.map(arg => JSON.stringify(arg)).join(' ')}\n` });
      this.host = this.makeHost(launch, event => this.onHostEvent(event));
      this.entry = await this.host.ready as HostStoppedMessage;
      if (this.isTerminated()) return;
      this.phase = 'configuring';
      for (const [source, states] of this.breakpoints.sources()) await this.syncBreakpoints(source, [...states.values()]);
      if (this.exceptionFilters.length) await this.host.request('setExceptionBreakpoints', { filters: this.exceptionFilters });
      this.event('initialized');
      // The launch response is sent by configurationDone, not here.
    } catch (error) {
      this.error(request, error instanceof Error ? error.message : String(error));
      this.pendingLaunch = undefined;
      this.host?.stop();
      this.finish(1);
    }
  }

  private async setBreakpoints(request: DapRequest, args: Record<string, any>): Promise<void> {
    const source = this.normalizePath(this.string(args.source?.path, 'source.path'));
    const requested: Record<string, any>[] = args.breakpoints ?? (args.lines ?? []).map((line: number) => ({ line }));
    if (!Array.isArray(requested)) throw new Error('breakpoints must be an array.');
    const entries = requested.map(item => {
      if (!item || typeof item !== 'object') throw new Error('Invalid breakpoint.');
      const line = this.integer(item.line, 'line', this.lineBase) + 1 - this.lineBase;
      const unsupported = ['condition', 'hitCondition', 'logMessage', 'column'].some(key => item[key] !== undefined);
      return { line, unsupported };
    });
    const states = this.breakpoints.replace(source, entries.filter(item => !item.unsupported).map(item => item.line));
    if (this.host) {
      const sync = this.breakpointSerial.then(() => this.syncBreakpoints(source, states));
      this.breakpointSerial = sync.catch(() => {});
      await sync;
    }
    let index = 0;
    this.respond(request, { breakpoints: entries.map(item => item.unsupported
      ? { verified: false, line: item.line - 1 + this.lineBase,
        message: 'Conditional, hit-count, logpoint and column breakpoints are not supported.' }
      : this.breakpoint(states[index++])) });
  }

  private async syncBreakpoints(sourcePath: string, states: BreakpointState[]): Promise<void> {
    const body = await this.host!.request('setBreakpoints', { sourcePath,
      breakpoints: this.launchArgs.noDebug ? [] : states.map(state => ({ line: state.requestedLine, id: state.id })) });
    for (const message of body.breakpoints ?? []) this.breakpoints.applyUpdate(message);
  }

  private breakpoint(state: BreakpointState): Record<string, any> {
    return { id: state.id, verified: state.verified,
      source: this.source(state.resolvedSourcePath ?? state.sourcePath),
      line: (state.resolvedLine ?? state.requestedLine) - 1 + this.lineBase,
      ...(state.resolvedColumn ? { column: state.resolvedColumn - 1 + this.columnBase } : {}),
      ...(state.message ? { message: state.message } : {}),
    };
  }

  private async resume(request: DapRequest, args: Record<string, any>): Promise<void> {
    this.thread(args);
    this.requirePaused();
    if (args.targetId !== undefined) throw new Error('Step-in targets are not supported.');
    const granularity = args.granularity ?? this.stepGranularity;
    if (!['line', 'statement', 'instruction'].includes(granularity)) throw new Error('Invalid stepping granularity.');
    const mode = { continue: 'continue', next: 'step', stepIn: 'stepin', stepOut: 'stepout' }[request.command]!;
    this.invalidatePause();
    this.phase = 'running';
    try {
      await this.host!.request('resume', { mode, granularity: granularity === 'instruction' ? 'instruction' : 'line' });
      this.respond(request, request.command === 'continue' ? { allThreadsContinued: true } : undefined);
      this.event('continued', { threadId: 1, allThreadsContinued: true });
    } catch (error) {
      this.error(request, error instanceof Error ? error.message : String(error));
      this.fatal(error);
    }
  }

  private stopped(snapshot: HostStoppedMessage): void {
    if (this.isTerminated()) return;
    this.snapshot = snapshot;
    this.phase = 'paused';
    this.frameIds.clear();
    const frames = snapshot.stackFrames ?? (snapshot.currentFrame ? [snapshot.currentFrame] : []);
    frames.forEach((_frame, index) => this.frameIds.set(this.nextFrameId++, index + 1));
    for (const frame of frames) if (frame.sourcePath) this.knownSources.add(this.normalizePath(frame.sourcePath));
    if (snapshot.sourceLocation?.sourcePath) this.knownSources.add(this.normalizePath(snapshot.sourceLocation.sourcePath));
    const reason = snapshot.kind === 'entry' ? 'entry' : snapshot.kind === 'step' ? 'step'
      : snapshot.kind === 'breakpoint' || snapshot.kind === 'debugger-statement' ? 'breakpoint'
      : snapshot.kind === 'caught-exception' ? 'exception' : 'pause';
    this.event('stopped', { reason, threadId: 1, allThreadsStopped: true, description: snapshot.summary });
  }

  private onHostEvent(message: HostMessage): void {
    if (this.isTerminated()) return;
    switch (message.event) {
      case 'stopped': this.stopped(message as HostStoppedMessage); break;
      case 'breakpoint-updated': {
        const state = this.breakpoints.applyUpdate(message);
        if (state) this.event('breakpoint', { reason: 'changed', breakpoint: this.breakpoint(state) });
        break;
      }
      case 'bytecode': this.event('okojo/bytecode', message); break;
      case 'output': this.event('output', { category: message.category ?? 'console', output: message.output ?? '' }); break;
      case 'error': this.event('output', { category: 'stderr', output: `[okojo] ${message.type ?? 'Error'}: ${message.message ?? ''}\n${message.stack ?? ''}\n` }); break;
      case 'host-exit':
        if (this.pendingLaunch) this.error(this.pendingLaunch, message.message ?? 'The debug host exited during launch.');
        this.finish(message.exitCode ?? 1);
        break;
      case 'terminated': this.finish(message.exitCode ?? 0); break;
    }
  }

  private frame(frame: HostFrame, id: number, index: number): Record<string, any> {
    const location = index === 0 ? this.snapshot?.sourceLocation : undefined;
    const sourcePath = frame.sourcePath ?? location?.sourcePath;
    const line = frame.hasSourceLocation ? frame.sourceLine : location?.line;
    const column = frame.hasSourceLocation ? frame.sourceColumn : location?.column;
    return { id, name: frame.functionName || '<anonymous>',
      ...(sourcePath ? { source: this.source(sourcePath) } : {}),
      line: line && line > 0 ? line - 1 + this.lineBase : 0,
      column: column && column > 0 ? column - 1 + this.columnBase : 0 };
  }

  private source(sourcePath: string): Record<string, any> {
    const normalized = this.normalizePath(sourcePath);
    return { name: path.basename(normalized), path: this.uriPaths ? pathToFileURL(normalized).href : normalized, sourceReference: 0 };
  }

  private hostFrame(frameId: unknown): number {
    const id = this.integer(frameId, 'frameId', 1);
    const frame = this.frameIds.get(id);
    if (frame === undefined) throw new Error('Unknown or expired frame id.');
    return frame;
  }

  private requirePaused(): HostStoppedMessage {
    if (this.phase !== 'paused' || !this.snapshot) throw new Error('Execution is not paused.');
    return this.snapshot;
  }

  private thread(args: Record<string, any>): void {
    if (args.threadId !== 1) throw new Error('Unknown thread id; Okojo exposes thread 1.');
  }

  private normalizePath(value: string): string {
    if (/^file:/i.test(value)) value = fileURLToPath(value);
    else if (/^[a-z][a-z\d+.-]*:\/\//i.test(value)) throw new Error('Only local file sources are supported.');
    const resolved = path.resolve(this.cwd, value);
    return process.platform === 'win32' ? resolved.toLowerCase() : resolved;
  }

  private findProject(supplied?: unknown): string {
    if (supplied !== undefined) {
      const project = this.normalizePath(this.string(supplied, 'debugServerProject'));
      if (!existsSync(project)) throw new Error(`Debug server project does not exist: ${project}`);
      return project;
    }
    for (let directory = this.cwd;; directory = path.dirname(directory)) {
      const candidate = path.join(directory, 'src', 'Okojo.DebugServer', 'Okojo.DebugServer.csproj');
      if (existsSync(candidate)) return candidate;
      if (path.dirname(directory) === directory) break;
    }
    const adjacent = path.resolve(__dirname, '../../../Okojo.DebugServer/Okojo.DebugServer.csproj');
    if (existsSync(adjacent)) return adjacent;
    throw new Error('Set debugServerPath to a published Okojo.DebugServer binary/DLL, or debugServerProject to its .csproj.');
  }

  private integer(value: unknown, name: string, minimum: number): number {
    if (typeof value !== 'number' || !Number.isInteger(value) || value < minimum || value > 0x7fffffff) {
      throw new Error(`${name} must be an integer between ${minimum} and 2147483647.`);
    }
    return value;
  }

  private string(value: unknown, name: string): string {
    if (typeof value !== 'string' || value.length === 0 || value.includes('\0')) throw new Error(`${name} must be a non-empty string without NUL characters.`);
    return value;
  }

  private invalidatePause(): void { this.snapshot = undefined; this.frameIds.clear(); }
  private isTerminated(): boolean { return this.phase === 'terminated'; }

  private finish(exitCode: number): void {
    if (this.isTerminated()) return;
    this.phase = 'terminated';
    this.invalidatePause();
    this.entry = undefined;
    this.pendingLaunch = undefined;
    for (const request of [...this.pending.values()]) this.error(request, 'The debug session ended before the request completed.');
    this.event('exited', { exitCode });
    this.event('terminated');
  }

  private fatal(error: unknown): void {
    if (this.isTerminated()) return;
    this.event('output', { category: 'stderr', output: `[okojo] ${String(error)}\n` });
    this.host?.stop();
    this.finish(1);
  }

  public getStepGranularity(): 'line' | 'instruction' { return this.stepGranularity; }
  public setStepGranularity(value: 'line' | 'instruction'): void { this.stepGranularity = value; }
  public requestBytecodeDump(): void {
    if (this.phase === 'paused') void this.host?.request('bytecode').catch(error => {
      this.event('output', { category: 'stderr', output: `${String(error)}\n` });
    });
  }

  public dispose(): void {
    if (this.disposed) return;
    this.host?.stop();
    this.finish(0);
    this.disposed = true;
    this.listeners.clear();
  }
}
