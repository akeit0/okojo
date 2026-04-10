function* range(start, end) {

    for (let i = start; i < end; i++) {
        yield i;
    }
}

function sumFunction() {
    var s = 0;
    for (let i of range(0, 10000)) {
        s += i;
    }

    return s;
}

sumFunction
