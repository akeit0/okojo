const styles = {};

styles.green = {
  get() {
    const builder = () => "ok";
    Object.setPrototypeOf(builder, proto);
    Object.defineProperty(this, "green", { value: builder });
    return builder;
  }
};

const proto = Object.defineProperties(() => {}, { ...styles });
const desc = Object.getOwnPropertyDescriptor(proto, "green");

function create() {
  const chalk = () => "base";
  Object.setPrototypeOf(chalk, proto);
  return chalk;
}

const left = create();
const right = create();

export default [
  typeof desc.get,
  typeof desc.value,
  typeof left.green,
  typeof right.green
].join("|");
