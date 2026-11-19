# Okojo, V8 jitless, Ekuma and native QuickJS

Run the same JavaScript on all four engines. The inputs are V8 benchmark
suite v7 at V8 tag `4.5.1` and the 74 functions in Ekuma's pinned QuickJS
`microbench.js`. The runner checks Ekuma's input hashes as it loads each
file. Use `--micro-source quickjs-release` to select the 71 comparable
functions bundled with the pinned QuickJS 2026-06-04 release. Both sources
are hash-checked. The self-timed `sort_bench` belongs to QuickJS's native
driver and runs separately with `qjs --std tests/microbench.js sort_bench`.
[Ekuma's upstream suite](https://github.com/hajimehoshi/ekuma/blob/41a55ec4e7cda95eb97417fb05a3b4dac7e06a6a/_bench/README.md)
[Bundled QuickJS suite](https://github.com/bellard/quickjs/blob/3d5e064e9dd67c70f7962836505a7fa067bf0a4e/tests/microbench.js)

Each sample starts a fresh process and realm, loads one script and warms its
workload before timing. The script uses `Date.now()` around a batch loop.
Process startup, file I/O, initial script evaluation and benchmark setup
run outside that interval. The engines use their normal GC settings.
Okojo uses Release .NET and V8 uses Node's `--jitless` with an ordinary
global object. [Node VM documentation](https://nodejs.org/api/vm.html#vmconstantsdont_contextify)

A V8 iteration is one workload call. The harness runs its setup, any
prerequisite workload, and teardown outside the timer. The QuickJS
microbenchmark numbers are divided by the supplied `n` argument. Some
workloads perform multiple operations per unit of `n`. Calls use `n = 100`
except for the two large concatenation workloads, which use `n = 1`.
The same function and argument run on every engine. QuickJS's native
driver uses the function's returned operation count and an adaptive
batch size. Ekuma's upstream Go harness uses a separate timer.

The runner rotates engine order, keeps raw samples and errors, and reports
the geometric mean of the median same-round ratios to Ekuma. A comparison
uses the workloads that completed on all four engines in every round.
Results measure warmed execution on one host. Built-in implementation and
garbage collection contribute to wall time.

## Reproduce on Windows

Install Python 3.10 or later, Node with `vm.constants.DONT_CONTEXTIFY`,
the .NET 10 runtime and SDK, Go 1.27.4, and MSYS2 make with MinGW GCC.
Run from the repository root in PowerShell. QuickJS is native C at the
revision used by Ekuma's upstream comparison.

```powershell
$cache = Join-Path (Get-Location) 'artifacts/ekuma-study'
New-Item -ItemType Directory -Force $cache | Out-Null
$revision = '3d5e064e9dd67c70f7962836505a7fa067bf0a4e'
$archive = Join-Path $cache 'quickjs.tar.gz'
curl.exe -fLS "https://codeload.github.com/bellard/quickjs/tar.gz/$revision" -o $archive
if ($LASTEXITCODE) { throw 'QuickJS download failed' }
if ((Get-FileHash $archive).Hash -ne 'AFCCD11533E21A4E49AF43E6909FC7236ED0B92F25E01E1061CEB0B47E15E05D') {
    throw 'QuickJS archive hash mismatch'
}
tar -xf $archive -C $cache
$qjs = Join-Path $cache "quickjs-$revision"
$env:Path = "C:\msys64\mingw64\bin;C:\msys64\usr\bin;$env:Path"
make -C $qjs CONFIG_WIN32=y CROSS_PREFIX= qjs.exe

dotnet build benchmarks/EngineComparison/Okojo/OkojoRunner.csproj -c Release
Push-Location benchmarks/EngineComparison/ekuma
go build -o "$cache/ekuma-runner.exe" .
Pop-Location

python benchmarks/EngineComparison/compare.py `
    --ekuma "$cache/ekuma-runner.exe" --quickjs "$qjs/qjs.exe" `
    --out "$cache/results.json"
```

Use `--suite v8` or `--suite micro` to select a suite, and `--match` to
select case names with a regular expression. Pass `--okojo` to compare
against an existing adapter assembly from another Okojo build.
`--okojo-baseline` adds a second Okojo build to the same rotation for a
paired comparison. Keep its adapter assembly beside its Release
dependencies. The JSON contains each sample's elapsed time, work count
and failure, where applicable.

[Measured results](RESULTS.md)
