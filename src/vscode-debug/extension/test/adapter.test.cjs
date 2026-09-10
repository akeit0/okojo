const canonicalPath = value => process.platform === 'win32' ? value.toLowerCase() : value;
const test = require('node:test');
const assert = require('node:assert/strict');
const { pathToFileURL } = require('node:url');
const { Client, workspace } = require('./client.cjs');

async function setup(t, text, launchArgs = {}, initArgs = {}) {
  const ws = workspace(text);
  const client = new Client();
  t.after(async () => { await client.stop(); ws.dispose(); });
  const initialized = await client.initialize(initArgs);
  const launch = await client.launch(ws.program, launchArgs);
  return { client, ws, launch, initialized };
}

test('initialize negotiates only implemented capabilities; launch is a real configuration barrier', async t => {
  const { client, launch, initialized } = await setup(t, '// normal\n');
  assert.equal(initialized.body.supportsConfigurationDoneRequest, true);
  assert.equal(initialized.body.supportsConditionalBreakpoints, undefined);
  assert.equal(initialized.body.supportsSetVariable, undefined);
  assert.equal(client.messages.some(message => message.command === 'launch' && message.type === 'response'), false);
  assert.equal(client.messages.some(message => message.event === 'stopped'), false);
  await client.configure(launch);
  const stop = await client.event('stopped');
  assert.equal(stop.body.reason, 'breakpoint');
  const commands = client.messages.filter(message => message.type === 'response').map(message => message.command);
  assert.ok(commands.indexOf('configurationDone') < commands.indexOf('launch'));
});

test('stopOnEntry is visible only after configurationDone and can continue immediately', async t => {
  const { client, launch } = await setup(t, '// normal\n', { stopOnEntry: true });
  await client.configure(launch);
  assert.equal((await client.event('stopped')).body.reason, 'entry');
  const continued = await client.send('continue', { threadId: 1 });
  assert.equal(continued.success, true);
  assert.equal(continued.body.allThreadsContinued, true);
  const stopped = await client.event('stopped');
  assert.equal(stopped.body.reason, 'breakpoint');
  const continuedEvent = client.messages.findLastIndex(message => message.event === 'continued');
  const stoppedEvent = client.messages.indexOf(stopped);
  assert.ok(client.messages.indexOf(continued) < continuedEvent && continuedEvent < stoppedEvent);
});

test('source paths with spaces/Unicode, source retrieval, breakpoint relocation and replacement', async t => {
  const { client, ws, launch } = await setup(t, '// normal 日本語\nconst value = 7;\ndebugger;\n');
  const set = await client.send('setBreakpoints', { source: { path: ws.program }, breakpoints: [{ line: 2 }, { line: 4, condition: 'value > 0' }] });
  assert.equal(set.success, true);
  assert.equal(set.body.breakpoints.length, 2);
  assert.match(set.body.breakpoints[1].message, /not supported/);
  await client.configure(launch);
  await client.event('stopped');
  const changed = await client.event('breakpoint');
  assert.equal(changed.body.breakpoint.id, set.body.breakpoints[0].id);
  assert.equal(changed.body.breakpoint.line, 3);
  assert.equal(changed.body.breakpoint.verified, true);
  assert.equal(changed.body.breakpoint.source.path, canonicalPath(ws.program));
  const before = client.messages.filter(message => message.event === 'breakpoint').length;
  const cleared = await client.send('setBreakpoints', { source: { path: ws.program }, breakpoints: [] });
  assert.deepEqual(cleared.body.breakpoints, []);
  await client.send('threads'); // drains the stale event sent alongside the replacement response
  assert.equal(client.messages.filter(message => message.event === 'breakpoint').length, before);
  const loaded = await client.send('loadedSources');
  assert.equal(loaded.body.sources[0].path, canonicalPath(ws.program));
  const source = await client.send('source', { source: { path: ws.program }, sourceReference: 0 });
  assert.match(source.body.content, /日本語/);
});

