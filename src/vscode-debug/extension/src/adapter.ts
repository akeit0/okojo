import {
  ChildProcessWithoutNullStreams,
  spawn,
} from 'node:child_process';
import { existsSync } from 'node:fs';
import * as path from 'node:path';
import * as readline from 'node:readline';
import * as vscode from 'vscode';
import { panelHost } from './panelHost';
import { BreakpointStore } from './breakpointStore';
import { buildBytecodeViewModel, renderBytecodeShellHtml } from './bytecodeView';
import {
  BreakpointRequestState,
  DapScopeEntry,
  HostBreakpointAddedMessage,
  HostBreakpointClearedMessage,
  HostBreakpointUpdatedMessage,
  HostBytecodeMessage,
  HostErrorMessage,
  HostEvaluateMessage,
  HostEventMessage,
  HostFrame,
  HostOptionUpdatedMessage,
  HostScopeSnapshot,
  HostStoppedMessage,
  OkojoLaunchArguments,
} from './debugTypes';

import {
  LoggingDebugSession,
  InitializedEvent,
  OutputEvent,
  StoppedEvent,
  TerminatedEvent,
} from '@vscode/debugadapter';
import { DebugProtocol } from '@vscode/debugprotocol';

export class OkojoDebugSession extends LoggingDebugSession {
  private static readonly adapterVersion = '0.0.1';
  private readonly threadId = 1;

  private launchArgs: OkojoLaunchArguments | undefined;
  private configurationDone = false;
  private hostProcess: ChildProcessWithoutNullStreams | undefined;
  private hostStarted = false;
  private hostTerminated = false;
  private nextVariablesReference = 1;
  private lastSnapshot: HostStoppedMessage | undefined;
  private lastStopKind = 'pause';
  private paused = false;
  private pauseTimestamp = 0;
  private pauseWasObserved = false;
  private traceAdapter = false;

  private readonly pendingBreakpointsBySource = new Map<string, BreakpointRequestState>();
  private readonly breakpointHandlesBySource = new Map<string, Set<number>>();
  private readonly handleSourceById = new Map<number, string>();
  private readonly breakpointStore = new BreakpointStore();
  private readonly scopesByReference = new Map<number, DapScopeEntry>();
  private readonly pendingEvaluateResponses = new Map<number, DebugProtocol.EvaluateResponse>();
  private stepGranularity: 'Line' | 'Instruction' = 'Line';
  private nextHostEvaluateRequestId = 1;
  private bytecodeShellReady = false;
  private pendingSourceMappedNext: SourceMappedNextState | undefined;
  private suppressStepGranularityOutput = false;

  public constructor() {
    super();
    this.setDebuggerLinesStartAt1(true);
    this.setDebuggerColumnsStartAt1(true);
  }

  protected initializeRequest(
    response: DebugProtocol.InitializeResponse,
    _args: DebugProtocol.InitializeRequestArguments
  ): void {
    response.body = response.body || {};
    response.body.supportsConfigurationDoneRequest = true;
    response.body.supportsTerminateRequest = true;
    response.body.supportsEvaluateForHovers = true;
    response.body.supportsExceptionInfoRequest = true;
    this.sendResponse(response);
    this.sendEvent(new InitializedEvent());
  }

  protected launchRequest(
    response: DebugProtocol.LaunchResponse,
    args: OkojoLaunchArguments
  ): void {
    this.launchArgs = args;
    this.sendResponse(response);
  }

  protected configurationDoneRequest(
    response: DebugProtocol.ConfigurationDoneResponse,
    _args: DebugProtocol.ConfigurationDoneArguments
  ): void {
    this.configurationDone = true;
    this.sendResponse(response);
    void this.startHostIfNeeded();
  }

