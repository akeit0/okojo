export const appendLog = (message) => {
    globalThis.serverLog ??= [];
    serverLog.push(message);
};

export const readJson = async (url) => {
    const response = await fetch(url);
    return response.json();
};

export const buildKinds = (config) => {
    return [config.hasWindow, config.hasWorker, config.hasFetch, config.hasHost].join("|");
};
