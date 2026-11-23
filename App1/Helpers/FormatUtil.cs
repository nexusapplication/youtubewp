using System;
using System.Globalization;

namespace App1.Helpers
{
    internal static class FormatUtil
    {
        public static string Compact(long value)
        {
            if (value >= 1000000000)
            {
                return Trimmed(value / 1000000000.0) + "B";
            }

            if (value >= 1000000)
            {
                return Trimmed(value / 1000000.0) + "M";
            }

            if (value >= 1000)
            {
                return Trimmed(value / 1000.0) + "K";
            }

            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Trimmed(double scaled)
        {
            // Truncate instead of rounding up: 999,999 has to stay "999.9K"
            // instead of turning into "1000K".
            var truncated = Math.Floor(scaled * 10.0) / 10.0;
            return truncated.ToString("0.#", CultureInfo.InvariantCulture);
        }

        public static string Views(long value)
        {
            return Compact(value) + " views";
        }

        public static string Subscribers(long value)
        {
            return Compact(value) + " subscribers";
        }

        public static string Duration(int seconds)
        {
            if (seconds <= 0)
            {
                return "";
            }

            var span = TimeSpan.FromSeconds(seconds);
            if (span.TotalHours >= 1)
            {
                return span.ToString(@"h\:mm\:ss");
            }

            return span.ToString(@"m\:ss");
        }

        public static string Published(string publishedText, long unixSeconds)
        {
            if (!string.IsNullOrWhiteSpace(publishedText))
            {
                return publishedText;
            }

            if (unixSeconds <= 0)
            {
                return "";
            }

            var published = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(unixSeconds);
            var elapsed = DateTimeOffset.Now - published;
            if (elapsed.TotalMinutes < 1)
            {
                return "just now";
            }

            if (elapsed.TotalHours < 1)
            {
                var minutes = Math.Max(1, (int)elapsed.TotalMinutes);
                return minutes + " minute" + (minutes == 1 ? "" : "s") + " ago";
            }

            if (elapsed.TotalDays < 1)
            {
                var hours = Math.Max(1, (int)elapsed.TotalHours);
                return hours + " hour" + (hours == 1 ? "" : "s") + " ago";
            }

            if (elapsed.TotalDays < 30)
            {
                var days = Math.Max(1, (int)elapsed.TotalDays);
                return days + " day" + (days == 1 ? "" : "s") + " ago";
            }

            if (elapsed.TotalDays < 365)
            {
                var months = Math.Max(1, (int)(elapsed.TotalDays / 30));
                return months + " month" + (months == 1 ? "" : "s") + " ago";
            }

            var years = Math.Max(1, (int)(elapsed.TotalDays / 365));
            return years + " year" + (years == 1 ? "" : "s") + " ago";
        }

        public static int ParseDuration(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.IndexOf("LIVE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 0;
            }

            var cleaned = text.Trim();
            var space = cleaned.IndexOf(' ');
            if (space > 0)
            {
                cleaned = cleaned.Substring(0, space);
            }

            var parts = cleaned.Split(':');
            var total = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                int value;
                if (!int.TryParse(parts[i], out value))
                {
                    return 0;
                }

                total = (total * 60) + value;
            }

            return total;
        }

        public static long ParseCount(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            var buffer = "";
            var suffixIndex = -1;
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if ((c >= '0' && c <= '9') || c == '.' || c == ',')
                {
                    buffer += c;
                }
                else if ((c == ' ' || c == '\u00A0')
                    && buffer.Length > 0
                    && i + 1 < text.Length
                    && text[i + 1] >= '0' && text[i + 1] <= '9')
                {
                    // A space between digits groups thousands ("1 234 vues").
                    buffer += ' ';
                }
                else if (buffer.Length > 0)
                {
                    suffixIndex = i;
                    break;
                }
            }

            buffer = buffer.Replace(" ", "").Replace("\u00A0", "");

            // "1,234" / "1.234" group thousands, while "1,2K" / "1.2M" use the
            // separator as a decimal point. Keep the two apart so localized
            // strings don't come back 10x-1000x off.
            var normalized = buffer;
            if (System.Text.RegularExpressions.Regex.IsMatch(buffer, "^\\d{1,3}([.,]\\d{3})+$"))
            {
                normalized = normalized.Replace(",", "").Replace(".", "");
            }
            else if (System.Text.RegularExpressions.Regex.IsMatch(buffer, "^\\d+[.,]\\d+$"))
            {
                normalized = normalized.Replace(',', '.');
            }
            else
            {
                var lastComma = normalized.LastIndexOf(',');
                var lastDot = normalized.LastIndexOf('.');
                if (lastComma >= 0 && lastDot >= 0)
                {
                    // Mixed separators: the right-most one is the decimal mark.
                    if (lastComma > lastDot)
                    {
                        normalized = normalized.Replace(".", "").Replace(',', '.');
                    }
                    else
                    {
                        normalized = normalized.Replace(",", "");
                    }
                }
                else
                {
                    normalized = normalized.Replace(",", "").Replace(".", "");
                }
            }

            double value;
            if (!double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return 0;
            }

            // The multiplier only counts for a real unit marker: a lone K/M/B
            // ("1.2M views") or a localized word like "Mio" / "Million". A plain
            // word that merely starts with one of those letters ("123
            // Kommentare") must stay untouched.
            var suffix = '\0';
            if (suffixIndex > 0 && suffixIndex < text.Length)
            {
                var markerIndex = suffixIndex;
                while (markerIndex < text.Length
                    && (text[markerIndex] == ' ' || text[markerIndex] == '\u00A0' || text[markerIndex] == '.'))
                {
                    markerIndex++;
                }

                var wordStart = markerIndex;
                while (markerIndex < text.Length && char.IsLetter(text[markerIndex]))
                {
                    markerIndex++;
                }

                var word = text.Substring(wordStart, markerIndex - wordStart);
                if (word.Length == 1)
                {
                    var lone = char.ToUpperInvariant(word[0]);
                    if (lone == 'K' || lone == 'M' || lone == 'B')
                    {
                        suffix = lone;
                    }
                }
                else
                {
                    var lower = word.ToLowerInvariant();
                    if (lower.StartsWith("mio") || lower.StartsWith("mill") || lower == "mln")
                    {
                        suffix = 'M';
                    }
                }
            }

            if (suffix == 'B' && value < 1000000)
            {
                value *= 1000000000;
            }
            else if (suffix == 'M' && value < 1000000)
            {
                value *= 1000000;
            }
            else if (suffix == 'K' && value < 1000)
            {
                value *= 1000;
            }

            return (long)value;
        }

        public static string JoinMeta(params string[] parts)
        {
            string result = "";
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(parts[i]))
                {
                    continue;
                }

                if (result.Length > 0)
                {
                    result += " · ";
                }

                result += parts[i];
            }

            return result;
        }

        public static string FormatHandle(string handle, string authorFallback)
        {
            var value = handle ?? "";
            if (string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(authorFallback))
            {
                value = authorFallback.Replace(" ", "");
            }

            value = value.Trim();
            while (value.StartsWith("@", StringComparison.Ordinal))
            {
                value = value.Substring(1);
            }

            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            return "@" + value;
        }
    }
}