  protected continueRequest(
    response: DebugProtocol.ContinueResponse,
    _args: DebugProtocol.ContinueArguments
  ): void {
    this.pendingSourceMappedNext = undefined;
    const pauseAgeMs = Date.now() - this.pauseTimestamp;
    if (this.traceAdapter) {
      this.sendEvent(new OutputEvent(
        `[okojo] continueRequest paused=${this.paused} observed=${this.pauseWasObserved} age=${pauseAgeMs}\n`,
        'console'
      ));
    }
    if (this.paused && !this.pauseWasObserved && pauseAgeMs >= 0 && pauseAgeMs < 1500) {
      this.sendEvent(new OutputEvent(
        `[okojo] rejecting premature continue (${pauseAgeMs}ms after stop)\n`,
        'console'
      ));
      this.sendErrorResponse(response, 2001, 'Pause handshake not completed yet.');
      return;
    }

    this.paused = false;
    this.sendHostCommand('continue');
    this.sendResponse(response);
  }

  protected nextRequest(
    response: DebugProtocol.NextResponse,
    _args: DebugProtocol.NextArguments
  ): void {
    if (this.tryStartSourceMappedNext()) {
      this.sendResponse(response);
      return;
    }

    this.pendingSourceMappedNext = undefined;
    this.paused = false;
    this.sendHostCommand('step');
    this.sendResponse(response);
  }

  protected stepInRequest(
    response: DebugProtocol.StepInResponse,
    _args: DebugProtocol.StepInArguments
  ): void {
    this.pendingSourceMappedNext = undefined;
    this.paused = false;
    this.sendHostCommand('stepin');
    this.sendResponse(response);
  }

  protected stepOutRequest(
    response: DebugProtocol.StepOutResponse,
    _args: DebugProtocol.StepOutArguments
  ): void {
    this.pendingSourceMappedNext = undefined;
    this.paused = false;
    this.sendHostCommand('stepout');
    this.sendResponse(response);
  }

  protected threadsRequest(
    response: DebugProtocol.ThreadsResponse
  ): void {
    if (this.traceAdapter) {
      this.sendEvent(new OutputEvent(
        `[okojo] threadsRequest paused=${this.paused}\n`,
        'console'
      ));
    }
    if (this.paused) {
      this.pauseWasObserved = true;
    }
    response.body = {
      threads: [{ id: this.threadId, name: 'Okojo Main Thread' }],
    };
    this.sendResponse(response);
  }

  protected disconnectRequest(
    response: DebugProtocol.DisconnectResponse,
    _args: DebugProtocol.DisconnectArguments
  ): void {
    this.shutdownHost();
    this.sendResponse(response);
  }

  protected terminateRequest(
    response: DebugProtocol.TerminateResponse,
    _args: DebugProtocol.TerminateArguments
  ): void {
    this.shutdownHost();
    this.sendResponse(response);
  }

  public requestBytecodeDump(): void {
    this.sendHostCommand('bytecode');
  }

  public async showDebugOptionsMenu(): Promise<void> {
    const choice = await vscode.window.showQuickPick([
      {
        label: this.stepGranularity === 'Line' ? '$(check) Line' : '    Line',
        granularity: 'Line' as const,
        description: 'Step by source line'
      },
      {
        label: this.stepGranularity === 'Instruction' ? '$(check) Instruction' : '    Instruction',
        granularity: 'Instruction' as const,
        description: 'Step by bytecode instruction'
      }
    ], {
      placeHolder: 'Step granularity',
    });

    if (!choice) {
      return;
    }

    this.stepGranularity = choice.granularity;
    this.sendHostCommand(`stepmode ${choice.granularity.toLowerCase()}`);
  }

  public toggleBytecodeView(): void {
    if (panelHost.isOpen()) {
      panelHost.disposePanel();
      this.bytecodeShellReady = false;
      return;
    }

    panelHost.showPlaceholder();
    this.bytecodeShellReady = false;
    this.requestBytecodeDump();
  }

