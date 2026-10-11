using System.Text;

namespace XtremScan.Scanning;

internal static class WinAnsi
{
    public static string Escape(string text)
    {
        var builder = new StringBuilder();
        foreach (var character in text)
        {
            if (character > 255)
            {
                continue;
            }

            var value = (int)character;
            if (value is '(' or ')' or '\\')
            {
                builder.Append('\\');
                builder.Append(character);
                continue;
            }

            if (value < 32 || value > 126)
            {
                builder.Append('\\');
                builder.Append(value.ToString("D3"));
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
