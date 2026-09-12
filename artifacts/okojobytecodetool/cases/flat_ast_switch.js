function choose(value) {
    let result = 0;
    switch (value) {
        case 1:
            return 10;
        default:
            result = 20;
        case 2:
            result += 2;
            break;
        case 3:
            result = 30;
    }
    return result;
}

choose(2) + choose(9);
