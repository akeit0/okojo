"""Run pinned V8 and QuickJS JavaScript workloads across engines."""

import argparse
import hashlib
import json
import math
import platform
import re
import statistics
import subprocess
import tempfile
import urllib.request
from pathlib import Path


HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
V8_URL = "https://raw.githubusercontent.com/v8/v8/4.5.1/benchmarks/"
MICRO_INPUTS = {
    "ekuma": (
        "888481da0b69b369333bfcbf52156273954f8c72",
        "c0085abf015ffbf0a0b349d53d2f72c11e563146ee008384f1a85709907b4168",
        "microbench.js",
    ),
    "quickjs-release": (
        "3d5e064e9dd67c70f7962836505a7fa067bf0a4e",
        "4abf0c34fd83cef925fd2f36009a1043f5009e98fdf6efe55e183a31dddd75f3",
        "quickjs-release.js",
    ),
}
# Input hashes from Ekuma _bench/internal/comparison/suites.go.
V8_INPUTS = {
    "base.js": "ac383759c3cbf5f3a499ade526a4e296f7925733d8c3889b6d5840ce6867ff51",
    "crypto.js": "d8b885eadc8be11c28f79543876beaa85bd70c71cfd5265c020005f43b59f48a",
    "deltablue.js": "ca04067f896887da902efd7117cfb49ecc36fcad937922edca65a5de9d9fc96e",
    "earley-boyer.js": "7bfa888839dee0939798d0f5da251f0ecb30fc5ea2ee52747b4adc5b0386e6ad",
    "navier-stokes.js": "ce241465ffeee77b5c4f7119f522aa971fe1fe92c3aed76f7514ea43e07fec84",
    "raytrace.js": "d52183ca324cdb88d9ee3e78326433fe2ca28d6436f0a6c3ec013ab67cb798eb",
    "regexp.js": "f04a1b4817e5d687363e99d941cc78de9b29b722b46cc034d877f59a269bdfdf",
    "richards.js": "d86eac4108343a54ba6e10621a77b46f82f554c3b0a46e8fb4032d5cf65a2a11",
    "splay.js": "1f875cec579d03af40d256ceaeb6823f3ef16c883652650ca51d183472a8185d",
}
V8_CASES = {
    "Richards": "richards.js",
    "DeltaBlue": "deltablue.js",
    "Encrypt": "crypto.js",
    "Decrypt": "crypto.js",
    "RayTrace": "raytrace.js",
    "Earley": "earley-boyer.js",
    "Boyer": "earley-boyer.js",
    "RegExp": "regexp.js",
    "Splay": "splay.js",
    "NavierStokes": "navier-stokes.js",
}


def pinned_input(path, url, expected):
    if path.is_file():
        content = path.read_bytes()
    else:
        with urllib.request.urlopen(url, timeout=30) as response:
            content = response.read()
    if hashlib.sha256(content).hexdigest() != expected:
        raise ValueError(f"SHA-256 mismatch for {path}")
    if not path.is_file():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
    return content.decode("utf-8")


def v8_case(base, workload, name):
    return f"""{base}
{workload}
var benchmarkSelection = (function (name) {{
    var benchmarks = BenchmarkSuite.suites[0].benchmarks;
    for (var i = 0; i < benchmarks.length; i++) {{
        var benchmark = benchmarks[i];
        benchmark.Setup();
        if (benchmark.name === name) return benchmark;
        benchmark.run();
        benchmark.TearDown();
    }}
    throw new Error("unknown benchmark " + name);
}})({json.dumps(name)});
var benchmarkResult = benchmarkTime(function () {{ benchmarkSelection.run(); }});
benchmarkSelection.TearDown();
"""


def cases(cache, suite, match, micro_source="ekuma"):
    if suite in ("all", "v8"):
        chosen = [(name, filename) for name, filename in V8_CASES.items() if match.search(name)]
        if chosen:
            base = pinned_input(cache / "v8-v7/base.js", V8_URL + "base.js", V8_INPUTS["base.js"])
            for name, filename in chosen:
                workload = pinned_input(cache / "v8-v7" / filename, V8_URL + filename, V8_INPUTS[filename])
                yield "v8", name, 1, v8_case(base, workload, name)
    if suite in ("all", "micro"):
        revision, sha256, filename = MICRO_INPUTS[micro_source]
        url = f"https://raw.githubusercontent.com/bellard/quickjs/{revision}/tests/microbench.js"
        micro = pinned_input(cache / "microbench" / filename, url, sha256)
        names = re.search(r"var test_list = \[(.*?)\];", micro, re.S)[1].split(",")
        names += re.findall(r"test_list\.push\((\w+)\);", micro)
        definitions, _ = micro.rsplit('if (typeof scriptArgs === "undefined") {', 1)
        for name in (name.strip() for name in names):
            if not name or name == "sort_bench" or not match.search(name):
                continue
            iterations = 1 if name.startswith("string_build_large") else 100
            body = f"""{definitions}
var benchmarkResult = benchmarkTime(function () {{
    if (!({name}({iterations}) > 0)) throw new Error("{name} returned an invalid result");
}});
"""
            yield "micro", name, iterations, body


