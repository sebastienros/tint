using System.Runtime.CompilerServices;

namespace Tint.Internal;

// How the per-line path asks the JIT to compile it.
internal static class Jit
{
    // The methods of the per-line path are compiled fully optimized on their first call instead of starting in tier 0
    // (docs/design.md section 6.2): tier-0 code runs them several times slower, since nothing is inlined, and a host
    // that keeps compiling new methods, as an editor does, postpones their promotion indefinitely because the runtime
    // restarts its call-counting delay at every new tier-0 method. Callees small enough to inline need no attribute:
    // inlining is decided by the caller's compilation.
#if NETCOREAPP3_0_OR_GREATER
    public const MethodImplOptions FullOpts = MethodImplOptions.AggressiveOptimization;
#else
    public const MethodImplOptions FullOpts = 0;
#endif
}
