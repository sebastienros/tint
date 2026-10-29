using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using GregsEx;

namespace Tint.Internal.Regex;

// Tier 2 of docs/design.md section 5.5: GregsEx's OnigurumaPattern.Compile() on JIT runtimes. Never reached under
// NativeAOT (IsDynamicCodeSupported is a constant false there) or in trimmed apps (GregsEx's build targets set its
// GregsEx.RuntimeCompilation.Auto switch to false when PublishTrimmed or PublishAot is on, and the trimmer folds
// IsSupported to that value).
internal static class RuntimeCompilation
{
    private const string SwitchName = "GregsEx.RuntimeCompilation.Auto";

#if NET9_0_OR_GREATER
    [FeatureSwitchDefinition(SwitchName)]
#endif
    public static bool IsSupported
        =>
#if NETSTANDARD2_0
            false;
#else
            RuntimeFeature.IsDynamicCodeSupported
            && RuntimeFeature.IsDynamicCodeCompiled
            && (!AppContext.TryGetSwitch(SwitchName, out var enabled) || enabled);
#endif

    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Guarded by IsSupported, which requires RuntimeFeature.IsDynamicCodeSupported.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Guarded by IsSupported, which the trimmer folds to false through GregsEx's feature switch.")]
    public static OnigurumaPattern? TryCompile(OnigurumaPattern pattern)
    {
        if (!IsSupported)
        {
            return null;
        }

        try
        {
            return pattern.Compile();
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            // A construct the lowering does not model, or a pattern already served by a faster path.
            return null;
        }
    }
}
