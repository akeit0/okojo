function lexicalBlock() {
    var s = 0;
    for (let i = 0; i < 200000; i++) {
        {
            s = s + i;
        }
    }
    return s;
}

lexicalBlock;
