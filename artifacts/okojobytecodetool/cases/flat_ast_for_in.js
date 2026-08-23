function keys(object) {
    let result = '';
    let closures = [];
    for (const key in object) {
        closures.push(() => key);
        if (key === 'skip') continue;
        result += key;
        if (key === 'stop') break;
    }
    return result + ':' + closures.map(read => read()).join(',');
}

keys({ first: 1, skip: 2, stop: 3, after: 4 });

let forInTarget = {};
for (forInTarget.key in { member: 1 }) {}
