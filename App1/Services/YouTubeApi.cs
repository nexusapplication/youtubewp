using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App1.Helpers;
using Windows.Storage;
using App1.Models;

namespace App1.Services
{
    public sealed class YouTubeApi
    {
        private List<VideoItem> _shortsCache;
        private List<CommunityPostItem> _postsCache;
        private DateTime _shortsCacheTime;
        private DateTime _postsCacheTime;
        private readonly Dictionary<string, Task<VideoDetails>> _playbackPrefetch = new Dictionary<string, Task<VideoDetails>>();
        private const int PlaybackPrefetchMax = 6;

        public YouTubeApi()
        {
            Invidious = new InvidiousService();
            InnerTube = new InnerTubeService();
            GoogleAuth = new GoogleAuthService();
            DataApi = new YouTubeDataService(GoogleAuth);
            HomeFeed = new HomeFeedService(this, InnerTube, DataApi, GoogleAuth);
        }

        public InvidiousService Invidious { get; private set; }
        public InnerTubeService InnerTube { get; private set; }
        public GoogleAuthService GoogleAuth { get; private set; }
        public YouTubeDataService DataApi { get; private set; }
        public HomeFeedService HomeFeed { get; private set; }
        public int Generation { get; private set; }

        public bool UseInnerTube
        {
            get
            {
                var stored = ApplicationData.Current.LocalSettings.Values["Backend"] as string;
                if (string.IsNullOrEmpty(stored))
                {
                    return true;
                }

                return !string.Equals(stored, "Invidious", System.StringComparison.OrdinalIgnoreCase);
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values["Backend"] = value ? "InnerTube" : "Invidious";
                Generation++;
            }
        }

        public string StatusLabel
        {
            get
            {
                return UseInnerTube ? "InnerTube (YouTube)" : ("Invidious: " + Invidious.CurrentInstance);
            }
        }

        public string CurrentInstance
        {
            get
            {
                return UseInnerTube ? "InnerTube" : Invidious.CurrentInstance;
            }
        }

        public bool UseCustomInstance
        {
            get { return Invidious.UseCustomInstance; }
            set { Invidious.UseCustomInstance = value; }
        }

        public void SetCustomInstance(string url)
        {
            Invidious.SetCustomInstance(url);
            Generation++;
        }

        public void UseAutomaticInstance()
        {
            Invidious.UseAutomaticInstance();
            Generation++;
        }

        public Task<List<string>> GetInstancesAsync(bool forceRefresh)
        {
            return Invidious.GetInstancesAsync(forceRefresh);
        }

        public string GetPlaybackUrl(string videoId, string itag)
        {
            return UseInnerTube ? InnerTube.GetPlaybackUrl(videoId, itag) : Invidious.GetPlaybackUrl(videoId, itag);
        }

        public Task<List<VideoItem>> GetHomeAsync()
        {
            return UseInnerTube ? InnerTube.GetHomeAsync() : Invidious.GetHomeAsync();
        }

        public async Task<List<VideoItem>> GetShortsAsync()
        {
            if (_shortsCache != null && (DateTime.Now - _shortsCacheTime).TotalMinutes < 15)
            {
                return _shortsCache;
            }

            var list = UseInnerTube ? await InnerTube.GetShortsAsync() : await Invidious.GetShortsAsync();
            if (list != null && list.Count > 0)
            {
                _shortsCache = list;
                _shortsCacheTime = DateTime.Now;
            }

            return list ?? new List<VideoItem>();
        }

        public List<VideoItem> TryGetCachedShorts()
        {
            if (_shortsCache != null && (DateTime.Now - _shortsCacheTime).TotalMinutes < 15)
            {
                return _shortsCache;
            }

            return null;
        }

        public async Task<List<CommunityPostItem>> GetCommunityPostsAsync()
        {
            if (_postsCache != null && (DateTime.Now - _postsCacheTime).TotalMinutes < 15)
            {
                return _postsCache;
            }

            var list = UseInnerTube
                ? await InnerTube.GetCommunityPostsAsync()
                : new List<CommunityPostItem>();
            if (list != null && list.Count > 0)
            {
                _postsCache = list;
                _postsCacheTime = DateTime.Now;
            }

            return list ?? new List<CommunityPostItem>();
        }

        public async Task<List<CommunityPostItem>> GetCommunityPostsForChannelsAsync(List<string> channelIds, int maxPosts)
        {
            if (!UseInnerTube || channelIds == null || channelIds.Count == 0)
            {
                return new List<CommunityPostItem>();
            }

            var list = await InnerTube.GetCommunityPostsForChannelsAsync(channelIds, maxPosts);
            if (list != null && list.Count > 0)
            {
                _postsCache = list;
                _postsCacheTime = DateTime.Now;
            }

            return list ?? new List<CommunityPostItem>();
        }

        public Task EnrichSingleVideoAsync(VideoItem video)
        {
            return UseInnerTube ? InnerTube.EnrichSingleVideoAsync(video) : Invidious.EnrichVideoMetadataAsync(new List<VideoItem> { video });
        }

