using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;

namespace iceman_gui.Core;

public static class AnsiColorParser
{
    private static readonly Regex AnsiRegex = new Regex(@"\x1B\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled);
    private static readonly Regex AnsiSgrRegex = new Regex(@"\x1B\[([0-9;]*)m", RegexOptions.Compiled);

    public static string StripAnsi(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return AnsiRegex.Replace(input, string.Empty);
    }

    public static List<Run> ParseToRuns(string text)
    {
        var runs = new List<Run>();
        if (string.IsNullOrEmpty(text)) return runs;

        var matches = AnsiSgrRegex.Matches(text);
        int lastIndex = 0;
        Brush currentBrush = Brushes.LightGray;

        foreach (Match match in matches)
        {
            if (match.Index > lastIndex)
            {
                string chunk = text.Substring(lastIndex, match.Index - lastIndex);
                runs.Add(new Run(chunk) { Foreground = currentBrush });
            }

            currentBrush = GetBrushFromCodes(match.Groups[1].Value, currentBrush);
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            string remaining = text.Substring(lastIndex);
            runs.Add(new Run(remaining) { Foreground = currentBrush });
        }

        return runs;
    }

    private static Brush GetBrushFromCodes(string codes, Brush fallback)
    {
        if (string.IsNullOrEmpty(codes) || codes == "0")
        {
            return Brushes.LightGray;
        }

        var parts = codes.Split(';');
        foreach (var p in parts)
        {
            switch (p)
            {
                case "30": return Brushes.DarkSlateGray;
                case "31": return Brushes.Crimson;        // Red [-]
                case "32": return Brushes.LimeGreen;      // Green [+]
                case "33": return Brushes.Goldenrod;      // Yellow [!]
                case "34": return Brushes.DodgerBlue;     // Blue
                case "35": return Brushes.Magenta;
                case "36": return Brushes.Cyan;           // Cyan [?]
                case "37": return Brushes.White;
                case "90": return Brushes.Gray;           // Bright Black
                case "91": return Brushes.LightCoral;
                case "92": return Brushes.LightGreen;
                case "93": return Brushes.Yellow;
                case "94": return Brushes.DeepSkyBlue;
                case "95": return Brushes.Plum;
                case "96": return Brushes.LightCyan;
                case "97": return Brushes.White;
                case "0": return Brushes.LightGray;
            }
        }

        return fallback;
    }
}
