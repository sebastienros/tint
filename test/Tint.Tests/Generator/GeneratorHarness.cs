extern alias TintGenerator;

using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Tint.Tests.Generator;

// An AdditionalFiles item as MSBuild hands it to the compiler: a path and the compiler-visible metadata.
internal sealed record TestFile(string Path, string Text, IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static TestFile Grammar(string path, string text, params (string Key, string Value)[] metadata)
        => new(path, text, metadata.Append(("TintKind", "Grammar")).ToDictionary(m => m.Item1, m => m.Item2));

    public static TestFile Theme(string path, string text, params (string Key, string Value)[] metadata)
        => new(path, text, metadata.Append(("TintKind", "Theme")).ToDictionary(m => m.Item1, m => m.Item2));
}

internal sealed record GeneratorRun(Compilation Compilation, ImmutableArray<Diagnostic> GeneratorDiagnostics, ImmutableArray<Diagnostic> CompilationDiagnostics, IReadOnlyDictionary<string, string> Sources)
{
    public IEnumerable<Diagnostic> Tint(string id) => GeneratorDiagnostics.Where(d => d.Id == id);

    public string Source(string hintName) => Sources[hintName + ".g.cs"];

    // Errors and warnings of the compiled output, as a TreatWarningsAsErrors consumer sees them.
    public IEnumerable<Diagnostic> CompilationProblems => CompilationDiagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning);

    public Assembly Load()
    {
        using var stream = new MemoryStream();
        var result = Compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(20)));
        stream.Position = 0;
        return new AssemblyLoadContext("generated-" + Guid.NewGuid().ToString("N")).LoadFromStream(stream);
    }
}

internal static class GeneratorHarness
{
    public static string ProjectDirectory { get; } = Path.Combine(Path.GetTempPath(), "tint-generator-tests", "project");

    // GregsEx from its package layout (<package>/lib/<tfm>/GregsEx.dll), the reference the generator derives
    // GregsEx.Generator.dll from.
    public static string GregsExPackagePath { get; } = typeof(GeneratorHarness).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "GregsExPackagePath").Value!;

    public static string RepositoryRoot { get; } = Path.GetFullPath(Path.Combine(SourceDirectory(), "..", "..", ".."));

    private static readonly Lazy<ImmutableArray<MetadataReference>> s_references = new(() =>
    {
        var references = new List<MetadataReference>();
        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name is "GregsEx" or "Tint" || !(name.StartsWith("System", StringComparison.Ordinal) || name.StartsWith("Microsoft", StringComparison.Ordinal) || name is "mscorlib" or "netstandard"))
            {
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(path));
        }

        references.Add(MetadataReference.CreateFromFile(typeof(GrammarRegistry).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(GregsExPackagePath, "lib", "net10.0", "GregsEx.dll")));
        return [.. references];
    });

    public static GeneratorRun Run(
        IEnumerable<TestFile> files,
        string source = "",
        IReadOnlyDictionary<string, string>? properties = null,
        bool referenceGregsEx = true,
        LanguageVersion languageVersion = LanguageVersion.CSharp12)
    {
        var parseOptions = new CSharpParseOptions(languageVersion);
        var references = s_references.Value;
        if (!referenceGregsEx)
        {
            references = [.. references.Where(r => !string.Equals(Path.GetFileName(r.Display), "GregsEx.dll", StringComparison.OrdinalIgnoreCase))];
        }

        var compilation = CSharpCompilation.Create(
            "Generated",
            [CSharpSyntaxTree.ParseText(source, parseOptions, path: Path.Combine(ProjectDirectory, "Source.cs"))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error));

        var additionalTexts = files.Select(f => new TestAdditionalText(Path.GetFullPath(Path.Combine(ProjectDirectory, f.Path)), f.Text, f.Metadata)).ToList();
        var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["build_property.projectdir"] = ProjectDirectory + Path.DirectorySeparatorChar,
            ["build_property.rootnamespace"] = "Test.Root",
        };
        foreach (var property in properties ?? new Dictionary<string, string>())
        {
            globals["build_property." + property.Key] = property.Value;
        }

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new TintGenerator::Tint.Generator.TintGenerator().AsSourceGenerator()],
            additionalTexts,
            parseOptions,
            new TestOptionsProvider(globals, additionalTexts));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        var result = driver.GetRunResult();
        var sources = result.Results.SelectMany(r => r.GeneratedSources).ToDictionary(s => s.HintName, s => s.SourceText.ToString());
        return new GeneratorRun(output, generatorDiagnostics, output.GetDiagnostics(), sources);
    }

    private static string SourceDirectory([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;

    private sealed class TestAdditionalText(string path, string text, IReadOnlyDictionary<string, string>? metadata) : AdditionalText
    {
        public override string Path { get; } = path;

        public IReadOnlyDictionary<string, string>? Metadata { get; } = metadata;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    private sealed class TestOptionsProvider(Dictionary<string, string> globals, List<TestAdditionalText> files) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(globals);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options([]);

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            var file = files.FirstOrDefault(f => ReferenceEquals(f, textFile));
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in file?.Metadata ?? new Dictionary<string, string>())
            {
                values["build_metadata.AdditionalFiles." + entry.Key] = entry.Value;
            }

            return new Options(values);
        }
    }

    private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (values.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
