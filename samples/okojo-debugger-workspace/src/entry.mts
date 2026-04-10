import { multiply } from './lib/math.mjs';
import { formatMessage } from './lib/message.mjs';

console.log('entry: module loaded');

function run(): string {
  console.log('entry: run() start');
  const product: number = multiply(6, 7);
  const message: string = formatMessage('answer', product);
  console.log(`entry: ${message}`);
  debugger;
  return message;
}

run();
