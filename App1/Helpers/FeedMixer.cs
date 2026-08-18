using App1.Models;
using System;
using System.Collections.Generic;

namespace App1.Helpers
{
    internal static class FeedMixer
    {
        private static readonly Random Rng = new Random();

        public static List<FeedItem> ScatterExtras(
            List<FeedItem> videos,
            List<VideoItem> shorts,
            List<CommunityPostItem> posts,
            int shortsShelfSize)
        {
            var result = new List<FeedItem>();
            if (videos == null)
            {
                videos = new List<FeedItem>();
            }

            var shortPool = shorts != null ? new List<VideoItem>(shorts) : new List<VideoItem>();
            var postPool = posts != null ? new List<CommunityPostItem>(posts) : new List<CommunityPostItem>();
            var videoIndex = 0;
            var stepsUntilShorts = NextScatterGap();
            var stepsUntilPost = NextScatterGap();

            while (videoIndex < videos.Count || shortPool.Count > 0 || postPool.Count > 0)
            {
                if (stepsUntilShorts <= 0 && shortPool.Count > 0)
                {
                    var shelfCount = Math.Min(shortsShelfSize > 0 ? shortsShelfSize : 8, shortPool.Count);
                    var shelf = new List<VideoItem>();
                    for (int i = 0; i < shelfCount; i++)
                    {
                        shelf.Add(shortPool[0]);
                        shortPool.RemoveAt(0);
                    }

                    result.Add(FeedItem.ShortsShelf(shelf));
                    stepsUntilShorts = NextScatterGap();
                    stepsUntilPost = Math.Max(1, stepsUntilPost - 1);
                    continue;
                }

                if (stepsUntilPost <= 0 && postPool.Count > 0)
                {
                    result.Add(FeedItem.FromCommunity(postPool[0]));
                    postPool.RemoveAt(0);
                    stepsUntilPost = NextScatterGap();
                    stepsUntilShorts = Math.Max(1, stepsUntilShorts - 1);
                    continue;
                }

                if (videoIndex < videos.Count)
                {
                    result.Add(videos[videoIndex]);
                    videoIndex++;
                    stepsUntilShorts--;
                    stepsUntilPost--;
                    continue;
                }

                if (shortPool.Count > 0)
                {
                    var shelfCount = Math.Min(shortsShelfSize > 0 ? shortsShelfSize : 8, shortPool.Count);
                    var shelf = new List<VideoItem>();
                    for (int i = 0; i < shelfCount; i++)
                    {
                        shelf.Add(shortPool[0]);
                        shortPool.RemoveAt(0);
                    }

                    result.Add(FeedItem.ShortsShelf(shelf));
                    continue;
                }

                if (postPool.Count > 0)
                {
                    result.Add(FeedItem.FromCommunity(postPool[0]));
                    postPool.RemoveAt(0);
                }
            }

            return result;
        }

        public static void ShuffleVideos(List<VideoItem> videos)
        {
            if (videos == null || videos.Count < 2)
            {
                return;
            }

            for (int i = videos.Count - 1; i > 0; i--)
            {
                var j = Rng.Next(i + 1);
                var temp = videos[i];
                videos[i] = videos[j];
                videos[j] = temp;
            }
        }

        public static int NextScatterGap()
        {
            return Rng.Next(3, 7);
        }
    }
}
