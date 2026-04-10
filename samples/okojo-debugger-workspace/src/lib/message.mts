console.log('message: module loaded');

export function formatMessage(label: string, value: number): string {
  const prefix: string = 'result';
  return `${prefix} ${label}: ${value}`;
}
