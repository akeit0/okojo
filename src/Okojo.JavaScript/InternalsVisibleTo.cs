using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Okojo.Tests")]
[assembly: InternalsVisibleTo("Okojo.JavaScript.Embedding")] // Lockstep CLR provider wiring.
[assembly: InternalsVisibleTo("Okojo.Compiler.Tests")]
[assembly: InternalsVisibleTo("Okojo.Benchmarks")]
[assembly: InternalsVisibleTo("CompilerAllocProbe")]
[assembly: InternalsVisibleTo("Okojo.Reflection")] // Lockstep CLR interop ABI and atom-based object overrides.
[assembly: InternalsVisibleTo("Okojo.Node")] // Lockstep Node profile: CommonJS compilation, nextTick, abstract operations, and dense/typed-array fast paths.
[assembly: InternalsVisibleTo("okojonode")] // Node CLI evaluation uses the planned compiler directly.
[assembly: InternalsVisibleTo("Okojo.Diagnostics")]
[assembly: InternalsVisibleTo("OkojoBytecodeTool")]
[assembly: InternalsVisibleTo("Okojo.DebugServer.Core")]
[assembly: InternalsVisibleTo("Test262Runner")]
[assembly: InternalsVisibleTo("Test262OpcodeHistogram")]