test('zero-based clients and file URIs are converted in both directions', async t => {
  const { client, ws, launch } = await setup(t, '// normal\n', {}, { linesStartAt1: false, columnsStartAt1: false, pathFormat: 'uri' });
  const set = await client.send('setBreakpoints', { source: { path: pathToFileURL(ws.program).href }, breakpoints: [{ line: 1 }] });
  assert.equal(set.body.breakpoints[0].line, 1);
  await client.configure(launch);
  await client.event('stopped');
  const changed = await client.event('breakpoint');
  assert.equal(changed.body.breakpoint.line, 2);
  assert.equal(changed.body.breakpoint.column, 0);
  assert.equal(changed.body.breakpoint.source.path, pathToFileURL(canonicalPath(ws.program)).href);
  const stack = await client.send('stackTrace', { threadId: 1 });
  assert.equal(stack.body.stackFrames[0].line, 2);
  assert.equal(stack.body.stackFrames[0].column, 0);
});

test('per-frame scopes, paged variables, lazy objects and stale handles', async t => {
  const { client, launch } = await setup(t, '// normal\n');
  await client.configure(launch);
  await client.event('stopped');
  const trace = await client.send('stackTrace', { threadId: 1, startFrame: 1, levels: 1 });
  assert.equal(trace.body.totalFrames, 2);
  assert.equal(trace.body.stackFrames.length, 1);
  assert.equal(trace.body.stackFrames[0].name, 'outer');
  const oldFrame = trace.body.stackFrames[0].id;
  const scope = await client.send('scopes', { frameId: oldFrame });
  const oldReference = scope.body.scopes[0].variablesReference;
  const variables = await client.send('variables', { variablesReference: oldReference, start: 0, count: 1 });
  assert.equal(variables.body.variables[0].value, 'Number(99)');
  assert.equal(variables.body.variables.length, 1);
  const evaluated = await client.send('evaluate', { expression: 'object', frameId: oldFrame, context: 'watch' });
  const object = await client.send('variables', { variablesReference: evaluated.body.variablesReference });
  assert.equal(object.body.variables[0].variablesReference, evaluated.body.variablesReference);
  assert.equal(object.body.variables[1].value, '<accessor>');
  assert.equal((await client.send('next', { threadId: 1, granularity: 'instruction' })).success, true);
  await client.event('stopped');
  assert.equal((await client.send('scopes', { frameId: oldFrame })).success, false);
  assert.equal((await client.send('variables', { variablesReference: oldReference })).success, false);
});

test('pause handles a running host; unsupported or out-of-state requests fail promptly', async t => {
  const { client, launch } = await setup(t, '// infinite\n');
  await client.configure(launch);
  const invalid = await client.send('evaluate', { expression: 'value' });
  assert.equal(invalid.success, false);
  assert.match(invalid.message, /not paused/);
  assert.equal((await client.send('pause', { threadId: 2 })).success, false);
  assert.equal((await client.send('pause', { threadId: 1 })).success, true);
  assert.equal((await client.event('stopped')).body.reason, 'pause');
  assert.equal((await client.send('setVariable', {})).success, false);
  assert.equal((await client.send('stepBack', { threadId: 1 })).success, false);
});

test('all-exception filter, exceptionInfo and custom bytecode request', async t => {
  const { client, launch } = await setup(t, '// exception\n');
  assert.equal((await client.send('setExceptionBreakpoints', { filters: ['all'] })).success, true);
  assert.equal((await client.send('setExceptionBreakpoints', { filters: ['uncaught'] })).success, false);
  await client.configure(launch);
  assert.equal((await client.event('stopped')).body.reason, 'exception');
  assert.equal((await client.send('exceptionInfo', { threadId: 1 })).body.breakMode, 'always');
  assert.equal((await client.send('okojo/bytecode')).success, true);
  assert.match((await client.event('okojo/bytecode')).body.text, /Debugger/);
});

