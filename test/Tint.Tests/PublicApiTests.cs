using System.Reflection;
using System.Text;
using Tint.Grammars;
using Tint.Themes;
using Xunit;

namespace Tint.Tests;

// The public classes of Tint.Grammars and Tint.Themes are written by Tint.Generator, which the public API analyzer does
// not see (source-generated code is always generated code to analyzers). This test keeps their surface in
// src/<bundle>/PublicAPI.Generated.txt so that changes show in diffs the way PublicAPI.Unshipped.txt does for
// hand-written code. Run with TINT_UPDATE_PUBLIC_API=1 to rewrite the files.
public class PublicApiTests
{
    public static TheoryData<string> Bundles => ["Tint.Grammars", "Tint.Themes"];

    [Theory]
    [MemberData(nameof(Bundles))]
    public void TheGeneratedSurfaceOfABundleMatchesItsApiFile(string bundle)
    {
        var assembly = bundle == "Tint.Grammars" ? typeof(TintGrammars).Assembly : typeof(TintThemes).Assembly;
        var actual = Describe(assembly);
        var path = Path.Combine(Fixtures.RepositoryRoot, "src", bundle, "PublicAPI.Generated.txt");
        if (Environment.GetEnvironmentVariable("TINT_UPDATE_PUBLIC_API") == "1")
        {
            File.WriteAllText(path, actual);
        }

        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual);
    }

    private static string Describe(Assembly assembly)
    {
        var lines = new List<string>();
        foreach (var type in assembly.GetExportedTypes())
        {
            var bases = new List<string>();
            if (type.BaseType is { } baseType && baseType != typeof(object))
            {
                bases.Add(Name(baseType));
            }

            bases.AddRange(type.GetInterfaces().Select(Name).Order(StringComparer.Ordinal));
            var kind = type.IsInterface ? "interface" : type.IsAbstract && type.IsSealed ? "static class" : type.IsSealed ? "sealed class" : "class";
            lines.Add($"{kind} {Name(type)}{(bases.Count > 0 ? " : " + string.Join(", ", bases) : "")}");

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var constructor in type.GetConstructors(flags))
            {
                lines.Add($"{Name(type)}.{type.Name}({Parameters(constructor)})");
            }

            foreach (var property in type.GetProperties(flags))
            {
                var isStatic = property.GetMethod?.IsStatic == true ? "static " : "";
                lines.Add($"{isStatic}{Name(type)}.{property.Name}.get -> {Name(property.PropertyType)}");
                if (property.SetMethod?.IsPublic == true)
                {
                    lines.Add($"{isStatic}{Name(type)}.{property.Name}.set");
                }
            }

            foreach (var method in type.GetMethods(flags).Where(m => !m.IsSpecialName))
            {
                lines.Add($"{(method.IsStatic ? "static " : "")}{Name(type)}.{method.Name}({Parameters(method)}) -> {Name(method.ReturnType)}");
            }

            foreach (var field in type.GetFields(flags))
            {
                lines.Add($"{(field.IsLiteral ? "const " : field.IsStatic ? "static " : "")}{Name(type)}.{field.Name} -> {Name(field.FieldType)}");
            }
        }

        lines.Sort(StringComparer.Ordinal);
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    private static string Parameters(MethodBase method) => string.Join(", ", method.GetParameters().Select(p => $"{Name(p.ParameterType)} {p.Name}"));

    private static string Name(Type type)
    {
        if (type.IsGenericType)
        {
            var name = type.GetGenericTypeDefinition().FullName!;
            return $"{name[..name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Name))}>";
        }

        return type.FullName ?? type.Name;
    }
}
