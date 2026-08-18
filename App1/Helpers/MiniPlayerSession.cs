using App1.Models;
using System;

namespace App1.Helpers
{
    internal static class MiniPlayerSession
    {
        public static bool IsActive { get; private set; }
        public static bool PendingShow { get; private set; }
        public static string VideoId { get; private set; }
        public static string Title { get; private set; }
        public static string Author { get; private set; }
        public static string Thumbnail { get; private set; }
        public static Uri Source { get; private set; }
        public static double PositionSeconds { get; private set; }
        public static bool ShouldPlay { get; private set; }
        public static VideoDetails Details { get; private set; }

        public static void Stash(
            string videoId,
            string title,
            string author,
            string thumbnail,
            Uri source,
            double positionSeconds,
            bool shouldPlay,
            VideoDetails details)
        {
            VideoId = videoId ?? "";
            Title = title ?? "";
            Author = author ?? "";
            Thumbnail = thumbnail ?? "";
            Source = source;
            PositionSeconds = positionSeconds;
            ShouldPlay = shouldPlay;
            Details = details;
            IsActive = true;
            PendingShow = true;
        }

        public static void UpdatePlayback(Uri source, double positionSeconds, bool shouldPlay)
        {
            if (!string.IsNullOrEmpty(source?.ToString()))
            {
                Source = source;
            }

            PositionSeconds = positionSeconds;
            ShouldPlay = shouldPlay;
        }

        public static void MarkShown()
        {
            PendingShow = false;
        }

        public static void Clear()
        {
            IsActive = false;
            PendingShow = false;
            VideoId = "";
            Title = "";
            Author = "";
            Thumbnail = "";
            Source = null;
            PositionSeconds = 0;
            ShouldPlay = false;
            Details = null;
        }
    }
}
