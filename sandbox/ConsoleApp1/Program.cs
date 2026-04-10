using Okojo.Compiler;
using Okojo.Objects;
using Okojo.Parsing;
using Okojo.Runtime;

//Console.WriteLine($"StackFrame {Unsafe.SizeOf<StackFrame>()}");
//Console.WriteLine($"SavedCallFrame {Unsafe.SizeOf<SavedCallFrame>()}");
//return;
var source = ScriptSourceLoader.LoadScenario("pure-function-call");

var managedVm = JsRuntime.Create().DefaultRealm;
managedVm.Execute(new JsCompiler(managedVm).Compile(JavaScriptParser.ParseScript(source)));
var function = (JsBytecodeFunction)managedVm.Accumulator.AsObject();
for (var i = 0; i < 40000; i++) managedVm.Execute(function);
//Thread.Sleep(2);
// Console.WriteLine(result.ToString());
