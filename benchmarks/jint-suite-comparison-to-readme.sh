#!/usr/bin/env bash
# Converts JintSuiteComparisonBenchmarks BenchmarkDotNet CSV to the
# benchmarks/README.md summary table.
# Usage:
#   bash benchmarks/jint-suite-comparison-to-readme.sh [csv-path]
set -euo pipefail

CSV="${1:-BenchmarkDotNet.Artifacts/results/JintSuiteComparisonBenchmarks-report.csv}"

python3 - "$CSV" <<'PY'
import csv
import sys

path = sys.argv[1]

with open(path, encoding="utf-8-sig", newline="") as f:
    rows = list(csv.DictReader(f))

def parse_ns(s):
    return float(s.replace(",", "").replace("ns", "").strip())

def parse_alloc(s):
    return int(s.replace(",", "").replace("B", "").strip())

def fmt_ns(ns):
    if ns < 1000:
        return f"{ns:.2f} ns"
    if ns < 1_000_000:
        return f"{ns / 1000:.2f} us"
    return f"{ns / 1_000_000:.3f} ms"

def fmt_int(n):
    return f"{n:,}"

data = {}
for r in rows:
    key = r["FileName"]
    data.setdefault(key, {})[r["Method"]] = (parse_ns(r["Mean"]), parse_alloc(r["Allocated"]))

print("| Script | Parse Jint | Parse Okojo (ratio) | Parse alloc O/J | Exec Jint | Exec Okojo (ratio) | Exec alloc O/J |")
print("| --- | ---: | ---: | ---: | ---: | ---: | ---: |")
for key in sorted(data):
    d = data[key]
    jp, jo = d["Jint_ParseCompile"], d["Okojo_ParseCompile"]
    je, oe = d["Jint_Execute"], d["Okojo_Execute"]
    pr = jo[0] / jp[0]
    er = oe[0] / je[0]
    pa = jo[1] / jp[1]
    ea = oe[1] / je[1]
    print(
        f"| {key} "
        f"| {fmt_ns(jp[0])} "
        f"| {fmt_ns(jo[0])} ({pr:.2f}) "
        f"| {fmt_int(jo[1])} / {fmt_int(jp[1])} B ({pa:.2f}) "
        f"| {fmt_ns(je[0])} "
        f"| {fmt_ns(oe[0])} ({er:.2f}) "
        f"| {fmt_int(oe[1])} / {fmt_int(je[1])} B ({ea:.2f}) |"
    )
PY
