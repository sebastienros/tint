#!/usr/bin/env python3
"""Kernel-size spike, step 2: builds each spike library and records build time, assembly size,
generated source size and GregsEx generator diagnostics into results/build-<project>.json.

Usage: python3 -I measure_build.py [Spike.CSharp ...]   (default: the four spike projects)
"""
import collections
import json
import os
import re
import shutil
import statistics
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.abspath(__file__))
RESULTS = os.path.join(ROOT, "results")
RUNS = 3


def run(args, cwd, output=False):
    start = time.perf_counter()
    proc = subprocess.run(args, cwd=cwd, capture_output=True, text=True)
    elapsed = time.perf_counter() - start
    if proc.returncode != 0:
        print(proc.stdout[-4000:], proc.stderr[-4000:])
        raise SystemExit(f"{' '.join(args)} failed in {cwd}")
    return (elapsed, proc.stdout) if output else elapsed


def clean(project_dir):
    shutil.rmtree(os.path.join(project_dir, "bin"), ignore_errors=True)
    shutil.rmtree(os.path.join(project_dir, "obj", "Release"), ignore_errors=True)
    shutil.rmtree(os.path.join(project_dir, "obj", "gen"), ignore_errors=True)


def build(project_dir, *extra, output=False):
    return run(["dotnet", "build", "-c", "Release", "--no-restore", "-nologo", *extra], project_dir, output)


# IsAotCompatible/IsTrimmable turn on the ILLink Roslyn analyzers; these switch them off.
NO_TRIM_ANALYZERS = ["-p:EnableTrimAnalyzer=false", "-p:EnableAotAnalyzer=false", "-p:EnableSingleFileAnalyzer=false"]


def seconds(pattern, log):
    m = re.search(pattern + r": ([0-9.]+) seconds", log)
    return float(m.group(1)) if m else None


def measure(project):
    project_dir = os.path.join(ROOT, project)
    assembly = "Tint." + project + ".dll"
    run(["dotnet", "restore", "-nologo"], project_dir)
    sarif = os.path.join(RESULTS, f"{project}.sarif")

    # Clean build with a cold compiler server, then warm clean builds.
    run(["dotnet", "build-server", "shutdown"], project_dir)
    clean(project_dir)
    cold = build(project_dir, f"-p:ErrorLog={sarif}%2Cversion=2.1")
    warm = []
    for _ in range(RUNS):
        clean(project_dir)
        warm.append(build(project_dir))
    noop = [build(project_dir) for _ in range(RUNS)]
    dll = os.path.join(project_dir, "bin", "Release", "net10.0", assembly)
    dll_bytes = os.path.getsize(dll)

    # Clean builds without the trim/AOT analyzers, and one build reporting analyzer/generator time.
    no_trim = []
    for _ in range(RUNS):
        clean(project_dir)
        no_trim.append(build(project_dir, *NO_TRIM_ANALYZERS))
    clean(project_dir)
    _, log = build(project_dir, "-v:d", "-p:ReportAnalyzer=true", output=True)
    analyzer_s = seconds("Total analyzer execution time", log)
    generator_s = seconds("Total generator execution time", log)
    illink = re.search(r"([0-9.]+)\s+\d+\s+ILLink\.RoslynAnalyzer,", log)
    illink_s = float(illink.group(1)) if illink else None

    # Same build with the generated files written to disk.
    clean(project_dir)
    gen_dir = os.path.join(project_dir, "obj", "gen")
    emit = build(project_dir, "-p:EmitCompilerGeneratedFiles=true", f"-p:CompilerGeneratedFilesOutputPath={gen_dir}")
    per_method = {}
    generated_bytes = 0
    for dirpath, _, files in os.walk(gen_dir):
        for name in files:
            size = os.path.getsize(os.path.join(dirpath, name))
            generated_bytes += size
            m = re.search(r"\.P(\d+)\.g\.cs$", name)
            if m:
                per_method[int(m.group(1))] = size
    shutil.rmtree(gen_dir, ignore_errors=True)

    source_files = [f for f in os.listdir(project_dir) if f.endswith(".cs")]
    source_bytes = sum(os.path.getsize(os.path.join(project_dir, f)) for f in source_files)

    # Generator diagnostics.
    with open(sarif, encoding="utf-8") as f:
        results = json.load(f)["runs"][0]["results"]
    gregex = [r for r in results if r["ruleId"].startswith("GREGEX")]
    by_id = collections.Counter(r["ruleId"] for r in gregex)
    reasons = collections.Counter()
    by_reason = collections.defaultdict(list)
    declined = []
    errors = []
    for r in gregex:
        text = r["message"]["text"]
        if r["ruleId"] == "GREGEX003":
            reason = text.split("runs on the interpreter:", 1)[-1].strip()
            reasons[reason] += 1
            m = re.match(r"'P(\d+)'", text)
            if m:
                declined.append(int(m.group(1)))
                by_reason[reason].append(int(m.group(1)))
        else:
            errors.append({"id": r["ruleId"], "level": r["level"], "message": text})
    os.remove(sarif)

    # Pattern lengths from the Emit manifest, to relate generated size to pattern length.
    manifest = os.path.join(project_dir, project.replace("Spike.", "") + "Patterns.tsv")
    lengths = {}
    with open(manifest, encoding="utf-8") as f:
        next(f)
        for line in f:
            index, length, _ = line.split("\t", 2)
            lengths[int(index)] = int(length)

    result = {
        "project": project,
        "assembly": assembly,
        "methods": len(lengths),
        "pattern_chars": sum(lengths.values()),
        "source_bytes": source_bytes,
        "generated_bytes": generated_bytes,
        "dll_bytes": dll_bytes,
        "build_clean_cold_s": round(cold, 2),
        "build_clean_warm_s": [round(x, 2) for x in warm],
        "build_clean_warm_median_s": round(statistics.median(warm), 2),
        "build_noop_s": [round(x, 2) for x in noop],
        "build_noop_median_s": round(statistics.median(noop), 2),
        "build_emit_generated_s": round(emit, 2),
        "build_clean_no_trim_analyzers_s": [round(x, 2) for x in no_trim],
        "build_clean_no_trim_analyzers_median_s": round(statistics.median(no_trim), 2),
        "analyzer_cpu_s": analyzer_s,
        "illink_analyzer_cpu_s": illink_s,
        "generator_s": generator_s,
        "diagnostics": dict(by_id),
        "declined": len(declined),
        "declined_reasons": dict(reasons.most_common()),
        "declined_methods": sorted(declined),
        "declined_by_reason": {k: sorted(v) for k, v in by_reason.items()},
        "errors": errors,
        "per_method": [
            {"index": i, "length": lengths.get(i, 0), "generated_bytes": per_method[i], "kernel": i not in declined}
            for i in sorted(per_method)
        ],
    }
    with open(os.path.join(RESULTS, f"build-{project}.json"), "w", encoding="utf-8") as f:
        json.dump(result, f, indent=1)
    summary = {k: v for k, v in result.items() if k not in ("per_method", "declined_methods", "declined_by_reason")}
    print(json.dumps(summary, indent=1))


def main():
    os.makedirs(RESULTS, exist_ok=True)
    projects = sys.argv[1:] or ["Spike.CSharp", "Spike.TypeScript", "Spike.Both", "Spike.CSharpLazy"]
    for project in projects:
        measure(project)


if __name__ == "__main__":
    main()
