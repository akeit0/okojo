const state = {
    armed: false,
    fired: false
};

export function init() {
    app.log("runaway-async boot");
    setTimeout(() => {
        state.fired = true;
        app.log("timer-fired");

        function flood() {
            queueMicrotask(flood);
        }

        queueMicrotask(flood);
    }, 34);
}

export function update() {
    if (!state.armed) {
        state.armed = true;
        app.emit("armed");
    }
}

export function snapshot() {
    return JSON.stringify(state);
}
