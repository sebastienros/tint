using System.Runtime.CompilerServices;

namespace Tint.Tests;

internal static class Fixtures
{
    public static string VSCodeTextMate { get; } = Path.Combine(SourceDirectory(), "Fixtures", "vscode-textmate");

    private static string SourceDirectory([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;
}
