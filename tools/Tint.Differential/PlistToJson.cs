using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Tint.Differential;

/// <summary>Converts an XML property list to JSON, for TextMateSharp, which reads JSON grammars only.</summary>
public static class PlistToJson
{
    /// <summary>The JSON text of the plist's root value.</summary>
    public static string Convert(string plist)
    {
        var document = XDocument.Parse(plist, LoadOptions.None);
        var root = document.Root!.Elements().First();
        return Value(root)!.ToJsonString();
    }

    private static JsonNode? Value(XElement element) => element.Name.LocalName switch
    {
        "dict" => Dict(element),
        "array" => new JsonArray([.. element.Elements().Select(Value)]),
        "string" => JsonValue.Create(element.Value),
        "integer" => JsonValue.Create(long.Parse(element.Value, System.Globalization.CultureInfo.InvariantCulture)),
        "real" => JsonValue.Create(double.Parse(element.Value, System.Globalization.CultureInfo.InvariantCulture)),
        "true" => JsonValue.Create(true),
        "false" => JsonValue.Create(false),
        _ => JsonValue.Create(element.Value),
    };

    private static JsonObject Dict(XElement element)
    {
        var result = new JsonObject();
        string? key = null;
        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "key")
            {
                key = child.Value;
            }
            else if (key is not null)
            {
                result[key] = Value(child);
                key = null;
            }
        }

        return result;
    }
}
