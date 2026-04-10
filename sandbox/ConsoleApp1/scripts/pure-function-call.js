function functionCall() {
    let identity = function (x) {
        return x;
    }

    var s = 0;
    for (var i = 0; i < 1000; i++) {
        s = identity(i);
    }

    return s;
}

functionCall;
