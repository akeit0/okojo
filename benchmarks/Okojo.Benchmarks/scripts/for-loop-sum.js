function loop() {
    let s = 0;
    for (let i = 0; i < 10000; i++) {
        {
            s = s + i;
        }
    }
    return s;
}

loop;
