# Okojo.Diagnostics

`Okojo.Diagnostics` provides formatting and disassembly helpers for debugging Okojo runtimes.

Useful entry points include:

- `DebugFormatter.FormatForRepl(...)`
- `DebugFormatter.FormatValue(...)`
- `Disassembler.Dump(...)`

## Example

```csharp
using Okojo.Diagnostics;

var text = DebugFormatter.FormatForRepl(realm, value);
var disasm = Disassembler.Dump(script);
```
