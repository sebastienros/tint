namespace Tint.Benchmarks;

/// <summary>Tokenizes a workload with Tint: a file line by line, or documentation fences each from the initial state.</summary>
public sealed class TintRunner
{
    private readonly (Tokenizer Tokenizer, string[][] Blocks)[] _work;

    private TintRunner((Tokenizer, string[][])[] work)
    {
        _work = work;
    }

    /// <summary>
    /// Creates the runner of a workload: a sample file name (<c>bigfile.cs</c>, <c>medium.cs</c>…) tokenized with C#,
    /// a docs language (<c>docs:typescript</c>), or <c>docs</c> for every docs language.
    /// </summary>
    public static TintRunner Create(string workload, Theme theme, TokenizerOptions options, TintGrammarSet grammars = TintGrammarSet.Loader)
    {
        if (workload == "docs" || workload.StartsWith("docs:", StringComparison.Ordinal))
        {
            var registry = Workloads.AllGrammarsRegistry(options, grammars);
            var languages = workload == "docs" ? Workloads.DocsScopes.Keys.ToArray() : [workload[5..]];
            return new TintRunner([.. languages.Select(l => (registry.CreateTokenizer(Workloads.DocsScopes[l], theme), Workloads.DocsBlocks(l)))]);
        }

        var csharp = Workloads.CSharpRegistry(grammars);
        csharp.TokenizerOptions = options;
        return new TintRunner([(csharp.CreateTokenizer("source.cs", theme), new[] { Workloads.ReadLines(workload) })]);
    }

    /// <summary>The UTF-8 size of the workload.</summary>
    public static long Bytes(string workload)
    {
        if (workload == "docs" || workload.StartsWith("docs:", StringComparison.Ordinal))
        {
            var languages = workload == "docs" ? Workloads.DocsScopes.Keys.ToArray() : [workload[5..]];
            return languages.Sum(l => Workloads.DocsBlocks(l).Sum(Workloads.Utf8Bytes));
        }

        return Workloads.Utf8Bytes(Workloads.ReadLines(workload));
    }

    /// <summary>The tokens through the encoded (<c>tokenizeLine2</c>) path.</summary>
    public int Run()
    {
        var count = 0;
        foreach (var (tokenizer, blocks) in _work)
        {
            foreach (var block in blocks)
            {
                var state = StateStack.Initial;
                foreach (var line in block)
                {
                    count += tokenizer.TokenizeLineEncoded(line, state, out state).Length;
                }
            }
        }

        return count;
    }

    /// <summary>The patterns running generated code, over the runner's tokenizers.</summary>
    public int CompiledPatternCount => _work.Sum(w => w.Tokenizer.CompiledPatternCount);
}
