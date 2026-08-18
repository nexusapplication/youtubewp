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
                return (value / 1000000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "B";
            }

            if (value >= 1000000)
            {
                return (value / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            }

            if (value >= 1000)
            {
                return (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            }

            return value.ToString(CultureInfo.InvariantCulture);
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
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if ((c >= '0' && c <= '9') || c == '.' || c == ',')
                {
                    buffer += c;
                }
                else if (buffer.Length > 0)
                {
                    break;
                }
            }

            buffer = buffer.Replace(",", "");
            double value;
            if (!double.TryParse(buffer, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return 0;
            }

            var upper = text.ToUpperInvariant();
            if (upper.IndexOf('B') >= 0 && value < 1000000)
            {
                value *= 1000000000;
            }
            else if (upper.IndexOf('M') >= 0 && value < 1000000)
            {
                value *= 1000000;
            }
            else if (upper.IndexOf('K') >= 0 && value < 1000)
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
