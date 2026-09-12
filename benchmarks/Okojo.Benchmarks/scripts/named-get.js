function namedGet() {
    const o = { x: 1, y: 2 };
    let s = 0;
    for (let i = 0; i < 100000; i++) {
        s += o.x;
        s += o.y;
    }
    return s;
}

namedGet;
