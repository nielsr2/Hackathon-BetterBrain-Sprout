using System;
using System.Collections.Generic;
using System.Text;

namespace Sequence
{
    /// <summary>One ERP button of the final choice: its label and the video it plays (path as written in the CSV).</summary>
    public readonly struct ChoiceOption
    {
        public readonly string Label;
        public readonly string Video;

        public ChoiceOption(string label, string video)
        {
            Label = label;
            Video = video;
        }
    }

    /// <summary>
    /// Parses the choice CSV: a header row with <c>button</c> and <c>video</c> columns (any order, any case,
    /// other columns ignored), then one row per button. Blank lines and lines starting with <c>#</c> are
    /// skipped; fields may be quoted (<c>"GO, NOW"</c>, <c>""</c> for a quote).
    /// </summary>
    public static class ChoiceCsv
    {
        /// <summary>
        /// The first <paramref name="needed"/> options. Throws <see cref="FormatException"/> on a missing column,
        /// an empty cell or too few rows; extra rows are ignored and reported in <paramref name="warnings"/>.
        /// </summary>
        public static List<ChoiceOption> Parse(string text, int needed, out List<string> warnings)
        {
            warnings = new List<string>();
            var rows = new List<(int line, List<string> cells)>();
            var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                rows.Add((i + 1, SplitRow(lines[i], i + 1)));
            }
            if (rows.Count == 0) throw new FormatException("The choice CSV is empty.");

            var header = rows[0].cells;
            int buttonCol = header.FindIndex(h => h.Equals("button", StringComparison.OrdinalIgnoreCase));
            int videoCol = header.FindIndex(h => h.Equals("video", StringComparison.OrdinalIgnoreCase));
            if (buttonCol < 0 || videoCol < 0)
                throw new FormatException($"Line {rows[0].line}: the header needs 'button' and 'video' columns (found: {string.Join(", ", header)}).");

            var options = new List<ChoiceOption>();
            for (int r = 1; r < rows.Count; r++)
            {
                var (line, cells) = rows[r];
                string label = Cell(cells, buttonCol), video = Cell(cells, videoCol);
                if (label.Length == 0) throw new FormatException($"Line {line}: the button label is empty.");
                if (video.Length == 0) throw new FormatException($"Line {line}: the video for '{label}' is empty.");
                options.Add(new ChoiceOption(label, video));
            }

            if (options.Count < needed)
                throw new FormatException($"The choice CSV has {options.Count} button row(s); {needed} are needed.");
            if (options.Count > needed)
            {
                warnings.Add($"The choice CSV has {options.Count} button rows; only the first {needed} are used.");
                options.RemoveRange(needed, options.Count - needed);
            }
            return options;
        }

        static string Cell(List<string> cells, int i) => i < cells.Count ? cells[i] : "";

        static List<string> SplitRow(string line, int lineNumber)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c != '"') cell.Append(c);
                    else if (i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(cell.ToString().Trim()); cell.Clear(); }
                else cell.Append(c);
            }
            if (quoted) throw new FormatException($"Line {lineNumber}: unclosed quote.");
            cells.Add(cell.ToString().Trim());
            return cells;
        }
    }
}
