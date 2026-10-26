namespace Tint.Loading;

/// <summary>The file formats of grammars and themes.</summary>
public enum DocumentFormat
{
    /// <summary>JSON when the content starts with <c>{</c>, <c>[</c> or a comment, plist otherwise.</summary>
    Auto,

    /// <summary>JSON, with comments and trailing commas allowed.</summary>
    Json,

    /// <summary>An XML property list (<c>.tmLanguage</c>, <c>.plist</c>, <c>.tmTheme</c>).</summary>
    Plist,
}
