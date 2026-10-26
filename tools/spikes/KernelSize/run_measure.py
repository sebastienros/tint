#!/usr/bin/env python3
"""Kernel-size spike, step 3 driver: runs Measure's cold first-use mode in fresh processes (median
of RUNS) and its throughput mode once, writing results/measure.json.

Usage: python3 -I run_measure.py
"""
import json
import os
import statistics
import subprocess

ROOT = os.path.dirname(os.path.abspath(__file__))
RESULTS = os.path.join(ROOT, "results")
RUNS = 7


def main():
    project = os.path.join(ROOT, "Measure")
    subprocess.run(["dotnet", "build", "-c", "Release", "-nologo", "-v:q"], cwd=project, check=True)
    exe = os.path.join(project, "bin", "Release", "net10.0", "Measure.dll")

    first = {}
    for variant in ("kernel", "lazy"):
        runs = []
        for _ in range(RUNS):
            out = subprocess.run(["dotnet", exe, "first", variant], capture_output=True, text=True, check=True).stdout
            runs.append(json.loads(out.strip().splitlines()[-1]))
        median = {k: statistics.median(r[k] for r in runs) for k in runs[0] if isinstance(runs[0][k], (int, float))}
        first[variant] = {"runs": runs, "median": median}
        print(variant, json.dumps(median))

    proc = subprocess.run(["dotnet", exe, "throughput"], capture_output=True, text=True)
    rows = [json.loads(line) for line in proc.stdout.splitlines() if line.startswith("{")]
    for row in rows:
        print(json.dumps(row))
    if proc.stderr:
        print(proc.stderr)

    os.makedirs(RESULTS, exist_ok=True)
    with open(os.path.join(RESULTS, "measure.json"), "w", encoding="utf-8") as f:
        json.dump({"first": first, "throughput": rows, "exit_code": proc.returncode, "stderr": proc.stderr}, f, indent=1)


if __name__ == "__main__":
    main()