  protected setBreakPointsRequest(
    response: DebugProtocol.SetBreakpointsResponse,
    args: DebugProtocol.SetBreakpointsArguments
  ): void {
    const sourcePath = this.normalizeSourcePath(args.source?.path ?? args.source?.name);
    const lines = (args.breakpoints ?? [])
      .map((breakpoint) => breakpoint.line)
      .filter((line) => Number.isFinite(line) && line > 0);

    this.trace(`[okojo] setBreakPoints ${sourcePath} -> [${lines.join(', ')}]\n`);

    if (!existsSync(sourcePath)) {
      this.sendEvent(new OutputEvent(
        `[okojo] ignoring breakpoints for missing file ${sourcePath}\n`,
        'console'
      ));
      this.pendingBreakpointsBySource.delete(sourcePath);
      response.body = {
        breakpoints: lines.map((line) => ({ verified: false, line })),
      };
      this.sendResponse(response);
      return;
    }

    this.pendingBreakpointsBySource.set(sourcePath, { sourcePath, lines });
    if (this.hostStarted) {
      this.refreshHostBreakpointsForSource(sourcePath, lines);
    }

    response.body = {
      breakpoints: this.breakpointStore.getResponseBreakpoints(sourcePath, lines),
    };
    this.sendResponse(response);
  }

  protected stackTraceRequest(
    response: DebugProtocol.StackTraceResponse,
    _args: DebugProtocol.StackTraceArguments
  ): void {
    if (this.traceAdapter) {
      this.sendEvent(new OutputEvent(
        `[okojo] stackTraceRequest paused=${this.paused}\n`,
        'console'
      ));
    }
    if (this.paused) {
      this.pauseWasObserved = true;
    }
    const snapshot = this.lastSnapshot;
    const frames = snapshot?.stackFrames ?? (snapshot?.currentFrame ? [snapshot.currentFrame] : []);
    response.body = {
      stackFrames: frames.map((frame, index) => this.toStackFrame(frame, index + 1)),
      totalFrames: frames.length,
    };
    this.sendResponse(response);
  }

  protected scopesRequest(
    response: DebugProtocol.ScopesResponse,
    args: DebugProtocol.ScopesArguments
  ): void {
    if (this.traceAdapter) {
      this.sendEvent(new OutputEvent(
        `[okojo] scopesRequest paused=${this.paused}\n`,
        'console'
      ));
    }
    if (this.paused) {
      this.pauseWasObserved = true;
    }
    const snapshot = this.lastSnapshot;
    const frameId = Math.max(1, Number(args.frameId ?? 1));
    const scopeChain = snapshot?.scopeChain ?? [];
    const scopes: DebugProtocol.Scope[] = [];

    if (scopeChain.length > 0) {
      const startIndex = Math.min(frameId - 1, scopeChain.length - 1);
      for (let index = startIndex; index < scopeChain.length; index++) {
        const scope = scopeChain[index];
        const scopeName = index === startIndex
          ? `Locals${scope.frameInfo.functionName ? ` (${scope.frameInfo.functionName})` : ''}`
          : `Captured ${index - startIndex}`;
        const ref = this.registerScope({
          name: scopeName,
          variables: (scope.localValues ?? []).map((local) => ({
            name: local.name,
            value: local.value ?? '',
          })),
        });

        scopes.push({
          name: scopeName,
          variablesReference: ref,
          expensive: false,
        });
      }
    } else if (snapshot) {
      const values = snapshot.localValues ?? [];
      const ref = this.registerScope({
        name: 'Locals',
        variables: values.map((local) => ({
          name: local.name,
          value: local.value ?? '',
        })),
      });

      scopes.push({
        name: 'Locals',
        variablesReference: ref,
        expensive: false,
      });
    }

    response.body = { scopes };
    this.sendResponse(response);
  }

