using System.Text;

namespace ShutdownGuard.Services;

internal static class Format
{
    /// <summary>
    /// Formats a duration like "1h 30m 5s" — only includes non-zero parts.
    /// Used in 1 Hz UI updates, so avoids List/Linq allocations.
    /// </summary>
    public static string Duration(TimeSpan ts)
    {
        if (ts.TotalSeconds < 1) return "0s";

        int h = ts.Hours, m = ts.Minutes, s = ts.Seconds;

        if (h == 0 && m == 0) return s + "s";
        if (h == 0 && s == 0) return m + "m";
        if (m == 0 && s == 0) return h + "h";

        var sb = new StringBuilder(16);
        if (h > 0) sb.Append(h).Append('h');
        if (m > 0) { if (sb.Length > 0) sb.Append(' '); sb.Append(m).Append('m'); }
        if (s > 0) { if (sb.Length > 0) sb.Append(' '); sb.Append(s).Append('s'); }
        return sb.ToString();
    }
}
