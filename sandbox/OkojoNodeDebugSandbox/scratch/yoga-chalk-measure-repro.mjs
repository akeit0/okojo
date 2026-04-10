import chalk from 'chalk';
import loadYogaImpl from '../../OkojoInkProbe/app/node_modules/.pnpm/ink@6.8.0_react@19.2.4/node_modules/yoga-layout/dist/binaries/yoga-wasm-base64-esm.js';
import wrapAssembly from '../../OkojoInkProbe/app/node_modules/.pnpm/ink@6.8.0_react@19.2.4/node_modules/yoga-layout/dist/src/wrapAssembly.js';

const Yoga = wrapAssembly(await loadYogaImpl());
const logs = [];

function colorizeBackground(value) {
  const methodName = 'bgWhiteBright';
  return chalk[methodName](value);
}

const root = Yoga.Node.create();
const child = Yoga.Node.create();

child.setMeasureFunc(function () {
  logs.push(`measure:${arguments.length}`);
  logs.push(colorizeBackground('inside-measure'));
  return { width: 5, height: 1 };
});

logs.push(colorizeBackground('before-layout'));
root.insertChild(child, 0);
root.setWidth(20);

for (let i = 0; i < 2; i++) {
  try {
    root.calculateLayout(undefined, undefined, Yoga.DIRECTION_LTR);
    logs.push(`layout:${i}:ok`);
  } catch (error) {
    logs.push(`layout:${i}:error:${error?.name}:${error?.message}`);
  }
}

export default logs.join('\n');
