function indexing() {
    var arr = [];
    var obj = {};
    for (var i = 0; i < 1024; i++) {
        arr[i] = i + 1;
        obj[i] = (i + 1) * 3;
    }

    var sum = 0;
    for (var j = 0; j < 200; j++) {
        var k = j % 1023;
        sum = sum + arr[k] + obj[k];
    }

    return sum;
}

indexing;

