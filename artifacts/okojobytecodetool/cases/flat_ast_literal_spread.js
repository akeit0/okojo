let key = 'tail';
let array = [0, ...[1, 2], , 4];
let object = { before: 1, ...{ copied: 2 }, [key]: 3 };
array.length + array[2] + object.before + object.copied + object.tail;
