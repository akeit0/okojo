let tick = 0;

export function init() {
    app.log("runaway-sync boot");
}

export function update(frame) {
    tick++;
    if (frame === 2) {
        let spin = 0;
        while (true) {
            spin++;
        }
    }

    return tick;
}

export function snapshot() {
    return JSON.stringify({tick});
}
