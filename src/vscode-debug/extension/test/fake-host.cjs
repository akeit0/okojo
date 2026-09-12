// Simulated engine HOST for adapter contract tests. This is not a JS interpreter.
const readline = require('node:readline');
const fs = require('node:fs');
const program = process.argv[process.argv.indexOf('--script') + 1];
const source = fs.readFileSync(program, 'utf8');
const mode = source.split('\n')[0];
let generation = 0;
let resumed = 0;
let paused = true;
let breakpoints = [];
const send = message => process.stdout.write(`${JSON.stringify(message)}\n`);
const response = (id, body = {}) => ({ event: 'response', requestId: id, success: true, body });
const frame = (name, line) => ({ functionName: name, programCounter: line * 3,
  hasSourceLocation: true, sourcePath: program, sourceLine: line, sourceColumn: 1 });
const stop = (kind, line = 3) => {
  generation++;
  paused = true;
  return { event: 'stopped', kind, summary: `${kind} at ${program}:${line}`, sourceLocation: { sourcePath: program, line, column: 1 },
    currentFrame: frame('inner', line), stackFrames: [frame('inner', line), frame('outer', 7)], scopeChain: [] };
};
if (process.env.FAKE_STARTUP_CRASH) process.exit(11);
const entryTimer = setTimeout(() => send({ event: 'stopped', kind: 'entry',
  currentFrame: frame('<entry>', 1), stackFrames: [frame('<entry>', 1)],
  sourceLocation: { sourcePath: program, line: 1, column: 1 } }), Number(process.env.FAKE_STARTUP_DELAY ?? 0));
const lines = readline.createInterface({ input: process.stdin });
lines.on('line', line => {
  if (line === 'quit') { clearTimeout(entryTimer); lines.close(); process.exit(0); }
  let request;
  try { request = JSON.parse(line); } catch { return; }
  const { id, command, arguments: args } = request;
  try {
    if (command === 'setBreakpoints') {
      const previous = breakpoints;
      breakpoints = args.breakpoints.map(bp => ({ event: 'breakpoint-updated', clientId: bp.id, handleId: bp.id,
        sourcePath: args.sourcePath, requestedLine: bp.line, verified: false }));
      send(response(id, { breakpoints }));
      // A late event for a removed handle must not resurrect a deleted breakpoint.
      for (const old of previous) if (!breakpoints.some(bp => bp.clientId === old.clientId)) {
        send({ ...old, verified: true, resolvedLine: old.requestedLine });
      }
      return;
    }
    if (command === 'setExceptionBreakpoints') { send(response(id, { breakpoints: [] })); return; }
    if (command === 'resume') {
      if (!paused) throw new Error('Execution is not paused.');
      paused = false;
      resumed++;
      const events = [response(id)];
      if (resumed === 1) {
        for (const bp of breakpoints) events.push({ ...bp, verified: true,
          resolvedSourcePath: bp.sourcePath, resolvedLine: bp.requestedLine === 2 ? 3 : bp.requestedLine, resolvedColumn: 1 });
      }
      if (mode.includes('infinite')) { /* wait for pause or quit */ }
      else if (resumed >= 3 || mode.includes('exit-fast')) {
        events.push({ event: 'output', category: 'stdout', output: 'result: 日本語\n' });
        events.push({ event: 'terminated', exitCode: 0 });
      } else {
        events.push(stop(mode.includes('exception') ? 'caught-exception' : args.mode === 'continue' ? 'debugger-statement' : 'step', resumed === 1 ? 3 : 5));
      }
      // One write deliberately exercises response/stopped ordering in one chunk.
      process.stdout.write(events.map(event => JSON.stringify(event)).join('\n') + '\n');
      if (events.some(event => event.event === 'terminated')) setImmediate(() => process.exit(0));
      return;
    }
    if (command === 'pause') {
      process.stdout.write(JSON.stringify(response(id)) + '\n' + JSON.stringify(stop('pause')) + '\n');
      return;
    }
    if (command === 'loadedSources') { send(response(id, { sources: [{ path: program }] })); return; }
    if (!paused) throw new Error('Execution is not paused.');
    if (command === 'scopes') {
      if (![1, 2].includes(args.frameId)) throw new Error('Invalid paused frame id.');
      send(response(id, { scopes: [{ name: 'Locals', variablesReference: generation * 100 + args.frameId, expensive: false }] }));
      return;
    }
    if (command === 'variables') {
      const ref = args.variablesReference - generation * 100;
      let values;
      if (ref === 1 || ref === 2) values = [
        { name: 'value', value: `Number(${ref === 1 ? 7 : 99})`, type: 'number', variablesReference: 0 },
        { name: 'object', value: 'JsPlainObject', type: 'object', variablesReference: generation * 100 + 3 },
      ];
      else if (ref === 3) values = [
        { name: 'self', value: 'JsPlainObject', variablesReference: generation * 100 + 3 },
        { name: 'danger', value: '<accessor>', variablesReference: 0 },
      ];
      else throw new Error('Unknown or expired variables reference.');
      send(response(id, { variables: values.slice(args.start ?? 0, args.count ? (args.start ?? 0) + args.count : undefined) }));
      return;
    }
    if (command === 'evaluate') {
      if (mode.includes('crash-request')) process.exit(12);
      if (mode.includes('timeout-request')) return;
      if (args.expression === 'object') send(response(id, { result: 'JsPlainObject', type: 'object', variablesReference: generation * 100 + 3 }));
      else if (args.expression === 'value') send(response(id, { result: `Number(${args.frameId === 1 ? 7 : 99})`, type: 'number', variablesReference: 0 }));
      else throw new Error('Only identifiers and safe property paths are supported.');
      return;
    }
    if (command === 'bytecode') {
      send(response(id));
      send({ event: 'bytecode', text: '.code\nDebugger', sourcePath: program });
      return;
    }
    throw new Error('Unsupported host request.');
  } catch (error) {
    send({ event: 'response', requestId: id, success: false, message: error.message });
  }
});
