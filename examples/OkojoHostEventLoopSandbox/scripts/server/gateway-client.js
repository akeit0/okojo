import {appendLog, readJson} from "./helpers.js";

export const loadUser = async (userId) => {
    appendLog(`gateway:user:${userId}`);
    return readJson(`https://services.local/users/${userId}`);
};

export const loadAudit = async (userId) => {
    appendLog(`gateway:audit:${userId}`);
    return readJson(`https://services.local/audit/${userId}`);
};
