using Okojo.Runtime;

namespace Okojo.Hosting;

public static class JsAgentExecutionBudgetExtensions
{
    public static JsAgentOptions ApplyExecutionBudget(
        this JsAgentOptions options,
        ulong maxInstructions,
        TimeSpan? executionTimeout = null,
        ulong? checkInterval = null,
        CancellationToken executionCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (checkInterval is { } interval)
            options.SetCheckInterval(interval);

        options.SetMaxInstructions(maxInstructions);
        if (executionTimeout is { } timeout)
            options.SetExecutionTimeout(timeout);
        else
            options.ClearExecutionTimeout();

        if (executionCancellationToken.CanBeCanceled)
            options.SetExecutionCancellationToken(executionCancellationToken);
        else
            options.ClearExecutionCancellationToken();

        return options;
    }

    public static JsAgent ApplyExecutionBudget(
        this JsAgent agent,
        ulong maxInstructions,
        TimeSpan? executionTimeout = null,
        ulong? checkInterval = null,
        CancellationToken executionCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (checkInterval is { } interval)
            agent.SetCheckInterval(interval);

        agent.SetMaxInstructions(maxInstructions);
        if (executionTimeout is { } timeout)
            agent.SetExecutionTimeout(timeout);
        else
            agent.ClearExecutionTimeout();

        if (executionCancellationToken.CanBeCanceled)
            agent.SetExecutionCancellationToken(executionCancellationToken);
        else
            agent.ClearExecutionCancellationToken();

        return agent;
    }

    public static JsAgent ResetExecutionBudget(
        this JsAgent agent,
        ulong maxInstructions,
        TimeSpan? executionTimeout = null,
        CancellationToken executionCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);

        agent.SetMaxInstructions(maxInstructions);
        agent.ResetExecutedInstructions();
        if (executionTimeout is { } timeout)
            agent.ResetExecutionTimeout(timeout);
        else
            agent.ClearExecutionTimeout();

        if (executionCancellationToken.CanBeCanceled)
            agent.SetExecutionCancellationToken(executionCancellationToken);
        else
            agent.ClearExecutionCancellationToken();

        return agent;
    }
}
