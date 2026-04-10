export let runCount = 0;

export function bumpRuns() {
  runCount += 1;
}

export default function getRuns() {
  return runCount;
}
