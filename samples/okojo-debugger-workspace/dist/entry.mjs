import { multiply } from './lib/math.mjs';
import { formatMessage } from './lib/message.mjs';
console.log('entry: module loaded');
function run() {
    console.log('entry: run() start');
    const product = multiply(6, 7);
    const message = formatMessage('answer', product);
    console.log(`entry: ${message}`);
    debugger;
    return message;
}
run();
//# sourceMappingURL=entry.mjs.map