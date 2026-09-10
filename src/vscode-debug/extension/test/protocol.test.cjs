const test = require('node:test');
const assert = require('node:assert/strict');
const { DapDecoder, encodeMessage } = require('../dist/dapProtocol');
const { BreakpointStore } = require('../dist/breakpointStore');

test('DAP framing preserves Unicode byte lengths across every split point', () => {
  const message = { seq: 1, type: 'request', command: 'evaluate', arguments: { expression: '日本語😀' } };
  const bytes = encodeMessage(message);
  for (let split = 1; split < bytes.length; split++) {
    const decoder = new DapDecoder();
    const result = [...decoder.push(bytes.subarray(0, split)), ...decoder.push(bytes.subarray(split))];
    assert.deepEqual(result, [message]);
    decoder.finish();
  }
});
test('DAP framing accepts byte-at-a-time and coalesced messages', () => {
  const messages = Array.from({ length: 12 }, (_, seq) => ({ seq, type: 'request', command: 'threads' }));
  const bytes = Buffer.concat(messages.map(encodeMessage));
  const decoder = new DapDecoder();
  const output = [];
  for (const byte of bytes) output.push(...decoder.push(Buffer.from([byte])));
  assert.deepEqual(output, messages);
  assert.deepEqual(new DapDecoder().push(bytes), messages);
});
for (const header of ['Content-Length: -1', 'Content-Length: nope', 'Content-Length: 0',
  'Content-Length: 999999999', 'Content-Length: 2\r\nContent-Length: 2', 'X-Other: 2']) {
  test(`reject malformed framing: ${JSON.stringify(header)}`, () => {
    assert.throws(() => new DapDecoder().push(Buffer.from(header + '\r\n\r\n{}')));
  });
}
test('reject oversized headers, invalid JSON/UTF-8 and truncated EOF', () => {
  assert.throws(() => new DapDecoder().push(Buffer.from('x'.repeat(8193))));
  assert.throws(() => new DapDecoder().push(Buffer.from('Content-Length: 2\r\n\r\nxx')));
  assert.throws(() => new DapDecoder().push(Buffer.concat([Buffer.from('Content-Length: 1\r\n\r\n'), Buffer.from([255])])));
  const decoder = new DapDecoder();
  decoder.push(Buffer.from('Content-Length: 10\r\n\r\n{'));
  assert.throws(() => decoder.finish(), /Truncated/);
});
test('breakpoint replacement keeps stable ids and ignores stale/deleted host events', () => {
  const store = new BreakpointStore();
  const [first, removed] = store.replace('/script.js', [1, 2]);
  const [same, added] = store.replace('/script.js', [1, 3]);
  assert.equal(first.id, same.id);
  assert.notEqual(removed.id, added.id);
  assert.equal(store.applyUpdate({ event: 'breakpoint-updated', clientId: removed.id, verified: true }), undefined);
  store.applyUpdate({ event: 'breakpoint-updated', clientId: first.id, verified: true, resolvedLine: 4 });
  assert.equal(first.resolvedLine, 4);
  store.replace('/script.js', []);
  assert.equal(store.applyUpdate({ event: 'breakpoint-updated', clientId: first.id, verified: true }), undefined);
});
