function arith() {
    var s = 0;
    for (var i = 0; i < 20; i++) {
        var temp = 0;
        temp += 1;
        temp = temp ** 3;
        temp *= 7;
        temp -= 11;
        temp /= 2;
        s = s + temp;
    }
    return s;
}

arith;
