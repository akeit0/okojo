let expression = /a[b\/]+c/gi;
let amount = 9007199254740993n;
expression.test('xxaB/cyy') + '|' + (amount + 7n).toString();
