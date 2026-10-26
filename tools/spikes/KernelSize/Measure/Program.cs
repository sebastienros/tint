// Kernel-size spike, step 3: first-use cost and matching speed of the C# grammar's patterns,
// source-generated kernels (Tint.Spike.CSharp) vs lazy interpreted Parse (Tint.Spike.CSharpLazy).
//
// Usage:
//   Measure first kernel|lazy   one cold run in this process: All(), then one search per pattern
//   Measure throughput          correctness cross-check, then ns/search for the 30 most-matching patterns
// run_measure.py starts fresh processes for the cold runs and collects the JSON lines.

using System.Diagnostics;
using GregsEx;
using Tint.Spike;

var mode = args.Length > 0 ? args[0] : "throughput";
var lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "lines.txt")).Select(l => l + "\n").ToArray();

if (mode == "first")
{
    var lazy = args.Length > 1 && args[1] == "lazy";
    var retainedBefore = GC.GetTotalMemory(true);
    var allocatedBefore = GC.GetTotalAllocatedBytes(true);
    var start = Stopwatch.GetTimestamp();
    var patterns = lazy ? CSharpLazyPatterns.All() : CSharpPatterns.All();
    var construct = Stopwatch.GetElapsedTime(start);
    var allocatedAfter = GC.GetTotalAllocatedBytes(true);
    var retainedAfter = GC.GetTotalMemory(true);

    // First search of every pattern (JIT of the kernel's Run methods, workspace creation).
    start = Stopwatch.GetTimestamp();
    var line = lines[7];
    var matched = 0;
    foreach (var pattern in patterns)
    {
        var workspace = pattern.CreateWorkspace();
        var captures = new RegexCapture[pattern.GroupCount];
        if (workspace.FindCaptures(line, 0, captures, out _) == RegexSearchStatus.Match)
        {
            matched++;
        }
    }

    var firstSearch = Stopwatch.GetElapsedTime(start);
    var allocatedSearch = GC.GetTotalAllocatedBytes(true);
    var retainedSearch = GC.GetTotalMemory(true);
    var compiled = patterns.Count(p => p.ExecutionBackend == RegexExecutionBackend.Compiled);
    GC.KeepAlive(patterns);
    Console.WriteLine(
        $"{{\"variant\":\"{(lazy ? "lazy" : "kernel")}\",\"patterns\":{patterns.Length},\"compiled\":{compiled}," +
        $"\"construct_ms\":{construct.TotalMilliseconds:F3},\"construct_us_per_pattern\":{construct.TotalMicroseconds / patterns.Length:F2}," +
        $"\"allocated_bytes\":{allocatedAfter - allocatedBefore},\"retained_bytes\":{retainedAfter - retainedBefore}," +
        $"\"first_search_allocated_bytes\":{allocatedSearch - allocatedAfter},\"retained_after_search_bytes\":{retainedSearch - retainedBefore}," +
        $"\"first_search_ms\":{firstSearch.TotalMilliseconds:F3},\"first_search_us_per_pattern\":{firstSearch.TotalMicroseconds / patterns.Length:F2},\"matched\":{matched}}}");
    return 0;
}

var kernels = CSharpPatterns.All();
var interpreted = CSharpLazyPatterns.All();
var kernelWorkspaces = kernels.Select(p => p.CreateWorkspace()).ToArray();
var interpretedWorkspaces = interpreted.Select(p => p.CreateWorkspace()).ToArray();
var captureBuffer = new RegexCapture[kernels.Max(p => p.GroupCount)];
var interpretedBuffer = new RegexCapture[captureBuffer.Length];

// Search positions: 0 and three positions inside the line.
int[] Positions(string line) => [0, line.Length / 4, line.Length / 2, 3 * line.Length / 4];

// Cross-check: kernel and interpreter must agree on every (pattern, line, position).
var mismatches = 0;
var matchCounts = new int[kernels.Length];
var searches = 0;
for (var i = 0; i < kernels.Length; i++)
{
    var groups = kernels[i].GroupCount;
    foreach (var line in lines)
    {
        foreach (var position in Positions(line))
        {
            searches++;
            var k = kernelWorkspaces[i].FindCaptures(line, position, captureBuffer.AsSpan(0, groups), out var kr);
            var n = interpretedWorkspaces[i].FindCaptures(line, position, interpretedBuffer.AsSpan(0, groups), out var nr);
            if (k != n || (k == RegexSearchStatus.Match && (kr != nr || !captureBuffer.AsSpan(0, groups).SequenceEqual(interpretedBuffer.AsSpan(0, groups)))))
            {
                mismatches++;
                Console.Error.WriteLine($"mismatch: P{i} at {position} on '{line.TrimEnd()}': kernel {k} {kr}, interpreter {n} {nr}");
            }

            if (n == RegexSearchStatus.Match)
            {
                matchCounts[i]++;
            }
        }
    }
}

Console.WriteLine($"{{\"crosscheck_searches\":{searches},\"mismatches\":{mismatches}," +
    $"\"kernel_backed\":{kernels.Count(p => p.ExecutionBackend == RegexExecutionBackend.Compiled)},\"patterns\":{kernels.Length}}}");

// The 30 patterns with the highest match rate (ties: lower index), counting only kernel-backed ones
// separately so the declined ones (interpreter on both sides) do not dilute the speedup.
var top = Enumerable.Range(0, kernels.Length)
    .OrderByDescending(i => matchCounts[i])
    .ThenBy(i => i)
    .Take(30)
    .ToArray();

