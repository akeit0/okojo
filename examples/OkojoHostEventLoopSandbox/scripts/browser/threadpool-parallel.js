globalThis.events = [];
globalThis.bootState = "pending";

(async function boot() {
    events.push("boot:start");

    const fetchBranch = fetch("https://demo.local/api/data")
        .then(function (response) {
            return response.text();
        })
        .then(function (text) {
            events.push("fetch:" + text);
            return text;
        });

    const timerBranch = new Promise(function (resolve) {
        setTimeout(function () {
            events.push("timer");
            resolve("timer");
        }, 5);
    });

    const microtaskBranch = Promise.resolve().then(function () {
        events.push("microtask");
        return "microtask";
    });

    const result = await Promise.all([fetchBranch, timerBranch, microtaskBranch]);
    events.push("joined:" + result.length);
    bootState = "ready";
})().catch(function (err) {
    bootState = "error:" + err;
});
