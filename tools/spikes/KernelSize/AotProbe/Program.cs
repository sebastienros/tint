// NativeAOT probe: binary size with the C# grammar's kernels, its lazy Parse methods, or neither
// (GregsEx alone), and the cold cost of All() plus one search per pattern without a JIT.

using System.Diagnostics;
using GregsEx;

var line = "                            if (def.SizeType == LayoutTimeSizeType.Star && def.MeasureSize > 0.0)\n";
var start = Stopwatch.GetTimestamp();
#if VARIANT_KERNEL
var patterns = Tint.Spike.CSharpPatterns.All();
#elif VARIANT_LAZY
var patterns = Tint.Spike.CSharpLazyPatterns.All();
#else
OnigurumaPattern[] patterns = [OnigurumaPattern.Parse(@"\b(class|struct)\s+(\w+)", RegexProfiles.OnigurumaTextMate)];
#endif
var construct = Stopwatch.GetElapsedTime(start);
start = Stopwatch.GetTimestamp();
var matched = 0;
foreach (var pattern in patterns)
{
    var captures = new RegexCapture[pattern.GroupCount];
    if (pattern.CreateWorkspace().FindCaptures(line, 0, captures, out _) == RegexSearchStatus.Match)
    {
        matched++;
    }
}

var search = Stopwatch.GetElapsedTime(start);
Console.WriteLine($"{{\"patterns\":{patterns.Length},\"construct_ms\":{construct.TotalMilliseconds:F3},\"first_search_ms\":{search.TotalMilliseconds:F3},\"matched\":{matched}}}");
