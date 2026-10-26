using System.Text;

namespace Tint.Differential;

/// <summary>Splits documents into lines.</summary>
public static class Lines
{
    /// <summary>The lines of a UTF-8 file without their terminators (<c>\n</c>, <c>\r\n</c> or <c>\r</c>), as an editor splits them.</summary>
    public static string[] Read(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '\n' or '\r')
            {
                lines.Add(text[start..i]);
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return [.. lines];
    }
}