  protected variablesRequest(
    response: DebugProtocol.VariablesResponse,
    args: DebugProtocol.VariablesArguments
  ): void {
    if (this.traceAdapter) {
      this.sendEvent(new OutputEvent(
        `[okojo] variablesRequest paused=${this.paused}\n`,
        'console'
      ));
    }
    if (this.paused) {
      this.pauseWasObserved = true;
    }
    const scope = this.scopesByReference.get(args.variablesReference);
    if (!scope) {
      response.body = { variables: [] };
      this.sendResponse(response);
      return;
    }

    response.body = {
      variables: scope.variables.map((variable) => ({
        name: variable.name,
        value: variable.value,
        variablesReference: 0,
      })),
    };
    this.sendResponse(response);
  }

  protected evaluateRequest(
    response: DebugProtocol.EvaluateResponse,
    args: DebugProtocol.EvaluateArguments
  ): void {
    if (this.traceAdapter) {
      this.sendEvent(new OutputEvent(
        `[okojo] evaluateRequest paused=${this.paused}\n`,
        'console'
      ));
    }
    if (this.paused) {
      this.pauseWasObserved = true;
    }
    const expression = (args.expression ?? '').trim();
    if (expression.length === 0) {
      response.body = {
        result: '',
        variablesReference: 0,
      };
      this.sendResponse(response);
      return;
    }

    const requestId = this.nextHostEvaluateRequestId++;
    this.pendingEvaluateResponses.set(requestId, response);
    this.sendHostCommand(`evaluate ${requestId} ${Math.max(1, Number(args.frameId ?? 1))} ${JSON.stringify(expression)}`);
  }

  protected exceptionInfoRequest(
    response: DebugProtocol.ExceptionInfoResponse,
    _args: DebugProtocol.ExceptionInfoArguments
  ): void {
    const message = this.lastSnapshot?.summary ?? 'Paused execution';
    response.body = {
      exceptionId: this.lastStopKind,
      breakMode: 'always',
      description: message,
      details: {
        typeName: this.lastStopKind,
      },
    };
    this.sendResponse(response);
  }

  private async startHostIfNeeded(): Promise<void> {
    if (this.hostStarted || !this.configurationDone || !this.launchArgs) {
      return;
    }

    const args = this.launchArgs;
    const cwd = this.normalizeWorkingDirectory(args.cwd);
    const program = this.resolvePath(args.program, cwd);
    const project = this.resolveDebugServerProject(args.debugServerProject, cwd, program);
    const checkInterval = this.resolveCheckInterval(args.checkInterval);
    const stepGranularity = this.resolveStepGranularity(args.stepGranularity);
    this.traceAdapter = !!args.traceAdapter;

    const dotnetArgs = [
      'run',
      '--project',
      project,
      '--',
      '--script',
      program,
      '--cwd',
      cwd,
      '--check-interval',
      String(checkInterval),
      '--step-granularity',
      stepGranularity,
    ];
    this.stepGranularity = stepGranularity === 'instruction' ? 'Instruction' : 'Line';
    if (args.moduleEntry) {
      dotnetArgs.push('--module-entry');
    }
    if (args.enableSourceMaps) {
      dotnetArgs.push('--enable-source-maps');
    }

    if (args.stopOnCall) {
      dotnetArgs.push('--stop-call');
    }
    if (args.stopOnReturn) {
      dotnetArgs.push('--stop-return');
    }
    if (args.stopOnPump) {
      dotnetArgs.push('--stop-pump');
    }
    if (args.stopOnSuspendGenerator) {
      dotnetArgs.push('--stop-suspend');
    }
    if (args.stopOnResumeGenerator) {
      dotnetArgs.push('--stop-resume');
    }
    if (args.stopOnPeriodic) {
      dotnetArgs.push('--stop-periodic');
    }
    if (args.stopOnEntry) {
      dotnetArgs.push('--stop-entry');
    }
    if (args.stopOnDebuggerStatement === false) {
      dotnetArgs.push('--no-stop-debugger');
    }
    if (args.stopOnBreakpoint === false) {
      dotnetArgs.push('--no-stop-breakpoint');
    }

    for (const { sourcePath, lines } of this.pendingBreakpointsBySource.values()) {
      this.trace(`[okojo] launch breakpoints ${sourcePath} -> [${lines.join(', ')}]\n`);
      for (const line of lines) {
        dotnetArgs.push('--break', `${sourcePath}:${line}`);
      }
    }

    this.sendEvent(
      new OutputEvent(
        `[okojo] adapter ${OkojoDebugSession.adapterVersion} launching dotnet ${dotnetArgs.join(' ')}\n`,
        'console'
      )
    );

    this.hostProcess = spawn('dotnet', dotnetArgs, {
      cwd: path.dirname(project),
      env: {
        ...process.env,
        ...(args.traceBreakpoints ? { okojo_DEBUG_TRACE_BREAKPOINTS: '1' } : {}),
      },
      stdio: 'pipe',
    });
    this.hostStarted = true;

    const stdout = readline.createInterface({
      input: this.hostProcess.stdout,
      crlfDelay: Infinity,
    });
    stdout.on('line', (line) => this.handleHostLine(line));

    this.hostProcess.stderr.on('data', (chunk: Buffer) => {
      this.sendEvent(new OutputEvent(String(chunk), 'stderr'));
    });

    this.hostProcess.once('error', (err) => {
      this.sendEvent(new OutputEvent(`[okojo] debug host failed: ${String(err)}\n`, 'stderr'));
      this.sendEvent(new TerminatedEvent());
      this.hostTerminated = true;
    });

    this.hostProcess.once('exit', (code, signal) => {
      if (this.hostTerminated) {
        return;
      }

      this.hostTerminated = true;
      this.bytecodeShellReady = false;
      if (panelHost.isOpen()) {
        panelHost.showPlaceholder();
      }
      const message = signal ? `signal ${signal}` : `exit ${code ?? 0}`;
      this.sendEvent(new OutputEvent(`[okojo] debug host ended with ${message}\n`, 'console'));
      this.sendEvent(new TerminatedEvent());
    });
  }