def run_engine(command, path, iterations, timeout):
    try:
        process = subprocess.run(
            [*command, str(path)], capture_output=True, text=True, encoding="utf-8",
            errors="replace", timeout=timeout,
        )
        if process.returncode:
            return {"error": (process.stderr or process.stdout).strip(), "exitCode": process.returncode}
        result = json.loads(process.stdout.strip().splitlines()[-1])
        batches, elapsed = result["batches"], result["elapsedMs"]
        if not (type(batches) is int and batches > 0 and math.isfinite(elapsed) and elapsed > 0):
            raise ValueError(f"invalid timing {result!r}")
        return {**result, "nsPerIteration": elapsed * 1_000_000 / (batches * iterations)}
    except subprocess.TimeoutExpired:
        return {"error": f"timed out after {timeout} s"}
    except (IndexError, KeyError, TypeError, ValueError) as error:
        return {"error": f"{error}\n{process.stdout}\n{process.stderr}".strip()}


def summarize(rows, engines, rounds):
    for suite in dict.fromkeys(row["suite"] for row in rows):
        by_name = [row for row in rows if row["suite"] == suite]
        common = [row for row in by_name if all(
            len(row["samples"][engine]) == rounds
            and all("nsPerIteration" in sample for sample in row["samples"][engine])
            for engine in engines
        )]
        print(f"{suite}: {len(common)}/{len(by_name)} cases valid in all {rounds} rounds")
        for engine in engines:
            ratios = [
                statistics.median(
                    sample["nsPerIteration"] / baseline["nsPerIteration"]
                    for sample, baseline in zip(row["samples"][engine], row["samples"]["ekuma"], strict=True)
                )
                for row in common
            ]
            if ratios:
                print(f"  {engine}: {statistics.geometric_mean(ratios):.3f}x Ekuma")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--okojo", type=Path, default=HERE / "Okojo/bin/Release/net10.0/OkojoRunner.dll",
        help="built Okojo adapter assembly",
    )
    parser.add_argument("--okojo-baseline", type=Path, help="optional second Okojo assembly for an A/B")
    parser.add_argument("--ekuma", required=True, type=Path, help="built Ekuma adapter")
    parser.add_argument("--quickjs", required=True, type=Path, help="native qjs executable")
    parser.add_argument("--cache", type=Path, default=ROOT / "artifacts/ekuma-study")
    parser.add_argument("--out", required=True, type=Path, help="raw sample JSON")
    parser.add_argument("--suite", choices=("all", "v8", "micro"), default="all")
    parser.add_argument(
        "--micro-source", choices=MICRO_INPUTS, default="ekuma",
        help="use Ekuma's pinned revision or the microbench.js bundled with the QuickJS release",
    )
    parser.add_argument("--match", type=re.compile, default=re.compile("."), help="case name regex")
    parser.add_argument("--rounds", type=int, default=3)
    parser.add_argument("--warmup-ms", type=int, default=100)
    parser.add_argument("--sample-ms", type=int, default=200)
    parser.add_argument("--timeout", type=int, default=120, help="per-process timeout in seconds")
    args = parser.parse_args()
    if min(args.rounds, args.sample_ms, args.timeout) < 1 or args.warmup_ms < 0:
        parser.error("rounds, sample and timeout must be positive, and warmup must be nonnegative")
    for path in (args.okojo, args.ekuma, args.quickjs, args.okojo_baseline):
        if path is not None and not path.is_file():
            parser.error(f"executable or assembly missing: {path}")
    engines = {
        "okojo": ["dotnet", str(args.okojo.resolve())],
        "v8-jitless": ["node", "--jitless", str(HERE / "node.cjs")],
        "ekuma": [str(args.ekuma.resolve())],
        "quickjs": [str(args.quickjs.resolve())],
    }
    if args.okojo_baseline is not None:
        engines["okojo-baseline"] = ["dotnet", str(args.okojo_baseline.resolve())]
    selected = list(cases(args.cache, args.suite, args.match, args.micro_source))
    if not selected:
        parser.error("no matching benchmarks")
    rows = [
        {"suite": suite, "name": name, "iterationsPerBatch": iterations,
         "samples": {engine: [] for engine in engines}}
        for suite, name, iterations, _ in selected
    ]
    config = {
        "rounds": args.rounds, "warmupMs": args.warmup_ms, "sampleMs": args.sample_ms,
        "microSource": args.micro_source,
    }
    report = {"platform": platform.platform(), "config": config, "cases": rows}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    timer = (HERE / "measure.js").read_text(encoding="utf-8")
    with tempfile.TemporaryDirectory(dir=args.cache) as directory:
        paths = []
        for _, name, _, body in selected:
            path = Path(directory) / f"{name}.js"
            path.write_text(
                f"var benchmarkWarmupMs = {args.warmup_ms};\n"
                f"var benchmarkSampleMs = {args.sample_ms};\n{timer}\n{body}\n"
                'var benchmarkOutput = JSON.stringify(benchmarkResult);\n'
                'if (typeof print === "function") print(benchmarkOutput);\nbenchmarkOutput;\n',
                encoding="utf-8",
            )
            paths.append(path)
        order = list(engines)
        for round_index in range(args.rounds):
            for case_index, (row, path) in enumerate(zip(rows, paths, strict=True)):
                offset = (round_index + case_index) % len(order)
                for engine in order[offset:] + order[:offset]:
                    sample = run_engine(engines[engine], path, row["iterationsPerBatch"], args.timeout)
                    row["samples"][engine].append(sample)
                    if "error" in sample:
                        print(f"{row['name']} {engine}: {sample['error'][:180]}", flush=True)
                print(f"round {round_index + 1}/{args.rounds} {row['suite']}/{row['name']}", flush=True)
                args.out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    summarize(rows, engines, args.rounds)


if __name__ == "__main__":
    main()
