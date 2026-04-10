using Okojo.Runtime;

namespace Okojo.Tests;

public class WorkerAgentThreadTests
{
    [Test]
    public void WorkerRunner_ProcessesPostedMessage_OnWorkerThread()
    {
        var engine = JsRuntime.Create();
        var main = engine.MainAgent;
        var worker = engine.CreateWorkerAgent();
        var runner = new JsAgentRunner(worker);

        using var cts = new CancellationTokenSource();
        using var received = new ManualResetEventSlim(false);

        var workerThreadId = -1;
        var handlerThreadId = -1;

        var thread = new Thread(() =>
        {
            workerThreadId = Thread.CurrentThread.ManagedThreadId;
            runner.Run(cts.Token);
        });
        thread.Start();

        worker.MessageReceived += (_, payload) =>
        {
            handlerThreadId = Thread.CurrentThread.ManagedThreadId;
            if ((string?)payload == "ping")
                received.Set();
        };

        main.PostMessage(worker, "ping");

        Assert.That(received.Wait(TimeSpan.FromSeconds(2)), Is.True, "worker message was not processed in time");
        Assert.That(handlerThreadId, Is.EqualTo(workerThreadId));

        cts.Cancel();
        Assert.That(thread.Join(TimeSpan.FromSeconds(2)), Is.True, "worker thread did not stop in time");
    }

    [Test]
    public void WorkerRunner_StopsOnCancellation_WhenIdle()
    {
        var engine = JsRuntime.Create();
        var worker = engine.CreateWorkerAgent();
        var runner = new JsAgentRunner(worker);

        using var cts = new CancellationTokenSource();
        var thread = new Thread(() => runner.Run(cts.Token));
        thread.Start();

        cts.Cancel();

        Assert.That(thread.Join(TimeSpan.FromSeconds(2)), Is.True, "worker thread did not stop in time");
    }

    [Test]
    public void WorkerRunner_PostMessage_OrderIsFifo()
    {
        var engine = JsRuntime.Create();
        var main = engine.MainAgent;
        var worker = engine.CreateWorkerAgent();
        var runner = new JsAgentRunner(worker);
        using var cts = new CancellationTokenSource();

        var done = new ManualResetEventSlim(false);
        var seen = 0;
        string? failure = null;
        worker.MessageReceived += (_, payload) =>
        {
            if (payload is not int n)
                return;
            if (n != seen)
            {
                failure = $"Expected message {seen} but got {n}";
                done.Set();
                return;
            }

            seen++;
            if (seen == 50)
                done.Set();
        };

        var thread = new Thread(() => runner.Run(cts.Token));
        thread.Start();
        for (var i = 0; i < 50; i++)
            main.PostMessage(worker, i);

        Assert.That(done.Wait(TimeSpan.FromSeconds(2)), Is.True, "worker did not receive all messages in time");
        Assert.That(failure, Is.Null);
        Assert.That(seen, Is.EqualTo(50));

        cts.Cancel();
        Assert.That(thread.Join(TimeSpan.FromSeconds(2)), Is.True, "worker thread did not stop in time");
    }

    [Test]
    public void WorkerRunner_StopsWhenAgentTerminated()
    {
        var engine = JsRuntime.Create();
        var worker = engine.CreateWorkerAgent();
        var runner = new JsAgentRunner(worker);
        using var cts = new CancellationTokenSource();

        var thread = new Thread(() => runner.Run(cts.Token));
        thread.Start();

        worker.Terminate();

        Assert.That(thread.Join(TimeSpan.FromSeconds(2)), Is.True, "worker thread did not stop after terminate");
    }
}
