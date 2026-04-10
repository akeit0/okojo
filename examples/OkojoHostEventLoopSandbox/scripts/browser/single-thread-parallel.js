globalThis.singleThreadEvents = [];
globalThis.singleThreadState = "pending";

(async function run() {
    singleThreadEvents.push("start");

    const fetchBranch = fetch("https://demo.local/api/data")
        .then(function (response) {
            return response.text();
        })
        .then(function (text) {
            singleThreadEvents.push("fetch:" + text);
            return text;
        });

    const timerA = new Promise(function (resolve) {
        setTimeout(function () {
            singleThreadEvents.push("timer:a");
            resolve("a");
        }, 5);
    });

    const timerB = new Promise(function (resolve) {
        setTimeout(function () {
            singleThreadEvents.push("timer:b");
            resolve("b");
        }, 10);
    });

    const values = await Promise.all([fetchBranch, timerA, timerB]);
    singleThreadEvents.push("joined:" + values.length);
    singleThreadState = "ready";
})().catch(function (err) {
    singleThreadState = "error:" + err;
});
