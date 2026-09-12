import { HostMessage } from './hostClient';

export interface BreakpointState {
  id: number;
  sourcePath: string;
  requestedLine: number;
  verified: boolean;
  resolvedSourcePath?: string;
  resolvedLine?: number;
  resolvedColumn?: number;
  message?: string;
}

/** Stable DAP ids; late host events can never resurrect deleted breakpoints. */
export class BreakpointStore {
  private nextId = 1;
  private readonly bySource = new Map<string, Map<number, BreakpointState>>();
  private readonly byId = new Map<number, BreakpointState>();

  public replace(sourcePath: string, lines: number[]): BreakpointState[] {
    const previous = this.bySource.get(sourcePath) ?? new Map<number, BreakpointState>();
    const current = new Map<number, BreakpointState>();
    for (const line of lines) {
      if (current.has(line)) continue;
      const state = previous.get(line) ?? {
        id: this.nextId++, sourcePath, requestedLine: line, verified: false,
      };
      current.set(line, state);
      this.byId.set(state.id, state);
    }
    for (const [line, state] of previous) {
      if (!current.has(line)) this.byId.delete(state.id);
    }
    this.bySource.set(sourcePath, current);
    return lines.map(line => current.get(line)!);
  }

  public sources(): IterableIterator<[string, Map<number, BreakpointState>]> {
    return this.bySource.entries();
  }

  public applyUpdate(message: HostMessage): BreakpointState | undefined {
    const state = this.byId.get(message.clientId);
    if (!state) return undefined;
    state.verified = message.verified === true;
    state.resolvedSourcePath = message.resolvedSourcePath ?? undefined;
    state.resolvedLine = message.resolvedLine > 0 ? message.resolvedLine : undefined;
    state.resolvedColumn = message.resolvedColumn > 0 ? message.resolvedColumn : undefined;
    state.message = state.verified ? undefined : 'Pending runtime source resolution';
    return state;
  }
}
