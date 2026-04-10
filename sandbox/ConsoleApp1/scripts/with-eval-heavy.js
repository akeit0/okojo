function withEvalHeavy() {
    var s = 0;
    var scope = {x: 3, y: 5};

    for (var i = 0; i < 50; i++) {
        with (scope) {
            s = s + x + y;
        }
    }

    // Keep eval in the workload to measure dynamic-name behavior.
    return eval("s + 1");
}

withEvalHeavy;
