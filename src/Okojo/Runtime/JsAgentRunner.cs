namespace Okojo.Runtime;

internal sealed class JsAgentRunner(JsAgent agent)
{
    private readonly HostPump pump = new(agent);

    public JsAgent Agent => pump.Agent;

    public void PumpOnce()
    {
        pump.PumpOnce();
    }

    public void PumpUntilIdle(int maxPasses = 1024)
    {
        pump.PumpUntilIdle(maxPasses);
    }

    public void Run(CancellationToken cancellationToken)
    {
        pump.Run(cancellationToken);
    }
}
