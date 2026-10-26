using System.Collections.Concurrent;
using Tint.Grammars;

namespace Tint.Internal.Rules;

internal readonly record struct BasicScopeAttributes(int LanguageId, StandardTokenType? TokenType);

// basicScopesAttributeProvider.ts: the embedded language and standard token type of a scope name, cached.
internal sealed class BasicScopeAttributesProvider
{
    private readonly KeyValuePair<string, int>[] _embeddedLanguages;
    private readonly ConcurrentDictionary<string, BasicScopeAttributes> _cache = new(StringComparer.Ordinal);

    public BasicScopeAttributesProvider(int initialLanguageId, IEnumerable<KeyValuePair<string, int>> embeddedLanguages)
    {
        DefaultAttributes = new BasicScopeAttributes(initialLanguageId, null);

        // The regex ^((s1)|(s2)...)($|\.) over the scopes sorted then reversed: the first scope, in descending
        // ordinal order, equal to the scope name or a dot-prefix of it.
        _embeddedLanguages = [.. embeddedLanguages];
        Array.Sort(_embeddedLanguages, static (a, b) => string.CompareOrdinal(b.Key, a.Key));
    }

    public BasicScopeAttributes DefaultAttributes { get; }

    public BasicScopeAttributes GetBasicScopeAttributes(string scopeName)
        => _cache.GetOrAdd(scopeName, static (name, self) => new BasicScopeAttributes(self.ScopeToLanguage(name), ToStandardTokenType(name)), this);

    private int ScopeToLanguage(string scope)
    {
        foreach (var entry in _embeddedLanguages)
        {
            var prefix = entry.Key;
            if (scope.StartsWith(prefix, StringComparison.Ordinal) && (scope.Length == prefix.Length || scope[prefix.Length] == '.'))
            {
                return entry.Value;
            }
        }

        return 0;
    }

    // /\b(comment|string|regex|meta\.embedded)\b/: the leftmost match, alternatives in order at one position.
    internal static StandardTokenType? ToStandardTokenType(string scopeName)
    {
        for (var i = 0; i < scopeName.Length; i++)
        {
            if (i > 0 && IsWord(scopeName[i - 1]))
            {
                continue;
            }

            if (Matches(scopeName, i, "comment"))
            {
                return StandardTokenType.Comment;
            }

            if (Matches(scopeName, i, "string"))
            {
                return StandardTokenType.String;
            }

            if (Matches(scopeName, i, "regex"))
            {
                return StandardTokenType.RegEx;
            }

            if (Matches(scopeName, i, "meta.embedded"))
            {
                return StandardTokenType.Other;
            }
        }

        return null;
    }

    private static bool Matches(string text, int index, string word)
    {
        var end = index + word.Length;
        return end <= text.Length && string.CompareOrdinal(text, index, word, 0, word.Length) == 0 && (end == text.Length || !IsWord(text[end]));
    }

    private static bool IsWord(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
}
