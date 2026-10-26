namespace Tint;

/// <summary>Font style flags of a token.</summary>
[Flags]
#pragma warning disable CA1008 // NotSet is vscode-textmate's sentinel.
public enum FontStyle
#pragma warning restore CA1008
{
    /// <summary>No rule set a font style.</summary>
    NotSet = -1,

    /// <summary>Regular text.</summary>
    None = 0,

    /// <summary>Italic.</summary>
    Italic = 1,

    /// <summary>Bold.</summary>
    Bold = 2,

    /// <summary>Underlined.</summary>
    Underline = 4,

    /// <summary>Struck through.</summary>
    Strikethrough = 8,
}