test('normal completion routes output and emits exited/terminated exactly once', async t => {
  const { client, launch } = await setup(t, '// exit-fast\n');
  await client.configure(launch);
  const output = await client.event('output', event => event.body.category === 'stdout');
  assert.match(output.body.output, /日本語/);
  await client.event('terminated');
  await client.send('disconnect');
  assert.equal(client.messages.filter(message => message.event === 'exited').length, 1);
  assert.equal(client.messages.filter(message => message.event === 'terminated').length, 1);
  const sequences = client.messages.map(message => message.seq);
  assert.deepEqual([...sequences].sort((a, b) => a - b), sequences);
  assert.equal(new Set(sequences).size, sequences.length);
});

test('host crash rejects a pending evaluate instead of leaving a DAP request hanging', async t => {
  const { client, launch } = await setup(t, '// crash-request\n');
  await client.configure(launch);
  await client.event('stopped');
  const response = await client.send('evaluate', { expression: 'value' });
  assert.equal(response.success, false);
  await client.event('terminated');
});

test('host RPC timeout produces a correlated error response', async t => {
  const { client, launch } = await setup(t, '// timeout-request\n', { requestTimeout: 100 });
  await client.configure(launch);
  await client.event('stopped');
  const response = await client.send('evaluate', { expression: 'value' });
  assert.equal(response.success, false);
  assert.match(response.message, /timed out/);
});

test('disconnect cancels a launch waiting for host readiness without duplicate responses', async t => {
  const ws = workspace();
  const client = new Client();
  t.after(async () => { await client.stop(); ws.dispose(); });
  await client.initialize();
  const launch = client.send('launch', { program: ws.program, cwd: ws.root, debugServerPath: process.execPath,
    debugServerArgs: [require('node:path').join(__dirname, 'fake-host.cjs')], env: { FAKE_STARTUP_DELAY: '1000' } });
  assert.equal((await client.send('disconnect')).success, true);
  assert.equal((await launch).success, false);
  assert.equal(client.messages.filter(message => message.command === 'launch' && message.type === 'response').length, 1);
});

test('missing program and attach return actionable errors', async t => {
  const client = new Client();
  t.after(() => client.stop());
  await client.initialize();
  assert.match((await client.send('launch', { program: '/definitely/not/okojo.js' })).message, /does not exist/);
  assert.match((await client.send('attach')).message, /not supported/);
});

test('startup crash returns a failed launch and terminates the session', async t => {
  const ws = workspace();
  const client = new Client();
  t.after(async () => { await client.stop(); ws.dispose(); });
  await client.initialize();
  const result = await client.send('launch', { program: ws.program, cwd: ws.root, debugServerPath: process.execPath,
    debugServerArgs: [require('node:path').join(__dirname, 'fake-host.cjs')], env: { FAKE_STARTUP_CRASH: '1' } });
  assert.equal(result.success, false);
  assert.match(result.message, /closed|exited/);
  await client.event('terminated');
});

test('startup timeout cancels a host that never reaches the configuration barrier', async t => {
  const ws = workspace();
  const client = new Client();
  t.after(async () => { await client.stop(); ws.dispose(); });
  await client.initialize();
  const result = await client.send('launch', { program: ws.program, cwd: ws.root, debugServerPath: process.execPath,
    debugServerArgs: [require('node:path').join(__dirname, 'fake-host.cjs')],
    env: { FAKE_STARTUP_DELAY: '1000' }, startupTimeout: 50 });
  assert.equal(result.success, false);
  assert.match(result.message, /startupTimeout|configuration stop/);
  await client.event('terminated');
});

test('standalone adapter reports truncated transport EOF on stderr and exits nonzero', { timeout: 3000 }, async t => {
  const client = new Client();
  t.after(() => client.stop());
  client.child.stdin.end('Content-Length: 25\r\n\r\n{');
  assert.equal(await client.closed, 1);
  assert.match(client.stderr, /Truncated DAP frame/);
});
