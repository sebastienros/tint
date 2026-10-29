using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.DotNetCli;

namespace Tint.Benchmarks;

/// <summary>
/// Adds the MB/s column, skips TextMateLib benchmarks when its native library does not load, and runs C#-only
/// benchmarks only for the <c>csharp</c> value of a <c>Language</c> parameter.
/// </summary>
public sealed class BenchmarkConfig : ManualConfig
{
    /// <summary>The category of benchmarks that need TextMateLib's native library.</summary>
    public const string TextMateLib = "TextMateLib";

    /// <summary>The category of benchmarks that only have a C# variant (the kernel-compiled grammar).</summary>
    public const string CSharpOnly = "CSharpOnly";

    /// <summary>Creates the configuration.</summary>
    public BenchmarkConfig()
    {
        // BenchmarkDotNet rebuilds the project, Tint.Grammars and the kernel-compiled C# grammar included, per job.
        WithBuildTimeout(TimeSpan.FromMinutes(10));
        AddColumn(new ThroughputColumn());
        AddFilter(new SimpleFilter(benchmark =>
            !benchmark.Descriptor.Categories.Contains(TextMateLib) || TextMateLibAvailability.IsAvailable));
        AddFilter(new SimpleFilter(benchmark =>
            !benchmark.Descriptor.Categories.Contains(CSharpOnly)
            || benchmark.Parameters.Items.All(p => p.Name != "Language" || Equals(p.Value, "csharp"))));
    }
}

/// <summary>
/// <see cref="BenchmarkConfig"/> with one job that runs every measurement in a fresh process
/// (<see cref="RunStrategy.ColdStart"/>): the first passes of a file, which include the JIT of Tint and GregsEx. The
/// process is built from this project by path, since BenchmarkDotNet's own lookup refuses a checkout holding copies of
/// it (worktrees under <c>.claude/</c>).
/// </summary>
public sealed class ColdStartConfig : ManualConfig
{
    /// <summary>Creates the configuration.</summary>
    public ColdStartConfig()
    {
        Add(new BenchmarkConfig());
        AddJob(Job.Default
            .WithToolchain(RepositoryToolchain.Instance)
            .WithStrategy(RunStrategy.ColdStart)
            .WithLaunchCount(3)
            .WithWarmupCount(0)
            .WithIterationCount(10)
            .WithInvocationCount(1)
            .WithUnrollFactor(1));
    }
}

/// <summary>The .NET CLI toolchain building <c>benchmarks/Tint.Benchmarks/Tint.Benchmarks.csproj</c> by path.</summary>
public static class RepositoryToolchain
{
    private const string Framework = "net10.0";

    /// <summary>The toolchain.</summary>
    public static IToolchain Instance { get; } = new Toolchain("Repository", new RepositoryProjectGenerator(), new DotNetCliBuilder(Framework), new DotNetCliExecutor(null!));

    private sealed class RepositoryProjectGenerator() : CsProjGenerator(Framework, null!, null!, null!)
    {
        protected override FileInfo GetProjectFilePath(Type benchmarkTarget, ILogger logger)
            => new(Path.Combine(Workloads.RepositoryRoot, "benchmarks", "Tint.Benchmarks", "Tint.Benchmarks.csproj"));
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
