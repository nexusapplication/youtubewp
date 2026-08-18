using App1.Models;
using System;
using System.Collections.Generic;

namespace App1.Helpers
{
    internal sealed class VideoSnapshot
    {
        public VideoDetails Details;
        public List<VideoItem> RelatedVideos;
        public List<FeedItem> MixedRelated;
        public int RelatedIndex;
        public DateTime CachedAt;
    }

    internal sealed class ChannelSnapshot
    {
        public ChannelItem Channel;
        public List<VideoItem> Videos;
        public List<VideoItem> Shorts;
        public DateTime CachedAt;
    }

    internal static class AppContentCache
    {
        private const int VideoTtlMinutes = 60;
        private const int ChannelTtlMinutes = 30;

        private static readonly Dictionary<string, VideoSnapshot> Videos =
            new Dictionary<string, VideoSnapshot>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ChannelSnapshot> Channels =
            new Dictionary<string, ChannelSnapshot>(StringComparer.Ordinal);

        public static VideoSnapshot GetVideo(string videoId)
        {
            if (string.IsNullOrEmpty(videoId))
            {
                return null;
            }

            VideoSnapshot snapshot;
            if (!Videos.TryGetValue(videoId, out snapshot) || snapshot == null || snapshot.Details == null)
            {
                return null;
            }

            if ((DateTime.Now - snapshot.CachedAt).TotalMinutes > VideoTtlMinutes)
            {
                Videos.Remove(videoId);
                return null;
            }

            return snapshot;
        }

        public static void PutVideo(string videoId, VideoSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(videoId) || snapshot == null || snapshot.Details == null)
            {
                return;
            }

            snapshot.CachedAt = DateTime.Now;
            Videos[videoId] = snapshot;
            TrimVideos();
        }

        public static ChannelSnapshot GetChannel(string channelId)
        {
            if (string.IsNullOrEmpty(channelId))
            {
                return null;
            }

            ChannelSnapshot snapshot;
            if (!Channels.TryGetValue(channelId, out snapshot) || snapshot == null || snapshot.Channel == null)
            {
                return null;
            }

            if ((DateTime.Now - snapshot.CachedAt).TotalMinutes > ChannelTtlMinutes)
            {
                Channels.Remove(channelId);
                return null;
            }

            return snapshot;
        }

        public static void PutChannel(string channelId, ChannelSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(channelId) || snapshot == null || snapshot.Channel == null)
            {
                return;
            }

            snapshot.CachedAt = DateTime.Now;
            Channels[channelId] = snapshot;
            TrimChannels();
        }

        public static void Clear()
        {
            Videos.Clear();
            Channels.Clear();
        }

        private static void TrimVideos()
        {
            if (Videos.Count <= 12)
            {
                return;
            }

            var oldestKey = "";
            var oldest = DateTime.MaxValue;
            foreach (var pair in Videos)
            {
                if (pair.Value.CachedAt < oldest)
                {
                    oldest = pair.Value.CachedAt;
                    oldestKey = pair.Key;
                }
            }

            if (!string.IsNullOrEmpty(oldestKey))
            {
                Videos.Remove(oldestKey);
            }
        }

        private static void TrimChannels()
        {
            if (Channels.Count <= 8)
            {
                return;
            }

            var oldestKey = "";
            var oldest = DateTime.MaxValue;
            foreach (var pair in Channels)
            {
                if (pair.Value.CachedAt < oldest)
                {
                    oldest = pair.Value.CachedAt;
                    oldestKey = pair.Key;
                }
            }

            if (!string.IsNullOrEmpty(oldestKey))
            {
                Channels.Remove(oldestKey);
            }
        }
    }
}
