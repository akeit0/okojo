#!/usr/bin/env node
import { OkojoDebugSession } from './adapter';
import { DapDecoder, encodeMessage } from './dapProtocol';

const session = new OkojoDebugSession();
const decoder = new DapDecoder();
let closing = false;
function close(error?: unknown): void {
  if (closing) return;
  closing = true;
  if (error) {
    process.stderr.write(`[okojo-dap] ${error instanceof Error ? error.message : String(error)}\n`);
    process.exitCode = 1;
  }
  session.dispose();
  process.stdin.pause();
}
session.onDidSendMessage(message => {
  // stdout is reserved exclusively for DAP frames, including launch diagnostics.
  if (!process.stdout.write(encodeMessage(message))) process.stdin.pause();
});
process.stdout.on('drain', () => { if (!closing) process.stdin.resume(); });
process.stdout.on('error', error => close(error));
process.stdin.on('error', error => close(error));
process.stdin.on('data', (data: Buffer) => {
  try { for (const message of decoder.push(data)) session.handleMessage(message); }
  catch (error) { close(error); }
});
process.stdin.on('end', () => {
  try { decoder.finish(); close(); } catch (error) { close(error); }
});
process.on('SIGTERM', () => close());
process.on('SIGINT', () => close());
