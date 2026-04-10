import { DebugProtocol } from '@vscode/debugprotocol';

export type OkojoLaunchArguments = DebugProtocol.LaunchRequestArguments & {
  program: string;
  cwd?: string;
  debugServerProject?: string;
  checkInterval?: number;
  stepGranularity?: string;
  traceAdapter?: boolean;
  traceBreakpoints?: boolean;
  enableSourceMaps?: boolean;
  moduleEntry?: boolean;
  stopOnEntry?: boolean;
  stopOnDebuggerStatement?: boolean;
  stopOnBreakpoint?: boolean;
  stopOnCall?: boolean;
  stopOnReturn?: boolean;
  stopOnPump?: boolean;
  stopOnSuspendGenerator?: boolean;
  stopOnResumeGenerator?: boolean;
  stopOnPeriodic?: boolean;
};

export type HostSourceLocation = {
  sourcePath?: string | null;
  line: number;
  column: number;
};

export type HostFrame = {
  functionName?: string;
  programCounter: number;
  frameKind?: string;
  flags?: string;
  hasGeneratorState?: boolean;
  generatorState?: string;
  generatorSuspendId?: number;
  hasSourceLocation?: boolean;
  sourceLine?: number;
  sourceColumn?: number;
  sourcePath?: string | null;
};

export type HostLocalValue = {
  name: string;
  storageKind?: string;
  storageIndex?: number;
  value?: string;
  startPc?: number;
  endPc?: number;
  flags?: string;
};

export type HostScopeSnapshot = {
  framePointer: number;
  frameInfo: HostFrame;
  locals?: HostLocalValue[];
  localValues?: HostLocalValue[];
};

export type HostStoppedMessage = {
  event: 'stopped';
  kind?: string;
  summary?: string;
  sourceLocation?: HostSourceLocation | null;
  currentFrame?: HostFrame;
  stackFrames?: HostFrame[];
  locals?: HostLocalValue[];
  localValues?: HostLocalValue[];
  scopeChain?: HostScopeSnapshot[];
};

export type HostBreakpointAddedMessage = {
  event: 'breakpoint-added';
  sourcePath?: string | null;
  requestedLine: number;
  verified?: boolean;
  resolvedSourcePath?: string | null;
  resolvedLine?: number;
  resolvedColumn?: number;
  programCounter?: number;
  handleId: number;
};

export type HostBreakpointUpdatedMessage = {
  event: 'breakpoint-updated';
  sourcePath?: string | null;
  requestedLine: number;
  verified?: boolean;
  resolvedSourcePath?: string | null;
  resolvedLine?: number;
  resolvedColumn?: number;
  programCounter?: number;
  handleId: number;
};

export type HostBreakpointClearedMessage = {
  event: 'breakpoint-cleared';
  handleId: number;
};

export type HostBytecodeMessage = {
  event: 'bytecode';
  title?: string;
  sourcePath?: string | null;
  sourceLocation?: HostSourceLocation | null;
  programCounter?: number;
  text?: string;
};

export type HostOptionUpdatedMessage = {
  event: 'option-updated';
  name?: string;
  enabled?: boolean;
  value?: string;
};

export type HostEvaluateMessage = {
  event: 'evaluate';
  requestId?: number;
  success?: boolean;
  expression?: string;
  result?: string;
  message?: string;
};

export type HostErrorMessage = {
  event: 'error';
  type?: string;
  message?: string;
  stack?: string;
};

export type HostTerminatedMessage = {
  event: 'terminated';
  exitCode?: number;
};

export type HostEventMessage =
  | HostStoppedMessage
  | HostBreakpointAddedMessage
  | HostBreakpointUpdatedMessage
  | HostBreakpointClearedMessage
  | HostBytecodeMessage
  | HostOptionUpdatedMessage
  | HostEvaluateMessage
  | HostErrorMessage
  | HostTerminatedMessage;

export type DapVariableEntry = {
  name: string;
  value: string;
};

export type DapScopeEntry = {
  name: string;
  variables: DapVariableEntry[];
};

export type BreakpointRequestState = {
  sourcePath: string;
  lines: number[];
};

export type BreakpointUiState = {
  id: number;
  sourcePath: string;
  requestedLine: number;
  verified: boolean;
  resolvedSourcePath?: string;
  resolvedLine?: number;
  resolvedColumn?: number;
  programCounter?: number;
};