  private shutdownHost(): void {
    if (!this.hostStarted || !this.hostProcess) {
      return;
    }

    try {
      this.sendHostCommand('quit');
    } catch {
      // ignore
    }

    try {
      this.hostProcess.stdin.end();
    } catch {
      // ignore
    }

    try {
      this.hostProcess.kill();
    } catch {
      // ignore
    }

    this.hostProcess = undefined;
    this.hostStarted = false;
  }

  private handleHostLine(line: string): void {
    const parsed = this.tryParseJson(line);
    if (!parsed) {
      this.sendEvent(new OutputEvent(`${line}\n`, 'console'));
      return;
    }

    const message = parsed as HostEventMessage & { [key: string]: unknown };
    switch (message.event) {
      case 'stopped':
        this.lastSnapshot = message as HostStoppedMessage;
        if (this.handlePendingSourceMappedNext(this.lastSnapshot)) {
          return;
        }
        this.lastStopKind = this.mapStopReason(this.lastSnapshot.kind);
        this.scopesByReference.clear();
        this.paused = true;
        this.pauseTimestamp = Date.now();
        this.pauseWasObserved = false;
        this.sendEvent(new OutputEvent(
          `[okojo] stopped ${this.lastStopKind} ${this.lastSnapshot.sourceLocation?.sourcePath ?? ''}:${this.lastSnapshot.sourceLocation?.line ?? 0}\n`,
          'console'
        ));
        {
          const event = new StoppedEvent(this.lastStopKind, this.threadId, this.lastSnapshot.summary);
          (event.body as DebugProtocol.StoppedEvent['body']).allThreadsStopped = true;
          this.sendEvent(event);
        }
        if (this.lastStopKind !== 'entry' && (panelHost.isOpen() || this.shouldAutoOpenBytecodePanel())) {
          this.requestBytecodeDump();
        }
        return;
      case 'breakpoint-added':
        this.recordBreakpointHandle(message as HostBreakpointAddedMessage);
        return;
      case 'breakpoint-updated':
        this.updateBreakpointHandle(message as HostBreakpointUpdatedMessage);
        return;
      case 'breakpoint-cleared':
        this.forgetBreakpointHandle(message as HostBreakpointClearedMessage);
        return;
      case 'bytecode':
        void this.openBytecodeView(message as HostBytecodeMessage);
        return;
      case 'option-updated':
        this.applyOptionUpdate(message as HostOptionUpdatedMessage);
        return;
      case 'evaluate':
        this.handleEvaluateResult(message as HostEvaluateMessage);
        return;
      case 'error':
        this.sendEvent(new OutputEvent(this.formatHostError(message as HostErrorMessage), 'stderr'));
        return;
      case 'terminated':
        this.hostTerminated = true;
        this.paused = false;
        this.bytecodeShellReady = false;
        this.pendingSourceMappedNext = undefined;
        this.sendEvent(new TerminatedEvent());
        return;
      default:
        this.sendEvent(new OutputEvent(`${line}\n`, 'console'));
        return;
    }
  }

