#!/usr/bin/env python3
"""Run the real .NET gates from the repository root (Python 3.10+)."""
from __future__ import annotations

import argparse
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def run(*args: str) -> None:
    print('+ ' + ' '.join(args), flush=True)
    subprocess.run(args, cwd=ROOT, check=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--test262-root', type=Path, help='Path to Test262 test/ directory; enables full sweep')
    parser.add_argument('--benchmarks', action='store_true', help='Run split benchmarks after correctness gates')
    options = parser.parse_args()
    if shutil.which('dotnet') is None:
        print('BLOCKED: dotnet is not installed. .NET 10 SDK is required. No C# gate was executed.')
        return 2
    if options.test262_root and not options.test262_root.resolve().is_dir():
        parser.error('--test262-root must be an existing Test262 test/ directory')

    run('dotnet', '--info')
    run('dotnet', 'tool', 'restore')
    changed = Path(__file__).with_name('changed-csharp.txt').read_text().splitlines()
    run('dotnet', 'csharpier', 'format', *changed)
    main_tests = 'tests/Okojo.Tests/Okojo.Tests.csproj'
    run('dotnet', 'test', main_tests, '-c', 'Release', '--filter',
        'FullyQualifiedName~CodeInstanceSplitTests')
    run('dotnet', 'test', main_tests, '-c', 'Release', '--no-build')
    for project in sorted((ROOT / 'tests').glob('*/*.csproj')):
        relative = project.relative_to(ROOT).as_posix()
        if relative != main_tests:
            run('dotnet', 'test', relative, '-c', 'Release')
    run('dotnet', 'build', 'Okojo.slnx', '-c', 'Release')

    if options.test262_root:
        run('dotnet', 'run', '--project', 'tools/Test262Runner/Test262Runner.csproj',
            '-c', 'Release', '--', '--root', str(options.test262_root.resolve()))
    else:
        print('NOT RUN: Test262 (provide --test262-root to complete the conformance gate).')
    if options.benchmarks:
        run('dotnet', 'run', '--project', 'benchmarks/Okojo.Benchmarks/Okojo.Benchmarks.csproj',
            '-c', 'Release', '--', '--filter', '*CodeInstanceSplit*')
    else:
        print('NOT RUN: performance benchmarks (provide --benchmarks).')
    return 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except subprocess.CalledProcessError as error:
        print(f'FAILED: gate exited with status {error.returncode}; later gates were not run.')
        raise SystemExit(error.returncode)
