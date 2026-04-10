# OkojoBytecodeTool

`OkojoBytecodeTool` disassembles Okojo script/function units from a JS file or inline JS string, and can print ES module
metadata.

## Usage

```powershell
dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- <js-file-or-string> [--filter <function-name>] [--list] [--save <path>] [--help]
```

Module metadata mode:

```powershell
dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- <js-file-or-string> --module-info [--save <path>]
```

```powershell
dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- <module-file> --module-info --resolved
```

Compare mode:

```powershell
dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- --compare <left-file-or-dir> <right-file-or-dir> [--save <path>]
```

Compare mode with opcode diff:

```powershell
dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- --compare <left-file-or-dir> <right-file-or-dir> --opcodes
```

Case snapshot mode:

```powershell
dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- --cases-snapshot
```

## Options

- `--list`  
  List discovered units only (`<script>`, function names) without disassembly.

- `--filter <name>`  
  Filter units by exact or substring match.

- `--save <path>`  
  Save rendered output to file (still prints to stdout).

- `--module-info`
  Parse input as ES module and print import/export/TLA metadata.

- `--resolved`
  With `--module-info`, resolve imports/re-exports recursively and print resolved module graph (file input only).

- `--help`, `-h`  
  Show help text.

- `--snapshot <name>`
  Save current disassembly to `artifacts/okojobytecodetool/snapshots/<timestamp>/<name>.disasm.txt`.

- `--compare <left> <right>`
  Compare two disasm files or two snapshot directories and print register deltas.

- `--opcodes`
  Include normalized opcode sequence diff (`same`/`diff`) and first mismatch in compare output.

- `--cases-snapshot`
  Snapshot all `artifacts/okojobytecodetool/cases/*.js` and auto-compare with latest compatible snapshot (same case file
  set).

- `--cases-dir <path>`
  Override case input directory for `--cases-snapshot`.

- `--snapshots-root <path>`
  Override snapshot root directory for `--cases-snapshot`.

## Examples

List units in a file:

```powershell
dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- hello_obj_comp.js --list
```

Disassemble only function `C`:

```powershell
dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- hello_class_comp.js --filter C
```

Disassemble inline snippet and save:

```powershell
dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- "class C { #x = 1; get(){ return this.#x; } } new C().get();" --save artifacts/okojobytecodetool/sample.txt
```

Print module metadata:

```powershell
dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- sandbox/ModuleSample/src/main.js --module-info
```

Print module metadata with resolved graph:

```powershell
dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- sandbox/ModuleSample/src/main.js --module-info --resolved
```

Create a named snapshot:

```powershell
dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- hello_obj_comp.js --snapshot hello_obj_comp_after_recycle
```

Compare two snapshots (directories):

```powershell
dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- --compare artifacts/okojobytecodetool/snapshots/20260303-101010 artifacts/okojobytecodetool/snapshots/20260303-113050
```

Create case snapshot + compatible compare:

```powershell
dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- --cases-snapshot
```

Create case snapshot + compatible compare with opcode diff:

```powershell
dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- --cases-snapshot --opcodes
```

## Baseline Outputs

Current baseline captures are stored under:

- `artifacts/okojobytecodetool/baseline/`

## Snapshot Workflow

Treat `artifacts/okojobytecodetool/cases/*.js` as stable inputs only.
Write disassembly snapshots to a timestamped directory:

```powershell
$ts = Get-Date -Format 'yyyyMMdd-HHmmss'
$outDir = "artifacts/okojobytecodetool/snapshots/$ts"
New-Item -ItemType Directory -Force $outDir | Out-Null
Get-ChildItem artifacts/okojobytecodetool/cases/*.js | ForEach-Object {
  dotnet run --project tools/OkojoBytecodeTool/OkojoBytecodeTool.csproj -- $_.FullName --save (Join-Path $outDir ($_.BaseName + '.disasm.txt'))
}
```

Helper script (auto-compare with latest compatible snapshot):

```powershell
powershell -ExecutionPolicy Bypass -File tools/OkojoBytecodeTool/scripts/Snapshot-And-Compare.ps1
```
