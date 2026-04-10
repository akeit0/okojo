import {rewardDelayMs, spawnName} from "./level-data.js";

const state = {
    x: 0,
    energy: 12,
    log: [],
    quest: "booting"
};

let introReady = false;
let rewardReady = false;

function waitMs(ms) {
    return app.delayMs(ms);
}

function waitFrames(frameCount) {
    return app.waitFrames(frameCount);
}

export function init() {
    app.log("cooperative boot");
    state.log.push(`init:${app.frame}`);

    queueMicrotask(() => {
        introReady = true;
        state.log.push("micro:boot");
    });

    setTimeout(() => {
        rewardReady = true;
        state.log.push(`timer:${spawnName}`);
        app.emit(`spawn:${spawnName}`);
    }, rewardDelayMs);

    (async () => {
        state.log.push("async:start");
        await Promise.resolve();
        state.log.push(`async:after-promise:${app.frame}`);
        await waitFrames(2);
        state.log.push(`after-frames:${app.frame}`);
        await waitMs(rewardDelayMs + 10);
        state.log.push(`after-ms:${app.frame}`);
        state.quest = "ready";
        app.emit("quest:ready");
    })();
}

export function update(frame, dtMs) {
    state.x += 3;
    state.energy = Math.max(0, state.energy - 1);
    state.log.push(`frame:${frame}:${dtMs}`);

    if (introReady) {
        state.log.push(`intro:${frame}`);
    }

    if (rewardReady) {
        state.log.push(`reward:${frame}`);
    }

    return state.x;
}

export function debugTrace() {
    return state.log.join(",");
}

export function snapshot() {
    return JSON.stringify({
        x: state.x,
        energy: state.energy,
        quest: state.quest,
        last: state.log.slice(-16)
    });
}
