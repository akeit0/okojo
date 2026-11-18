function benchmarkTime(run) {
    var batches = 1;
    var elapsed;
    do {
        elapsed = runFor(batches);
        if (elapsed >= 20) break;
        batches *= 2;
    } while (true);

    var warmupStart = Date.now();
    do {
        elapsed = runFor(batches);
    } while (Date.now() - warmupStart < benchmarkWarmupMs);

    batches = scaleTo(benchmarkSampleMs);
    elapsed = runFor(batches);
    while (elapsed < benchmarkSampleMs / 2) {
        batches = Math.max(batches * 2, scaleTo(benchmarkSampleMs));
        elapsed = runFor(batches);
    }
    return { elapsedMs: elapsed, batches: batches };

    function scaleTo(ms) {
        return Math.max(1, Math.ceil(batches * ms / Math.max(1, elapsed)));
    }

    function runFor(count) {
        var start = Date.now();
        for (var i = 0; i < count; i++) run();
        var elapsed = Date.now() - start;
        if (elapsed < 0) throw new Error("clock went backwards");
        return elapsed;
    }
}
