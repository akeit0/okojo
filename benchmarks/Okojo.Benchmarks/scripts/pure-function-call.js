function functionCall() {
    let identity = function (x) {
        return x;
    }

    let s = 0;
    for (let i = 0; i < 5000; i = i + 1) {
        s = identity(i) + 1;
    }

    return s;
}

functionCall;
