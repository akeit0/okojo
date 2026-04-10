import React, {useEffect, useState} from "react";
import {Box, render, Text} from "ink";

const totalSteps = 5;
const SampleApp = () => {
    const [step, setStep] = useState(0);
    useEffect(() => {
        const timer = setInterval(() => {
            setStep((previousStep) => {
                if (previousStep + 1 >= totalSteps) {
                    clearInterval(timer);
                    return totalSteps;
                }
                return previousStep + 1;
            });
        }, 120);
        return () => {
            clearInterval(timer);
        };
    }, []);
    const completed = Math.min(step, totalSteps);
    const pending = totalSteps - completed;
    const progressBar = `${"\u2588".repeat(completed)}${"\u2591".repeat(pending)}`;
    const status = completed >= totalSteps ? "Ready" : "Running";
    const checks = [
        ["Layout", completed >= 1],
        ["Colors", completed >= 2],
        ["Timers", completed >= 3],
        ["Columns", completed >= 4],
        ["Exit", completed >= 5]
    ];
    return /* @__PURE__ */ React.createElement(Box, {
        flexDirection: "column",
        borderStyle: "round",
        borderColor: "green",
        paddingX: 1,
        width: 72
    }, /* @__PURE__ */ React.createElement(Text, {color: "cyan"}, "Ink Layout Probe"), /* @__PURE__ */ React.createElement(Box, {
        marginTop: 1,
        flexDirection: "row",
        justifyContent: "space-between"
    }, /* @__PURE__ */ React.createElement(Box, {
        flexDirection: "column",
        width: 32,
        borderStyle: "single",
        borderColor: "blue",
        paddingX: 1
    }, /* @__PURE__ */ React.createElement(Text, {color: "yellow"}, "Status Panel"), /* @__PURE__ */ React.createElement(Text, null, "State: ", /* @__PURE__ */ React.createElement(Text, {color: completed >= totalSteps ? "green" : "yellow"}, status)), /* @__PURE__ */ React.createElement(Text, null, "Progress: ", /* @__PURE__ */ React.createElement(Text, {color: "green"}, progressBar)), /* @__PURE__ */ React.createElement(Text, null, "Step: ", completed, "/", totalSteps)), /* @__PURE__ */ React.createElement(Box, {
        flexDirection: "column",
        width: 32,
        borderStyle: "single",
        borderColor: "magenta",
        paddingX: 1
    }, /* @__PURE__ */ React.createElement(Text, {color: "green"}, "Checks"), checks.map(([label, ready]) => /* @__PURE__ */ React.createElement(Text, {
        key: label,
        color: ready ? "green" : "gray"
    }, ready ? "\u2713" : "\xB7", " ", label)))), /* @__PURE__ */ React.createElement(Text, {color: "magenta"}, "Two columns rendered through React + Ink"));
};
render(/* @__PURE__ */ React.createElement(SampleApp, null));
var main_default = "ink-entry-loaded";
export {
    main_default as default
};
