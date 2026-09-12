namespace Okojo.JavaScript.Execution.Interop;

internal readonly record struct HostByRefBinding(
    int ArgumentIndex,
    IClrByRefPlaceholder Placeholder
);
