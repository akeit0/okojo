import * as path from 'node:path';
import { DebugProtocol } from '@vscode/debugprotocol';
import { BreakpointEvent } from '@vscode/debugadapter';
import {
  BreakpointUiState,
  HostBreakpointAddedMessage,
  HostBreakpointUpdatedMessage,
} from './debugTypes';

export class BreakpointStore {
  private readonly breakpointUiStateByKey = new Map<string, BreakpointUiState>();
  private readonly breakpointIdByHandle = new Map<number, number>();
  private nextBreakpointId = 1;

  public getResponseBreakpoints(sourcePath: string, lines: number[]): DebugProtocol.Breakpoint[] {
    const requestedKeys = new Set<string>();
    const breakpoints = lines.map((line) => {
      const state = this.getOrCreate(sourcePath, line);
      requestedKeys.add(this.getKey(sourcePath, line));
      return {
        id: state.id,
        verified: state.verified,
        line: state.resolvedLine ?? state.requestedLine,
        column: state.resolvedColumn && state.resolvedColumn > 0 ? state.resolvedColumn : undefined,
        message: this.formatMessage(state),
      };
    });

    this.pruneForSource(sourcePath, requestedKeys);
    return breakpoints;
  }

  public applyUpdate(
    normalizeSourcePath: (sourcePath?: string | null) => string,
    message: HostBreakpointAddedMessage | HostBreakpointUpdatedMessage
  ): DebugProtocol.Breakpoint {
    const requestedSourcePath = normalizeSourcePath(message.sourcePath);
    const state = this.getOrCreate(requestedSourcePath, message.requestedLine);
    state.verified = !!message.verified;
    state.resolvedSourcePath = message.resolvedSourcePath
      ? normalizeSourcePath(message.resolvedSourcePath)
      : undefined;
    state.resolvedLine = message.resolvedLine && message.resolvedLine > 0 ? message.resolvedLine : undefined;
    state.resolvedColumn = message.resolvedColumn && message.resolvedColumn > 0 ? message.resolvedColumn : undefined;
    state.programCounter = message.programCounter && message.programCounter >= 0 ? message.programCounter : undefined;
    this.breakpointIdByHandle.set(message.handleId, state.id);

    return {
      id: state.id,
      verified: state.verified,
      source: {
        path: state.resolvedSourcePath ?? state.sourcePath,
        name: path.basename(state.resolvedSourcePath ?? state.sourcePath),
      },
      line: state.resolvedLine ?? state.requestedLine,
      column: state.resolvedColumn && state.resolvedColumn > 0 ? state.resolvedColumn : undefined,
      message: this.formatMessage(state),
    };
  }

  public removeHandle(handleId: number): void {
    this.breakpointIdByHandle.delete(handleId);
  }

  public toChangedEvent(breakpoint: DebugProtocol.Breakpoint): BreakpointEvent {
    return new BreakpointEvent('changed', breakpoint);
  }

  private getKey(sourcePath: string, requestedLine: number): string {
    return `${sourcePath}#${requestedLine}`;
  }

  private getOrCreate(sourcePath: string, requestedLine: number): BreakpointUiState {
    const key = this.getKey(sourcePath, requestedLine);
    let state = this.breakpointUiStateByKey.get(key);
    if (!state) {
      state = {
        id: this.nextBreakpointId++,
        sourcePath,
        requestedLine,
        verified: false,
      };
      this.breakpointUiStateByKey.set(key, state);
    }

    return state;
  }

  private pruneForSource(sourcePath: string, requestedKeys: Set<string>): void {
    for (const [key, state] of this.breakpointUiStateByKey) {
      if (state.sourcePath === sourcePath && !requestedKeys.has(key)) {
        this.breakpointUiStateByKey.delete(key);
      }
    }
  }

  private formatMessage(state: BreakpointUiState): string | undefined {
    if (!state.verified) {
      return 'Pending runtime resolution';
    }

    if (state.resolvedLine !== undefined && state.resolvedLine !== state.requestedLine) {
      return `Relocated from line ${state.requestedLine} to ${state.resolvedLine}`;
    }

    if (state.programCounter !== undefined) {
      return `Bound at pc ${state.programCounter}`;
    }

    return undefined;
  }
}