  private refreshHostBreakpointsForSource(sourcePath: string, lines: number[]): void {
    this.trace(`[okojo] refresh breakpoints ${sourcePath} -> [${lines.join(', ')}]\n`);

    const knownHandles = this.breakpointHandlesBySource.get(sourcePath);
    if (knownHandles) {
      for (const handleId of knownHandles) {
        this.sendHostCommand(`clear ${handleId}`);
      }
      knownHandles.clear();
    }

    for (const line of lines) {
      this.sendHostCommand(`break ${sourcePath}:${line}`);
    }
  }

  private sendHostCommand(command: string): void {
    if (!this.hostProcess) {
      return;
    }

    this.trace(`[okojo] -> host ${command}\n`);
    this.hostProcess.stdin.write(`${command}\n`);
  }

  private recordBreakpointHandle(message: HostBreakpointAddedMessage): void {
    if (message.sourcePath == null) {
      return;
    }

    const sourcePath = this.normalizeSourcePath(message.sourcePath);
    this.trace(`[okojo] breakpoint-added ${sourcePath}:${message.requestedLine} handle ${message.handleId}\n`);
    let handles = this.breakpointHandlesBySource.get(sourcePath);
    if (!handles) {
      handles = new Set<number>();
      this.breakpointHandlesBySource.set(sourcePath, handles);
    }

    handles.add(message.handleId);
    this.handleSourceById.set(message.handleId, sourcePath);
    this.applyBreakpointUpdate(message);
  }

  private forgetBreakpointHandle(message: HostBreakpointClearedMessage): void {
    const sourcePath = this.handleSourceById.get(message.handleId);
    if (!sourcePath) {
      return;
    }

    const handles = this.breakpointHandlesBySource.get(sourcePath);
    if (handles) {
      handles.delete(message.handleId);
      if (handles.size === 0) {
        this.breakpointHandlesBySource.delete(sourcePath);
      }
    }

    this.handleSourceById.delete(message.handleId);
    this.breakpointStore.removeHandle(message.handleId);
  }

  private updateBreakpointHandle(message: HostBreakpointUpdatedMessage): void {
    this.applyBreakpointUpdate(message);
  }

  private applyBreakpointUpdate(message: HostBreakpointAddedMessage | HostBreakpointUpdatedMessage): void {
    const breakpoint = this.breakpointStore.applyUpdate(
      (sourcePath) => this.normalizeSourcePath(sourcePath),
      message
    );
    this.sendEvent(this.breakpointStore.toChangedEvent(breakpoint));
  }

  private registerScope(scope: DapScopeEntry): number {
    const reference = this.nextVariablesReference++;
    this.scopesByReference.set(reference, scope);
    return reference;
  }

