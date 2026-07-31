using System.Text;

namespace GodotAdvancedLogger.addons.godot_advanced_logger.core;

/// <summary>
/// Helpers for safely embedding arbitrary text into Godot BBCode output
/// (<c>GD.PrintRich</c> and <c>RichTextLabel</c> with <c>BbcodeEnabled</c>).
/// </summary>
/// <remarks>
/// Log messages routinely contain square brackets - array dumps, node paths,
/// serialized data, or a literal "[Warning]" typed by a user. Without escaping,
/// the BBCode parser treats those as tags and silently swallows them, which can
/// corrupt the formatting of everything that follows on the same line.
/// </remarks>
public static class Bbcode
{
    /// <summary>
    /// Escapes the BBCode opening bracket so <paramref name="text"/> renders literally.
    /// </summary>
    /// <param name="text">Untrusted text to render inside a BBCode context.</param>
    /// <returns>The escaped text, or the original instance when nothing needed escaping.</returns>
    public static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        
        return text.IndexOf('[') < 0 ? text : text.Replace("[", "[lb]");
    }

    /// <summary>
    /// Appends <paramref name="text"/> to <paramref name="sb"/>, escaping BBCode brackets.
    /// </summary>
    public static StringBuilder AppendEscaped(this StringBuilder sb, string text)
        => sb.Append(Escape(text));
}
