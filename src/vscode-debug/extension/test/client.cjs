const { spawn } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { DapDecoder, encodeMessage } = require('../dist/dapProtocol');

class Client {
  constructor() {
    this.seq = 1;
    this.messages = [];
    this.events = [];
    this.waiters = [];
    this.pending = new Map();
    this.stderr = '';
    this.child = spawn(process.execPath, [path.resolve(__dirname, '../dist/main.js')], { stdio: 'pipe' });
    this.closed = new Promise(resolve => this.child.once('close', resolve));
    const decoder = new DapDecoder();
    this.child.stdout.on('data', chunk => {
      try { for (const message of decoder.push(chunk)) this.receive(message); }
      catch (error) { for (const request of this.pending.values()) request.reject(error); }
    });
    this.child.stderr.on('data', chunk => { this.stderr += chunk; });
    this.child.stdin.on('error', () => {});
  }
  receive(message) {
    this.messages.push(message);
    if (message.type === 'response') {
      const pending = this.pending.get(message.request_seq);
      if (pending) {
        this.pending.delete(message.request_seq);
        clearTimeout(pending.timer);
        pending.resolve(message);
      }
    } else if (message.type === 'event') {
      const index = this.waiters.findIndex(waiter => waiter.name === message.event && waiter.predicate(message));
      if (index >= 0) {
        const waiter = this.waiters.splice(index, 1)[0];
        clearTimeout(waiter.timer);
        waiter.resolve(message);
      } else this.events.push(message);
    }
  }
  send(command, args = {}, fragmented = false) {
    const seq = this.seq++;
    const result = new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(seq);
        reject(new Error(`Timed out waiting for ${command}: ${this.stderr}`));
      }, 5000);
      this.pending.set(seq, { resolve, reject, timer });
    });
    const bytes = encodeMessage({ seq, type: 'request', command, arguments: args });
    if (fragmented) for (let i = 0; i < bytes.length; i += 3) this.child.stdin.write(bytes.subarray(i, i + 3));
    else this.child.stdin.write(bytes);
    return result;
  }
  event(name, predicate = () => true) {
    const index = this.events.findIndex(event => event.event === name && predicate(event));
    if (index >= 0) return Promise.resolve(this.events.splice(index, 1)[0]);
    return new Promise((resolve, reject) => {
      const waiter = { name, predicate, resolve, timer: undefined };
      waiter.timer = setTimeout(() => {
        this.waiters = this.waiters.filter(item => item !== waiter);
        reject(new Error(`Timed out waiting for ${name}: ${JSON.stringify(this.messages)} ${this.stderr}`));
      }, 5000);
      this.waiters.push(waiter);
    });
  }
  async initialize(args = {}) {
    const response = await this.send('initialize', { adapterID: 'okojo', linesStartAt1: true, columnsStartAt1: true, ...args }, true);
    if (!response.success) throw new Error(response.message);
    return response;
  }
  async launch(program, args = {}) {
    const launch = this.send('launch', {
      program, cwd: path.dirname(program), debugServerPath: process.execPath,
      debugServerArgs: [path.resolve(__dirname, 'fake-host.cjs')], ...args,
    });
    await this.event('initialized');
    return { response: launch };
  }
  async configure(launch) {
    const response = await this.send('configurationDone');
    if (!response.success) throw new Error(response.message);
    const launched = await launch.response;
    if (!launched.success) throw new Error(launched.message);
    return launched;
  }
  async stop() {
    if (this.child.exitCode === null && this.child.signalCode === null) {
      await this.send('disconnect').catch(() => {});
      this.child.stdin.end();
      const timer = setTimeout(() => this.child.kill('SIGKILL'), 2000);
      await this.closed;
      clearTimeout(timer);
    }
    for (const request of this.pending.values()) clearTimeout(request.timer);
    for (const waiter of this.waiters) clearTimeout(waiter.timer);
  }
}
function workspace(text = '// normal\nconst value = 7;\ndebugger;\n') {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'okojo dap 日本語-'));
  const program = path.join(root, 'script with spaces.js');
  fs.writeFileSync(program, text);
  return { root, program, dispose: () => fs.rmSync(root, { recursive: true, force: true }) };
}
module.exports = { Client, workspace };
