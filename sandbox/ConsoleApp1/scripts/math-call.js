function mathCall() {
    var s = 0;
    for (var i = 1; i <= 200; i++) {
        s = s
            + Math.sin(i)
            + Math.cos(i)
            + Math.sqrt(i)
            + Math.log(i)
            + Math.pow(i, 0.5)
            + Math.imul(i, 3)
            + Math.trunc(i / 3)
            + Math.log2(i)
            + Math.log10(i);
    }

    return s + Math.PI + Math.E;
}

mathCall;
