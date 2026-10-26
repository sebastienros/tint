using System.Collections.Concurrent;
using System.Reflection;

namespace Tint.Generator;

internal enum KernelStatus
{
    Succeeded,
    Declined,
    Invalid,
}

internal sealed record KernelResult(KernelStatus Status, ulong Fingerprint, string? PlainClass, string? CaptureClass, int Offset, string? Message);

// Reaches GregsEx's KernelSource façade (GregsEx.Generator.dll, shipped by the GregsEx package under analyzers/dotnet/cs)
// without a compile-time reference, so kernels and validation always come from the GregsEx version the consumer runs.
// Bound by reflection to the public signature
//   KernelSourceResult KernelSource.TryEmit(string, OnigurumaOptions, string, string, OnigurumaCompileOptions?)
// Any reflection failure means "no GregsEx at build time", never an exception escaping to the compiler.
#pragma warning disable RS1035 // The shim's job is to find and load an assembly from disk.
internal sealed class GregsExShim
{
    private static readonly ConcurrentDictionary<string, Lazy<(GregsExShim? Shim, string? Failure)>> s_cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly MethodInfo _tryEmit;
    private readonly object _options;
    private readonly object? _compileOptions;
    private readonly PropertyInfo _status;
    private readonly PropertyInfo _fingerprint;
    private readonly PropertyInfo _plainClass;
    private readonly PropertyInfo _captureClass;
    private readonly PropertyInfo _diagnostic;

    private GregsExShim(MethodInfo tryEmit, object options, object? compileOptions, Type resultType)
    {
        _tryEmit = tryEmit;
        _options = options;
        _compileOptions = compileOptions;
        _status = resultType.GetProperty("Status") ?? throw new MissingMemberException("KernelSourceResult.Status");
        _fingerprint = resultType.GetProperty("Fingerprint") ?? throw new MissingMemberException("KernelSourceResult.Fingerprint");
        _plainClass = resultType.GetProperty("PlainClass") ?? throw new MissingMemberException("KernelSourceResult.PlainClass");
        _captureClass = resultType.GetProperty("CaptureClass") ?? throw new MissingMemberException("KernelSourceResult.CaptureClass");
        _diagnostic = resultType.GetProperty("Diagnostic") ?? throw new MissingMemberException("KernelSourceResult.Diagnostic");
    }

    // The GregsEx package root: the override, else derived from the GregsEx.dll reference (<package>/lib/<tfm>/GregsEx.dll).
    public static string? FindPackageRoot(string? overridePath, string? referencePath)
    {
        if (overridePath is not null)
        {
            return overridePath.Replace('\\', '/').TrimEnd('/');
        }

        if (referencePath is null)
        {
            return null;
        }

        var lib = Path.GetDirectoryName(Path.GetDirectoryName(referencePath));
        if (lib is null || !string.Equals(Path.GetFileName(lib), "lib", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetDirectoryName(lib)?.Replace('\\', '/');
    }

    public static string GeneratorPath(string packageRoot) => packageRoot + "/analyzers/dotnet/cs/GregsEx.Generator.dll";

    // One load per package path for the lifetime of the generator assembly.
    public static GregsExShim? Get(string? packageRoot, out string? failure)
    {
        if (packageRoot is null)
        {
            failure = "the compilation has no GregsEx.dll reference from a NuGet package layout; set TintGregsExPackagePath to the GregsEx package directory";
            return null;
        }

        var entry = s_cache.GetOrAdd(packageRoot, root => new Lazy<(GregsExShim?, string?)>(() => Load(root)));
        (var shim, failure) = entry.Value;

        // A package that is not there yet (an IDE build before restore) is looked for again next time.
        if (shim is null && !File.Exists(GeneratorPath(packageRoot)))
        {
            s_cache.TryRemove(packageRoot, out _);
        }

        return shim;
    }

    private static (GregsExShim?, string?) Load(string packageRoot)
    {
        try
        {
            var path = GeneratorPath(packageRoot);
            var assembly = FindLoaded(path);
            if (assembly is null)
            {
                if (!File.Exists(path))
                {
                    return (null, $"'{path}' does not exist");
                }

                assembly = Assembly.LoadFile(Path.GetFullPath(path));
            }

            var kernelSource = assembly.GetType("GregsEx.SourceGeneration.KernelSource");
            var optionsType = assembly.GetType("GregsEx.OnigurumaOptions");
            var compileOptionsType = assembly.GetType("GregsEx.OnigurumaCompileOptions");
            if (kernelSource is null || optionsType is null || compileOptionsType is null)
            {
                return (null, $"'{path}' has no KernelSource façade (GregsEx 0.5.1 or later is required)");
            }

            var tryEmit = kernelSource.GetMethod(
                "TryEmit",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                [typeof(string), optionsType, typeof(string), typeof(string), compileOptionsType],
                modifiers: null);
            if (tryEmit is null)
            {
                return (null, $"'{path}' has no KernelSource.TryEmit with the expected signature");
            }

            var options = Enum.Parse(optionsType, "CaptureGroup");
            var compileOptions = compileOptionsType.GetProperty("TextMate", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (compileOptions is null)
            {
                return (null, $"'{path}' has no OnigurumaCompileOptions.TextMate");
            }

            return (new GregsExShim(tryEmit, options, compileOptions, tryEmit.ReturnType), null);
        }
        catch (Exception e)
        {
            return (null, $"GregsEx.Generator.dll could not be loaded ({e.GetType().Name}: {e.Message})");
        }
    }

    // Prefer the copy the compiler already loaded (GregsEx's own generator, from the same package).
    private static Assembly? FindLoaded(string path)
    {
        AssemblyName? fileName = null;
        Assembly? sameVersion = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            AssemblyName name;
            try
            {
                name = assembly.GetName();
                if (name.Name != "GregsEx.Generator")
                {
                    continue;
                }

                if (!assembly.IsDynamic && PathUtilities.PathEquals(assembly.Location.Replace('\\', '/'), path))
                {
                    return assembly;
                }

                fileName ??= File.Exists(path) ? AssemblyName.GetAssemblyName(path) : null;
                if (fileName is not null && name.Version == fileName.Version && sameVersion is null)
                {
                    sameVersion = assembly;
                }
            }
            catch (Exception e) when (e is IOException or BadImageFormatException or NotSupportedException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }

        return sameVersion;
    }

    // Null when the call itself failed.
    public KernelResult? TryEmit(string pattern, string plainClassName, string captureClassName)
    {
        try
        {
            var result = _tryEmit.Invoke(null, [pattern, _options, plainClassName, captureClassName, _compileOptions]);
            if (result is null)
            {
                return null;
            }

            var status = (KernelStatus)Enum.Parse(typeof(KernelStatus), _status.GetValue(result)!.ToString());
            var offset = 0;
            string? message = null;
            if (status == KernelStatus.Invalid && _diagnostic.GetValue(result) is { } diagnostic)
            {
                var type = diagnostic.GetType();
                offset = type.GetProperty("Offset")?.GetValue(diagnostic) is int o ? o : 0;
                message = type.GetProperty("Message")?.GetValue(diagnostic) as string;
            }

            return new KernelResult(
                status,
                _fingerprint.GetValue(result) is ulong fingerprint ? fingerprint : 0,
                _plainClass.GetValue(result) as string,
                _captureClass.GetValue(result) as string,
                offset,
                message);
        }
        catch (Exception e) when (e is TargetInvocationException or ArgumentException or InvalidCastException or MemberAccessException or NullReferenceException or OverflowException)
        {
            return null;
        }
    }
}
#pragma warning restore RS1035
