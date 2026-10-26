using Microsoft.CodeAnalysis;

namespace Tint.Generator;

/// <summary>
/// Compiles <c>TintGrammar</c> and <c>TintTheme</c> items into <c>IGrammarSource</c> and <c>IThemeSource</c> classes.
/// Phase P0 stub: it registers no outputs yet.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class TintGrammarGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
    }
}
