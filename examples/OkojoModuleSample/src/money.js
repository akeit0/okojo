export function round2(value) {
  return value;
}

export default function formatMoney(value) {
  return "$" + round2(value);
}
