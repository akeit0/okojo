using Okojo.JavaScript;
using Okojo.JavaScript.Embedding;

var path = args.Single();
using var runtime = JsRuntime.CreateBuilder().Build();
var realm = runtime.DefaultRealm;
var script = realm.CompileScript(File.ReadAllText(path), path);
realm.Execute(script, pumpJobsAfterRun: false);
Console.WriteLine(realm.Accumulator.AsString());
