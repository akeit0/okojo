const styles = {
  green: {
    get() {
      debugger;
      const value = () => 0;
      Object.defineProperty(this, 'green', { value });
      return value;
    }
  }
};

const proto = Object.defineProperties({}, { ...styles });
const left = Object.create(proto);
const first = typeof left.green;
debugger;
export default `${first}|${typeof left.green}`;