using System.Collections.Frozen;
using System.Globalization;
using Tint.Grammars;

namespace Tint.Loading;

/// <summary>Reads TextMate grammars from JSON (<c>.tmLanguage.json</c>) and plist (<c>.tmLanguage</c>) files.</summary>
/// <remarks>
/// JSON is read as standard JSON with comments and trailing commas allowed. JavaScript-style relaxations that some
/// readers accept, such as Newtonsoft.Json's unquoted or single-quoted property names and content after the root
/// value, are rejected with a <see cref="System.Text.Json.JsonException"/>; a host that validates grammars with such
/// a reader can accept files that fail here.
/// </remarks>
public static partial class GrammarLoader
{
    /// <summary>Loads a grammar file: JSON when the name ends with <c>.json</c>, plist otherwise, as vscode-textmate decides.</summary>
    /// <param name="path">The grammar file.</param>
    /// <param name="info">Metadata for the grammar, or null for none.</param>
    public static LoadedGrammar Load(string path, GrammarInfo? info = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        var format = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? DocumentFormat.Json : DocumentFormat.Plist;
        return Load(File.ReadAllBytes(path), format, info);
    }

    /// <summary>Loads a grammar from a stream.</summary>
    /// <param name="stream">The grammar content.</param>
    /// <param name="format">The format of the content.</param>
    /// <param name="info">Metadata for the grammar, or null for none.</param>
    public static LoadedGrammar Load(Stream stream, DocumentFormat format = DocumentFormat.Auto, GrammarInfo? info = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Load(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), format, info);
    }

    /// <summary>Loads a grammar from UTF-8 content.</summary>
    /// <param name="utf8">The grammar content.</param>
    /// <param name="format">The format of the content.</param>
    /// <param name="info">Metadata for the grammar, or null for none.</param>
    public static LoadedGrammar Load(ReadOnlySpan<byte> utf8, DocumentFormat format = DocumentFormat.Auto, GrammarInfo? info = null)
        => Load(utf8.ToArray().AsMemory(), format, info);

    private static LoadedGrammar Load(ReadOnlyMemory<byte> utf8, DocumentFormat format, GrammarInfo? info)
    {
        if (format == DocumentFormat.Auto)
        {
            format = DocumentTree.LooksLikeJson(utf8.Span) ? DocumentFormat.Json : DocumentFormat.Plist;
        }

        var tree = format == DocumentFormat.Json ? DocumentTree.ParseJson(utf8) : DocumentTree.ParsePlist(utf8);
        if (tree is not TreeMap root)
        {
            throw new FormatException("A grammar must be an object.");
        }

        var grammar = ReadGrammar(root);
        return new LoadedGrammar(grammar, WithDependencies(info ?? GrammarInfo.Empty, grammar));
    }

    private static GrammarInfo WithDependencies(GrammarInfo info, RawGrammar grammar)
    {
        if (info.Dependencies.Count > 0)
        {
            return info;
        }

        var dependencies = CollectDependencies(grammar);
        if (dependencies.Count == 0)
        {
            return info;
        }

        return new GrammarInfo
        {
            LanguageId = info.LanguageId,
            Aliases = info.Aliases,
            FileExtensions = info.FileExtensions,
            EmbeddedLanguages = info.EmbeddedLanguages,
            InjectTo = info.InjectTo,
            InjectionSelector = info.InjectionSelector,
            TokenTypes = info.TokenTypes,
            BalancedBracketScopes = info.BalancedBracketScopes,
            UnbalancedBracketScopes = info.UnbalancedBracketScopes,
            Dependencies = dependencies,
        };
    }
}
