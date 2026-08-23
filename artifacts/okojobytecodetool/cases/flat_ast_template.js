let order = '';
function value(text) {
    order += text;
    return { toString() { order += 't'; return text; } };
}
let result = `a${value('x')}b${value('y')}c`;
let nested = `n${{ value: `i${2}` }.value}`;
result + '|' + nested + '|' + order;