  private toStackFrame(frame: HostFrame, id: number): DebugProtocol.StackFrame {
    const fallbackLocation = this.lastSnapshot?.sourceLocation;
    const hasFrameLocation = frame.hasSourceLocation === true && (frame.sourceLine ?? 0) > 0;
    const sourcePath = hasFrameLocation
      ? (frame.sourcePath ?? fallbackLocation?.sourcePath)
      : fallbackLocation?.sourcePath;
    const line = Math.max(1, hasFrameLocation ? (frame.sourceLine ?? fallbackLocation?.line ?? 1) : (fallbackLocation?.line ?? 1));
    const column = Math.max(1, hasFrameLocation ? (frame.sourceColumn ?? fallbackLocation?.column ?? 1) : (fallbackLocation?.column ?? 1));
    const source = sourcePath
      ? { path: sourcePath, name: path.basename(sourcePath) }
      : undefined;

    return {
      id,
      name: frame.functionName ?? '<anonymous>',
      source,
      line,
      column,
    };
  }

  private mapStopReason(kind?: string): string {
    switch ((kind ?? '').toLowerCase()) {
      case 'entry':
        return 'entry';
      case 'step':
        return 'step';
      case 'breakpoint':
        return 'breakpoint';
      case 'debugger-statement':
        return 'pause';
      case 'suspend-generator':
      case 'resume-generator':
      case 'call':
      case 'return':
      case 'pump':
      case 'periodic':
        return 'pause';
      default:
        return 'pause';
    }
  }

  private shouldAutoOpenBytecodePanel(): boolean {
    try {
      return !!vscode.workspace.getConfiguration('okojo.debugger').get<boolean>('openOnStop', false);
    } catch {
      return false;
    }
  }

  private applyOptionUpdate(message: HostOptionUpdatedMessage): void {
    if (message.name === 'stepGranularity' && typeof message.value === 'string') {
      this.stepGranularity = message.value === 'Instruction' ? 'Instruction' : 'Line';
      if (this.suppressStepGranularityOutput) {
        this.suppressStepGranularityOutput = false;
        return;
      }
      this.sendEvent(new OutputEvent(`[okojo] step granularity: ${this.stepGranularity}\n`, 'console'));
      return;
    }

    this.sendEvent(new OutputEvent(`[okojo] option updated: ${message.name ?? 'unknown'}\n`, 'console'));
  }

  private normalizeSourcePath(sourcePath?: string | null): string {
    const cwd = this.normalizeWorkingDirectory(this.launchArgs?.cwd);
    if (!sourcePath || sourcePath.length === 0) {
      return cwd;
    }

    return this.resolvePath(sourcePath, cwd);
  }

  private tryStartSourceMappedNext(): boolean {
    if (!this.launchArgs?.enableSourceMaps || this.stepGranularity !== 'Line') {
      return false;
    }

    const sourcePath = this.lastSnapshot?.sourceLocation?.sourcePath;
    const line = this.lastSnapshot?.sourceLocation?.line;
    if (!sourcePath || !line || line <= 0) {
      return false;
    }

    this.pendingSourceMappedNext = {
      sourcePath: this.normalizeSourcePath(sourcePath),
      line,
      stackDepth: this.lastSnapshot?.stackFrames?.length ?? 1,
      restoreGranularity: this.stepGranularity,
    };

    this.paused = false;
    this.suppressStepGranularityOutput = true;
    this.sendHostCommand('stepmode instruction');
    this.sendHostCommand('step');
    return true;
  }

  private handlePendingSourceMappedNext(snapshot: HostStoppedMessage): boolean {
    const pending = this.pendingSourceMappedNext;
    if (!pending) {
      return false;
    }

    const sourcePath = snapshot.sourceLocation?.sourcePath
      ? this.normalizeSourcePath(snapshot.sourceLocation.sourcePath)
      : undefined;
    const line = snapshot.sourceLocation?.line ?? 0;
    const stackDepth = snapshot.stackFrames?.length ?? 1;
    const stillOnSameDisplayedLine = !!sourcePath
      && sourcePath === pending.sourcePath
      && line === pending.line
      && stackDepth >= pending.stackDepth;

    if (stillOnSameDisplayedLine) {
      this.paused = false;
      this.sendHostCommand('step');
      return true;
    }

    this.pendingSourceMappedNext = undefined;
    if (pending.restoreGranularity === 'Line') {
      this.suppressStepGranularityOutput = true;
      this.sendHostCommand('stepmode line');
    }

    return false;
  }