const int Iterations = 10_000;
var speedups = new List<double>();
var kernelOnlySpeedups = new List<double>();
var sumKernel0 = 0.0;
var sumInterp0 = 0.0;
var sumKernelIn = 0.0;
var sumInterpIn = 0.0;
foreach (var i in top)
{
    var groups = kernels[i].GroupCount;
    var k0 = Time(kernelWorkspaces[i], captureBuffer, groups, inner: false);
    var n0 = Time(interpretedWorkspaces[i], interpretedBuffer, groups, inner: false);
    var kIn = Time(kernelWorkspaces[i], captureBuffer, groups, inner: true);
    var nIn = Time(interpretedWorkspaces[i], interpretedBuffer, groups, inner: true);
    sumKernel0 += k0;
    sumInterp0 += n0;
    sumKernelIn += kIn;
    sumInterpIn += nIn;
    var speedup = (n0 + 3 * nIn) / (k0 + 3 * kIn);
    speedups.Add(speedup);
    var isKernel = kernels[i].ExecutionBackend == RegexExecutionBackend.Compiled;
    if (isKernel)
    {
        kernelOnlySpeedups.Add(speedup);
    }

    Console.WriteLine($"{{\"pattern\":{i},\"kernel\":{(isKernel ? "true" : "false")},\"match_rate\":{matchCounts[i] / 80.0:F3}," +
        $"\"kernel_ns_pos0\":{k0:F1},\"interp_ns_pos0\":{n0:F1},\"kernel_ns_inner\":{kIn:F1},\"interp_ns_inner\":{nIn:F1},\"speedup\":{speedup:F2}}}");
}

Console.WriteLine($"{{\"summary\":true,\"mean_kernel_ns_pos0\":{sumKernel0 / top.Length:F1},\"mean_interp_ns_pos0\":{sumInterp0 / top.Length:F1}," +
    $"\"mean_kernel_ns_inner\":{sumKernelIn / top.Length:F1},\"mean_interp_ns_inner\":{sumInterpIn / top.Length:F1}," +
    $"\"geomean_speedup\":{GeoMean(speedups):F2},\"geomean_speedup_kernel_backed\":{GeoMean(kernelOnlySpeedups):F2},\"kernel_backed_in_top\":{kernelOnlySpeedups.Count}}}");
// The top 30 by match rate is dominated by trivial patterns ($, \G, lookarounds) that match
// everywhere; a tokenizer mostly runs patterns that fail. Same timing over every pattern.
var all = new List<double>();
var allKernel = new List<double>();
var allMatched = new List<double>();
for (var i = 0; i < kernels.Length; i++)
{
    var groups = kernels[i].GroupCount;
    var k = Time(kernelWorkspaces[i], captureBuffer, groups, inner: false) + 3 * Time(kernelWorkspaces[i], captureBuffer, groups, inner: true);
    var n = Time(interpretedWorkspaces[i], interpretedBuffer, groups, inner: false) + 3 * Time(interpretedWorkspaces[i], interpretedBuffer, groups, inner: true);
    all.Add(n / k);
    if (kernels[i].ExecutionBackend == RegexExecutionBackend.Compiled)
    {
        allKernel.Add(n / k);
        if (matchCounts[i] > 0)
        {
            allMatched.Add(n / k);
        }
    }
}

allKernel.Sort();
Console.WriteLine($"{{\"all_patterns\":true,\"geomean_speedup\":{GeoMean(all):F2},\"geomean_speedup_kernel_backed\":{GeoMean(allKernel):F2}," +
    $"\"geomean_speedup_kernel_backed_matching\":{GeoMean(allMatched):F2},\"kernel_backed\":{allKernel.Count},\"kernel_backed_matching\":{allMatched.Count}," +
    $"\"kernel_slower\":{allKernel.Count(x => x < 1)},\"p10\":{allKernel[allKernel.Count / 10]:F2},\"median\":{allKernel[allKernel.Count / 2]:F2},\"p90\":{allKernel[allKernel.Count * 9 / 10]:F2},\"max\":{allKernel[^1]:F2}}}");
return mismatches == 0 ? 0 : 1;

// ns per search: Iterations rounds over the 20 lines (line = round % 20); position 0 only, or the
// three inner positions. 1,000 warm-up rounds first, then the best of five trials.
double Time(OnigurumaWorkspace workspace, RegexCapture[] buffer, int groups, bool inner)
{
    Rounds(workspace, buffer, groups, inner, 1_000);
    var best = double.MaxValue;
    for (var trial = 0; trial < 5; trial++)
    {
        var start = Stopwatch.GetTimestamp();
        var count = Rounds(workspace, buffer, groups, inner, Iterations);
        best = Math.Min(best, Stopwatch.GetElapsedTime(start).TotalNanoseconds / count);
    }

    return best;
}

long Rounds(OnigurumaWorkspace workspace, RegexCapture[] buffer, int groups, bool inner, int rounds)
{
    var span = buffer.AsSpan(0, groups);
    var count = 0L;
    for (var round = 0; round < rounds; round++)
    {
        var line = lines[round % lines.Length];
        if (!inner)
        {
            workspace.FindCaptures(line, 0, span, out _);
            count++;
            continue;
        }

        workspace.FindCaptures(line, line.Length / 4, span, out _);
        workspace.FindCaptures(line, line.Length / 2, span, out _);
        workspace.FindCaptures(line, 3 * line.Length / 4, span, out _);
        count += 3;
    }

    return count;
}

static double GeoMean(List<double> values) => values.Count == 0 ? double.NaN : Math.Exp(values.Average(Math.Log));
