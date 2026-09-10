// Real-engine tests, deliberately separate from the simulated-host contract suite.
// Build Release Okojo.DebugServer first and set OKOJO_DEBUG_SERVER to its DLL/executable.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { Client, workspace } = require('./client.cjs');

const server = process.env.OKOJO_DEBUG_SERVER;
if (!server || !fs.existsSync(server)) {
  throw new Error('Set OKOJO_DEBUG_SERVER to a built Okojo.DebugServer.dll or native executable. This suite does not substitute a fake host.');
}

async function start(t, text, options = {}) {
  const ws = workspace(text);
  const client = new Client();
  t.after(async () => { await client.stop(); ws.dispose(); });
  await client.initialize();
  const launch = await client.launch(ws.program, {
    debugServerPath: path.resolve(server), debugServerArgs: [],
    dotnetPath: process.env.DOTNET_HOST_PATH || 'dotnet',
    checkInterval: 1024, stopOnEntry: true, ...options,
  });
  return { ws, client, launch };
}

async function continueToStop(client, reason) {
  const response = await client.send('continue', { threadId: 1 });
  assert.equal(response.success, true, JSON.stringify(response));
  const stopped = await client.event('stopped');
  assert.equal(stopped.body.reason, reason);
  return stopped;
}

function ok(response) {
  assert.equal(response.success, true, JSON.stringify(response));
  return response.body;
}

test('real engine: configuration, nested call frames, safe object expansion, stepping and completion', async t => {
  const { client, launch } = await start(t, [
    'let getterCalls = 0;',
    'function inner(value) {',
    '  const object = { value, items: [10, 20, 30], get danger() { getterCalls++; throw new Error("do not invoke"); } };',
    '  object.self = object;',
    '  debugger;',
    '  return object.value;',
    '}',
    'function caller(value) {',
    '  const callerOnly = 99;',
    '  return inner(value + 1) + callerOnly;',
    '}',
    'console.log(caller(7));',
    '',
  ].join('\n'));
  await client.configure(launch);
  assert.equal((await client.event('stopped')).body.reason, 'entry');
  await continueToStop(client, 'breakpoint');
  const trace = ok(await client.send('stackTrace', { threadId: 1 }));
  const inner = trace.stackFrames.find(frame => frame.name === 'inner');
  const caller = trace.stackFrames.find(frame => frame.name === 'caller');
  assert.ok(inner && caller, JSON.stringify(trace));
  assert.equal(ok(await client.send('evaluate', { frameId: inner.id, expression: 'value' })).result, 'Number(8)');
  assert.equal(ok(await client.send('evaluate', { frameId: caller.id, expression: 'value' })).result, 'Number(7)');
  assert.equal((await client.send('evaluate', { frameId: inner.id, expression: 'callerOnly' })).success, false);
  const scope = ok(await client.send('scopes', { frameId: inner.id }));
  assert.deepEqual(scope.scopes.map(item => item.name), ['Locals', 'Global']);
  const object = ok(await client.send('evaluate', { frameId: inner.id, expression: 'object' }));
  const children = ok(await client.send('variables', { variablesReference: object.variablesReference })).variables;
  assert.equal(children.find(item => item.name === 'self').variablesReference, object.variablesReference);
  assert.equal(children.find(item => item.name === 'danger').value, '<accessor>');
  assert.equal((await client.send('evaluate', { expression: 'object.danger' })).success, false);
  assert.equal((await client.send('evaluate', { expression: 'object.value = 100' })).success, false);
  const items = children.find(item => item.name === 'items');
  const page = ok(await client.send('variables', { variablesReference: items.variablesReference, filter: 'indexed', start: 1, count: 1 }));
  assert.equal(page.variables.length, 1);
  assert.equal(page.variables[0].value, 'Number(20)');
  ok(await client.send('next', { threadId: 1 }));
  assert.equal((await client.event('stopped')).body.reason, 'step');
  assert.equal((await client.send('scopes', { frameId: inner.id })).success, false);
  assert.equal((await client.send('variables', { variablesReference: object.variablesReference })).success, false);
  ok(await client.send('continue', { threadId: 1 }));
  const output = await client.event('output', message => message.body.category === 'stdout');
  assert.match(output.body.output, /107/);
  assert.equal((await client.event('exited')).body.exitCode, 0);
  await client.event('terminated');
});

test('real engine: replaced breakpoints bind at script compilation with Unicode paths', async t => {
  const { client, launch, ws } = await start(t, 'let value = 0;\nvalue++;\nvalue++;\nconsole.log(value);\n');
  ok(await client.send('setBreakpoints', { source: { path: ws.program }, breakpoints: [{ line: 2 }] }));
  const replacement = ok(await client.send('setBreakpoints', { source: { path: ws.program }, breakpoints: [{ line: 3 }] }));
  await client.configure(launch);
  await client.event('stopped');
  await continueToStop(client, 'breakpoint');
  const changed = await client.event('breakpoint', message => message.body.breakpoint.id === replacement.breakpoints[0].id);
  assert.equal(changed.body.breakpoint.verified, true);
  const trace = ok(await client.send('stackTrace', { threadId: 1 }));
  assert.equal(trace.stackFrames[0].line, 3);
  assert.equal(trace.stackFrames[0].source.path, process.platform === 'win32' ? ws.program.toLowerCase() : ws.program);
  ok(await client.send('setBreakpoints', { source: { path: ws.program }, breakpoints: [] }));
  ok(await client.send('continue', { threadId: 1 }));
  await client.event('terminated');
});

test('real engine: pause a running loop and terminate from a periodic checkpoint', async t => {
  const { client, launch } = await start(t, 'try { while (true) {} } catch (error) { while (true) {} }\n', { stopOnEntry: false });
  await client.configure(launch);
  ok(await client.send('pause', { threadId: 1 }));
  assert.equal((await client.event('stopped')).body.reason, 'pause');
  ok(await client.send('next', { threadId: 1, granularity: 'instruction' }));
  assert.equal((await client.event('stopped')).body.reason, 'step');
  ok(await client.send('terminate'));
  await client.event('terminated');
});

test('real engine: all-exception filter includes handled JavaScript throws', async t => {
  const { client, launch } = await start(t, 'try { throw new Error("expected"); } catch (error) { console.log("handled"); }\n');
  ok(await client.send('setExceptionBreakpoints', { filters: ['all'] }));
  await client.configure(launch);
  await client.event('stopped');
  await continueToStop(client, 'exception');
  assert.equal(ok(await client.send('exceptionInfo', { threadId: 1 })).breakMode, 'always');
  ok(await client.send('setExceptionBreakpoints', { filters: [] }));
  ok(await client.send('continue', { threadId: 1 }));
  assert.match((await client.event('output', message => message.body.category === 'stdout')).body.output, /handled/);
  await client.event('terminated');
});