  private normalizeWorkingDirectory(cwd?: string): string {
    if (!cwd || cwd.length === 0) {
      return process.cwd();
    }

    return this.resolvePath(cwd, process.cwd());
  }

  private resolvePath(value: string, cwd: string): string {
    return path.isAbsolute(value) ? path.resolve(value) : path.resolve(cwd, value);
  }

  private resolveDebugServerProject(project?: string, cwd?: string, program?: string): string {
    const root = this.findWorkspaceRoot(cwd, program);
    const defaultProject = path.resolve(root, 'src', 'Okojo.DebugServer', 'Okojo.DebugServer.csproj');
    if (!project || project.length === 0) {
      return defaultProject;
    }

    return this.resolvePath(project, root);
  }

  private findWorkspaceRoot(cwd?: string, program?: string): string {
    const candidates = [cwd, program ? path.dirname(program) : undefined, process.cwd()]
      .filter((candidate): candidate is string => typeof candidate === 'string' && candidate.length > 0);

    for (const candidate of candidates) {
      let current = path.resolve(candidate);
      while (true) {
        const project = path.join(current, 'src', 'Okojo.DebugServer', 'Okojo.DebugServer.csproj');
        if (existsSync(project)) {
          return current;
        }

        const parent = path.dirname(current);
        if (parent === current) {
          break;
        }

        current = parent;
      }
    }

    return process.cwd();
  }

  private resolveCheckInterval(raw?: number): number {
    const value = Number(raw ?? 1024);
    if (!Number.isFinite(value) || value < 1) {
      return 1024;
    }

    return Math.floor(value);
  }

  private resolveStepGranularity(raw?: string): 'line' | 'instruction' {
    const value = String(raw ?? '').toLowerCase();
    if (value === 'instruction' || value === 'pc') {
      return 'instruction';
    }

    return 'line';
  }

  private tryParseJson(line: string): unknown {
    try {
      return JSON.parse(line);
    } catch {
      return undefined;
    }
  }

  private formatHostError(message: HostErrorMessage): string {
    const parts = [
      '[okojo] ',
      message.type ? `${message.type}: ` : '',
      message.message ?? 'debug host error',
    ];

    if (message.stack) {
      parts.push(`\n${message.stack}`);
    }

    parts.push('\n');
    return parts.join('');
  }

  private handleEvaluateResult(message: HostEvaluateMessage): void {
    const requestId = Number(message.requestId ?? 0);
    const response = this.pendingEvaluateResponses.get(requestId);
    if (!response) {
      return;
    }

    this.pendingEvaluateResponses.delete(requestId);
    if (message.success) {
      response.body = {
        result: message.result ?? '',
        variablesReference: 0,
      };
      this.sendResponse(response);
      return;
    }

    this.sendErrorResponse(response, 2002, message.message ?? 'Evaluation failed.');
  }

  private async openBytecodeView(message: HostBytecodeMessage): Promise<void> {
    if (!this.bytecodeShellReady) {
      panelHost.setHtml(renderBytecodeShellHtml());
      this.bytecodeShellReady = true;
    }

    await panelHost.postMessage({
      type: 'bytecode-update',
      payload: buildBytecodeViewModel(message),
    });
    this.trace(`[okojo] opened ${message.title ?? 'Okojo Bytecode'}\n`);
  }

  private trace(text: string): void {
    if (!this.traceAdapter) {
      return;
    }

    this.sendEvent(new OutputEvent(text, 'console'));
  }
}

type SourceMappedNextState = {
  sourcePath: string;
  line: number;
  stackDepth: number;
  restoreGranularity: 'Line' | 'Instruction';
};
