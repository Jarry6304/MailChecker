using System.Net;
using System.Text.RegularExpressions;

namespace MailChecker.Services;

public static partial class HtmlToText
{
    [GeneratedRegex("<script[^>]*>.*?</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptBlock();

    [GeneratedRegex("<style[^>]*>.*?</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex StyleBlock();

    [GeneratedRegex("<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BrTag();

    [GeneratedRegex("</p\\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex ClosingParagraph();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex("[ \\t]+")]
    private static partial Regex InlineWhitespace();

    [GeneratedRegex("\\n{3,}")]
    private static partial Regex ExcessiveNewlines();

    public static string Convert(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = ScriptBlock().Replace(html, string.Empty);
        text = StyleBlock().Replace(text, string.Empty);
        text = BrTag().Replace(text, "\n");
        text = ClosingParagraph().Replace(text, "\n\n");
        text = AnyTag().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = InlineWhitespace().Replace(text, " ");
        text = ExcessiveNewlines().Replace(text, "\n\n");
        return text.Trim();
    }
}
