namespace Tint;

/// <summary>A grammar cannot be used as written, for example a regex that does not parse reached by a search.</summary>
public sealed class TintGrammarException : Exception
{
    /// <summary>Creates an exception with a message.</summary>
    public TintGrammarException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and its cause.</summary>
    public TintGrammarException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
