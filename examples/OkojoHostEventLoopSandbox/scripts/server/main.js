import {hasFetch, hasHost, hasWindow, hasWorker, runtimeName} from "./config.js";
import {appendLog, buildKinds, readJson} from "./helpers.js";

appendLog(`host:${runtimeName}`);

const fetchTask = readJson("https://demo.local/api/data");
const tlaTask = Promise.resolve("tla").then(value => {
    appendLog(value);
    return value;
});
const microtaskTask = Promise.resolve().then(() => {
    appendLog("microtask");
    return "microtask";
});

const [payload, tla, microtask] = await Promise.all([fetchTask, tlaTask, microtaskTask]);

export const serverState = "ready";
export const fetchedKind = payload.kind;
export const summary = `${runtimeName}:${payload.kind}:${tla}:${microtask}`;
export const tlaStage = tla;
export const microtaskStage = microtask;
export const kinds = buildKinds({hasWindow, hasWorker, hasFetch, hasHost});
