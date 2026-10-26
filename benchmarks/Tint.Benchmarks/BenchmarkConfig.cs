using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Tint.Benchmarks;

/// <summary>Adds the MB/s column and skips TextMateLib benchmarks when its native library does not load.</summary>
public sealed class BenchmarkConfig : ManualConfig
{
    /// <summary>The category of benchmarks that need TextMateLib's native library.</summary>
    public const string TextMateLib = "TextMateLib";

    /// <summary>Creates the configuration.</summary>
    public BenchmarkConfig()
    {
        AddColumn(new ThroughputColumn());
        AddFilter(new SimpleFilter(benchmark =>
            !benchmark.Descriptor.Categories.Contains(TextMateLib) || TextMateLibAvailability.IsAvailable));
    }
}

/// <summary>Whether TextMateLib's native library loads on this platform.</summary>
public static class TextMateLibAvailability
{
    private static readonly Lazy<bool> s_available = new(() =>
    {
        try
        {
            using var registry = new TextMateLib.Bindings.Registry();
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or TypeInitializationException)
        {
            Console.WriteLine($"// TextMateLib benchmarks skipped: its native library does not load on this platform ({e.GetType().Name}: {e.Message}).");
            return false;
        }
    });

    /// <summary>Whether the library loads.</summary>
    public static bool IsAvailable => s_available.Value;
}

/// <summary>
/// Throughput in MB/s (10^6 bytes of UTF-8 source per second) of the benchmarks whose class implements
/// <see cref="IThroughputWorkload"/>.
/// </summary>
public sealed class ThroughputColumn : IColumn
{
    /// <inheritdoc/>
    public string Id => "Throughput";

    /// <inheritdoc/>
    public string ColumnName => "MB/s";

    /// <inheritdoc/>
    public bool AlwaysShow => true;

    /// <inheritdoc/>
    public ColumnCategory Category => ColumnCategory.Custom;

    /// <inheritdoc/>
    public int PriorityInCategory => 0;

    /// <inheritdoc/>
    public bool IsNumeric => true;

    /// <inheritdoc/>
    public UnitType UnitType => UnitType.Dimensionless;

    /// <inheritdoc/>
    public string Legend => "Source megabytes (10^6 UTF-8 bytes) tokenized per second";

    /// <inheritdoc/>
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) => GetValue(summary, benchmarkCase, summary.Style);

    /// <inheritdoc/>
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        var mean = summary[benchmarkCase]?.ResultStatistics?.Mean;
        if (mean is not > 0 || Activator.CreateInstance(benchmarkCase.Descriptor.Type) is not IThroughputWorkload workload)
        {
            return "-";
        }

        foreach (var parameter in benchmarkCase.Parameters.Items)
        {
            benchmarkCase.Descriptor.Type.GetProperty(parameter.Name)?.SetValue(workload, parameter.Value);
        }

        var bytes = workload.GetWorkloadBytes();
        return (bytes / (mean.Value / 1e9) / 1e6).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc/>
    public bool IsAvailable(Summary summary) => true;

    /// <inheritdoc/>
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
}

/// <summary>A benchmark class that knows the size of the source text one operation tokenizes.</summary>
public interface IThroughputWorkload
{
    /// <summary>The UTF-8 bytes per operation, computed from the current parameter values.</summary>
    long GetWorkloadBytes();
}
