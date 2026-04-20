using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

//Console.WriteLine($"StackFrame {Unsafe.SizeOf<StackFrame>()}");
//Console.WriteLine($"SavedCallFrame {Unsafe.SizeOf<SavedCallFrame>()}");
//return;

using var rt = JsRuntime.CreateBuilder()
    .UseAgent(agent =>
    {
        agent.SetExecutionTimeout(TimeSpan.FromSeconds(2));
        agent.SetMaxInstructions(100_000);
        agent.SetCheckInterval(1000);
    })
    .Build();
var realm = rt.MainRealm;
var agent = realm.Agent;
agent.SetCheckInterval(10000);
realm.Evaluate("while(true){}");
// rt.MainRealm.Evaluate("++++++++++++[");
//Thread.Sleep(2);
// Console.WriteLine(result.ToString());
