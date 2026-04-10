const q0 = 'https://example.com/a/b.js?x#y';
const q1 = q0.replace(/[?#].*/, '');
const q2 = q1.lastIndexOf('/');
const q3 = q1.substr(0, q2 + 1);
console.log('q1=', q1);
console.log('q2=', q2);
console.log('q3=', q3);
