import { ChildProcessWithoutNullStreams, execFile, spawn } from 'node:child_process';
import * as readline from 'node:readline';

export type HostMessage = { event: string; [key: string]: any };
export interface HostLaunch {
  command: string;
  args: string[];
  cwd: string;
  env?: NodeJS.ProcessEnv;
  startupTimeout?: number;
  requestTimeout?: number;
}
export interface DebugHost {
  readonly ready: Promise<HostMessage>;
  request(command: string, args?: Record<string, any>): Promise<Record<string, any>>;
  stop(): void;
}
export type HostFactory = (launch: HostLaunch, onEvent: (message: HostMessage) => void) => DebugHost;

/** Private newline-JSON host RPC, not DAP. Only this module knows the host transport. */
export class HostClient implements DebugHost {
  public readonly ready: Promise<HostMessage>;
  private readonly child: ChildProcessWithoutNullStreams;
  private readonly lines: readline.Interface;
  private readonly pending = new Map<number, {
    resolve: (body: Record<string, any>) => void;
    reject: (error: Error) => void;
    timer: NodeJS.Timeout;
  }>();
  private nextId = 1;
  private ended = false;
  private stopping = false;
  private readonly startupTimer: NodeJS.Timeout;
  private readyResolve!: (entry: HostMessage) => void;
  private readyReject!: (error: Error) => void;

  public constructor(private readonly launch: HostLaunch, private readonly onEvent: (event: HostMessage) => void) {
    this.ready = new Promise((resolve, reject) => { this.readyResolve = resolve; this.readyReject = reject; });
    this.child = spawn(launch.command, launch.args, {
      cwd: launch.cwd, env: launch.env ?? process.env,
      stdio: 'pipe', windowsHide: true, detached: process.platform !== 'win32', shell: false,
    });
    this.startupTimer = setTimeout(() => {
      const error = new Error('Okojo host did not reach its configuration stop before startupTimeout.');
      this.close(error, 1);
      this.stop();
    }, launch.startupTimeout ?? 120_000);
    this.lines = readline.createInterface({ input: this.child.stdout, crlfDelay: Infinity });
    this.lines.on('line', line => this.receive(line));
    this.child.stderr.on('data', chunk => this.emit({ event: 'output', category: 'stderr', output: String(chunk) }));
    this.child.stdin.on('error', error => this.close(error, 1));
    this.child.stdout.on('error', error => this.close(error, 1));
    this.child.stderr.on('error', error => this.close(error, 1));
    this.child.once('error', error => this.close(error, 1));
    this.child.once('close', (code, signal) => this.close(
      new Error(`Debug host closed (${signal ?? code ?? 'unknown'}).`), code ?? (signal ? 1 : 0)));
  }

  public request(command: string, args: Record<string, any> = {}): Promise<Record<string, any>> {
    if (this.ended || this.stopping || this.child.stdin.destroyed) {
      return Promise.reject(new Error('The debug host is not connected.'));
    }
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Okojo host request '${command}' timed out.`));
      }, this.launch.requestTimeout ?? 10_000);
      this.pending.set(id, { resolve, reject, timer });
      this.child.stdin.write(`${JSON.stringify({ id, command, arguments: args })}\n`);
    });
  }

  private receive(line: string): void {
    let message: HostMessage;
    try { message = JSON.parse(line); }
    catch { this.emit({ event: 'output', category: 'console', output: `${line}\n` }); return; }
    if (!message || typeof message !== 'object' || typeof message.event !== 'string') {
      this.emit({ event: 'output', category: 'console', output: `${line}\n` });
      return;
    }
    if (message.event === 'response') {
      const pending = this.pending.get(message.requestId);
      if (!pending) return;
      this.pending.delete(message.requestId);
      clearTimeout(pending.timer);
      if (message.success === true) pending.resolve(message.body ?? {});
      else pending.reject(new Error(message.message ?? 'Okojo host request failed.'));
      return;
    }
    if (message.event === 'stopped' && message.kind === 'entry') {
      clearTimeout(this.startupTimer);
      this.readyResolve(message);
      return;
    }
    this.emit(message);
  }

  private emit(message: HostMessage): void {
    // Deliver RPC responses before the following stop, even when the pipe puts
    // response + stopped in the same chunk. This avoids continued-after-stopped.
    queueMicrotask(() => this.onEvent(message));
  }

  private close(error: Error, exitCode: number): void {
    if (this.ended) return;
    this.ended = true;
    clearTimeout(this.startupTimer);
    this.readyReject(error);
    for (const request of this.pending.values()) {
      clearTimeout(request.timer);
      request.reject(error);
    }
    this.pending.clear();
    this.lines.close();
    this.emit({ event: 'host-exit', exitCode, message: error.message });
  }

  public stop(): void {
    if (this.stopping) return;
    this.stopping = true;
    clearTimeout(this.startupTimer);
    // dotnet run can have a child host: terminate the process group/tree, not
    // only the CLI parent. Never construct a shell command from a launch path.
    try { if (!this.child.stdin.destroyed) this.child.stdin.end('quit\n'); } catch { /* closed pipe */ }
    const pid = this.child.pid;
    if (!pid) return;
    const kill = () => {
      if (process.platform === 'win32') {
        if (!this.ended) execFile('taskkill', ['/PID', String(pid), '/T', '/F'], { windowsHide: true }, () => {});
      } else {
        try { process.kill(-pid, 'SIGKILL'); } catch { /* group already exited */ }
      }
    };
    const timer = setTimeout(kill, 500);
    timer.unref();
  }
}
