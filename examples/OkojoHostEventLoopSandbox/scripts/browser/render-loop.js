globalThis.renderState = "booting";
globalThis.renderTimestamp = -1;
globalThis.renderEvents = [];

renderEvents.push("sync");
queueMicrotask(() => {
    renderEvents.push("microtask");
});

requestAnimationFrame(timestamp => {
    renderTimestamp = timestamp;
    renderEvents.push("frame");
    queueMicrotask(() => {
        renderEvents.push("frame-microtask");
        renderState = "ready";
    });
});
