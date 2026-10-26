using Tint.Grammars;

namespace Tint.Playground;

/// <summary>A language of the picker: a grammar with a language id.</summary>
/// <param name="Id">The VS Code language id, as in permalinks and sample file names (<c>csharp</c>).</param>
/// <param name="Label">The first alias, or the id (<c>C#</c>).</param>
/// <param name="ScopeName">The grammar's root scope (<c>source.cs</c>).</param>
public sealed record LanguageOption(string Id, string Label, string ScopeName);

/// <summary>Builds the language picker from grammars.</summary>
public static class LanguageCatalog
{
    /// <summary>
    /// One option per language id, the first grammar declaring it winning, sorted by label. Grammars without a language
    /// (injections and grammars only other grammars include) are left out.
    /// </summary>
    public static IReadOnlyList<LanguageOption> Build(IEnumerable<IGrammarSource> grammars)
    {
        ArgumentNullException.ThrowIfNull(grammars);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var options = new List<LanguageOption>();
        foreach (var grammar in grammars)
        {
            if (grammar.Info.LanguageId is not { Length: > 0 } id || !seen.Add(id))
            {
                continue;
            }

            var label = grammar.Info.Aliases.Count > 0 && grammar.Info.Aliases[0].Length > 0 ? grammar.Info.Aliases[0] : id;
            options.Add(new LanguageOption(id, label, grammar.ScopeName));
        }

        options.Sort(static (a, b) =>
        {
            var r = string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            return r != 0 ? r : string.CompareOrdinal(a.Id, b.Id);
        });
        return options;
    }
}
