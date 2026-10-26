namespace Tint.Grammars;

/// <summary>The coarse token classification VS Code keeps in the encoded token attributes.</summary>
public enum StandardTokenType
{
    /// <summary>Code.</summary>
    Other = 0,

    /// <summary>A comment.</summary>
    Comment = 1,

    /// <summary>A string literal.</summary>
    String = 2,

    /// <summary>A regular expression literal.</summary>
    RegEx = 3,
}
