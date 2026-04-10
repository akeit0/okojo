import {runtimeName} from "./config.js";
import {appendLog} from "./helpers.js";
import {loadAudit, loadUser} from "./gateway-client.js";

appendLog(`gateway:${runtimeName}:start`);

const [user, audit] = await Promise.all([
    loadUser(42),
    loadAudit(42)
]);

const actionSummary = audit.actions.join("|");
appendLog(`gateway:${user.name}:${actionSummary}`);

export const gatewayState = "ready";
export const userName = user.name;
export const actionCount = audit.actions.length;
export const gatewaySummary = `${user.name}:${actionSummary}`;