        public Task PrefetchHomeExtrasAsync()
        {
            return HomeFeed.PrefetchExtrasAsync();
        }

        public List<CommunityPostItem> TryGetCachedCommunityPosts()
        {
            if (_postsCache != null && (DateTime.Now - _postsCacheTime).TotalMinutes < 15)
            {
                return _postsCache;
            }

            return null;
        }

        public void ClearContentCache()
        {
            _shortsCache = null;
            _postsCache = null;
        }

        public Task<List<FeedItem>> LoadHomeFeedPageAsync()
        {
            return HomeFeed.LoadNextPageAsync();
        }

        public Task<FeedItem> LoadHomeFeedNextItemAsync()
        {
            return HomeFeed.LoadNextItemAsync();
        }

        public List<FeedItem> CreateHomePlaceholders(int count)
        {
            return HomeFeed.CreatePlaceholders(count);
        }

        public void ResetHomeFeed()
        {
            HomeFeed.Reset();
            ClearContentCache();
            if (UseInnerTube)
            {
                InnerTube.ClearBrowseCache();
            }
        }

        public void ResetAllFeeds()
        {
            ClearContentCache();
            HomeFeed.Reset();
            AppContentCache.Clear();
            if (UseInnerTube)
            {
                InnerTube.ClearAllCaches();
            }

            Generation++;
        }

        public Task<List<VideoItem>> SearchVideosAsync(string query, bool shortsOnly)
        {
            return UseInnerTube ? InnerTube.SearchVideosAsync(query, shortsOnly) : Invidious.SearchVideosAsync(query, shortsOnly);
        }

        public Task<VideoDetails> GetVideoAsync(string videoId)
        {
            return UseInnerTube ? InnerTube.GetVideoAsync(videoId) : Invidious.GetVideoAsync(videoId);
        }

        public Task<VideoDetails> GetVideoPlaybackAsync(string videoId)
        {
            Task<VideoDetails> prefetch;
            if (_playbackPrefetch.TryGetValue(videoId, out prefetch))
            {
                _playbackPrefetch.Remove(videoId);
                return prefetch;
            }

            return UseInnerTube ? InnerTube.GetVideoPlaybackAsync(videoId) : Invidious.GetVideoAsync(videoId);
        }

        public void PrefetchVideoPlayback(string videoId)
        {
            if (string.IsNullOrEmpty(videoId))
            {
                return;
            }

            if (_playbackPrefetch.ContainsKey(videoId))
            {
                return;
            }

            _playbackPrefetch[videoId] = UseInnerTube
                ? InnerTube.GetVideoPlaybackAsync(videoId)
                : Invidious.GetVideoAsync(videoId);
            TrimPlaybackPrefetch();
        }

        private void TrimPlaybackPrefetch()
        {
            while (_playbackPrefetch.Count > PlaybackPrefetchMax)
            {
                string oldestKey = null;
                foreach (var key in _playbackPrefetch.Keys)
                {
                    oldestKey = key;
                    break;
                }

                if (oldestKey != null)
                {
                    _playbackPrefetch.Remove(oldestKey);
                }
                else
                {
                    break;
                }
            }
        }

        public Task<List<VideoItem>> GetRecommendedAsync(string videoId)
        {
            return UseInnerTube ? InnerTube.GetRecommendedAsync(videoId) : Invidious.GetRecommendedAsync(videoId);
        }

        public Task EnrichVideoMetadataAsync(List<VideoItem> videos)
        {
            return UseInnerTube ? InnerTube.EnrichVideoMetadataAsync(videos) : Invidious.EnrichVideoMetadataAsync(videos);
        }

        public async Task<List<CommentItem>> GetCommentsAsync(string videoId)
        {
            if (UseInnerTube)
            {
                var list = await InnerTube.GetCommentsAsync(videoId);
                if (list.Count > 0)
                {
                    return list;
                }

                try
                {
                    var fallback = await Invidious.GetCommentsAsync(videoId);
                    if (fallback.Count > 0)
                    {
                        return fallback;
                    }
                }
                catch
                {
                }

                return list;
            }

            return await Invidious.GetCommentsAsync(videoId);
        }

        public Task<int> GetCommentCountAsync(string videoId)
        {
            return UseInnerTube ? InnerTube.GetCommentCountAsync(videoId) : Invidious.GetCommentCountAsync(videoId);
        }

        public Task<ChannelItem> GetChannelAsync(string channelId)
        {
            return UseInnerTube ? InnerTube.GetChannelAsync(channelId) : Invidious.GetChannelAsync(channelId);
        }

        public Task<List<VideoItem>> GetChannelVideosAsync(string channelId)
        {
            return UseInnerTube ? InnerTube.GetChannelVideosAsync(channelId) : Invidious.GetChannelVideosAsync(channelId);
        }

        public Task<List<VideoItem>> GetChannelShortsAsync(string channelId)
        {
            return UseInnerTube ? InnerTube.GetChannelShortsAsync(channelId) : Invidious.GetChannelShortsAsync(channelId);
        }
    }
}
