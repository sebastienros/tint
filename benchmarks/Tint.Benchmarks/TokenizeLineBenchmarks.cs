using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Tint.Loading;

namespace Tint.Benchmarks;

/// <summary>
/// Tokenizes a C# file line by line with the VS Code C# grammar and Dark+, once warm: the whole file per operation,
/// through the <see cref="Token"/> and the <see cref="EncodedToken"/> paths.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class TokenizeLineBenchmarks
{
    private string[] _lines = [];
    private Tokenizer _tokenizer = null!;

    /// <summary>The sample file, from <c>benchmarks/samples</c>.</summary>
    [Params("sample.cs", "medium.cs")]
    public string File { get; set; } = "sample.cs";

    /// <summary>Loads the grammar and theme, then tokenizes the file once so every rule reached is compiled.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var root = RepositoryRoot();
        var registry = new GrammarRegistry();
        registry.Add(GrammarLoader.Load(Path.Combine(root, "grammars", "csharp", "syntaxes", "csharp.tmLanguage.json")));
        var theme = Theme.FromSource(ThemeLoader.Load(Path.Combine(root, "themes", "theme-defaults", "dark_plus.json")));
        _tokenizer = registry.CreateTokenizer("source.cs", theme);
        _lines = System.IO.File.ReadAllLines(Path.Combine(root, "benchmarks", "samples", File));
        Tokens();
        Encoded();
    }

    /// <summary>The <see cref="Token"/> path.</summary>
    [Benchmark(Baseline = true)]
    public int Tokens()
    {
        var count = 0;
        var state = StateStack.Initial;
        foreach (var line in _lines)
        {
            count += _tokenizer.TokenizeLine(line, state, out state).Length;
        }

        return count;
    }

    /// <summary>The <see cref="EncodedToken"/> path.</summary>
    [Benchmark]
    public int Encoded()
    {
        var count = 0;
        var state = StateStack.Initial;
        foreach (var line in _lines)
        {
            count += _tokenizer.TokenizeLineEncoded(line, state, out state).Length;
        }

        return count;
    }

    private static string RepositoryRoot([CallerFilePath] string file = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
