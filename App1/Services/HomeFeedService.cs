using App1.Helpers;
using App1.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace App1.Services
{
    public sealed class HomeFeedService
    {
        private const int PageSize = 10;
        private readonly YouTubeApi _api;
        private readonly InnerTubeService _innerTube;
        private readonly YouTubeDataService _data;
        private readonly GoogleAuthService _auth;

        private List<FeedItem> _anonymousVideoQueue;
        private int _anonymousVideoIndex;
        private List<string> _subscriptionChannels;
        private int _subscriptionChannelIndex;
        private List<VideoItem> _shortsPool;
        private List<CommunityPostItem> _postsPool;
        private bool _extrasLoaded;
        private bool _extrasLoading;

        private int _stepsUntilShorts;
        private int _stepsUntilPost;
        private List<VideoItem> _scatterShortPool;
        private List<CommunityPostItem> _scatterPostPool;

        public HomeFeedService(YouTubeApi api, InnerTubeService innerTube, YouTubeDataService data, GoogleAuthService auth)
        {
            _api = api;
            _innerTube = innerTube;
            _data = data;
            _auth = auth;
        }

        public void Reset()
        {
            _anonymousVideoQueue = null;
            _anonymousVideoIndex = 0;
            _subscriptionChannels = null;
            _subscriptionChannelIndex = 0;
            _shortsPool = null;
            _postsPool = null;
            _extrasLoaded = false;
            _extrasLoading = false;
            _stepsUntilShorts = 0;
            _stepsUntilPost = 0;
            _scatterShortPool = null;
            _scatterPostPool = null;
        }

        public async Task<FeedItem> LoadNextItemAsync()
        {
            if (_auth.IsSignedIn)
            {
                return await LoadSignedInNextItemAsync();
            }

            return await LoadAnonymousNextItemAsync();
        }

        public async Task<List<FeedItem>> LoadNextPageAsync()
        {
            var page = new List<FeedItem>();
            for (int i = 0; i < PageSize; i++)
            {
                var item = await LoadNextItemAsync();
                if (item == null)
                {
                    break;
                }

                page.Add(item);
            }

            return page;
        }

        public async Task PrefetchExtrasAsync()
        {
            if (_extrasLoaded || _extrasLoading)
            {
                return;
            }

            _extrasLoading = true;
            try
            {
                await EnsureExtrasAsync();
                MergeExtrasIntoScatterPools();
            }
            finally
            {
                _extrasLoading = false;
            }
        }

        private async Task EnsureExtrasAsync()
        {
            if (_extrasLoaded)
            {
                return;
            }

            try
            {
                _shortsPool = await _api.GetShortsAsync();
            }
            catch
            {
                _shortsPool = new List<VideoItem>();
            }

            try
            {
                if (_auth.IsSignedIn)
                {
                    var channels = await _data.GetSubscribedChannelIdsAsync(12);
                    _postsPool = await _api.GetCommunityPostsForChannelsAsync(channels, 16);
                }

                if (_postsPool == null || _postsPool.Count == 0)
                {
                    _postsPool = await _api.GetCommunityPostsAsync();
                }
            }
            catch
            {
                _postsPool = new List<CommunityPostItem>();
            }

            _extrasLoaded = true;
        }

        private List<VideoItem> GetShortsPool()
        {
            var cached = _api.TryGetCachedShorts();
            return cached ?? (_shortsPool ?? new List<VideoItem>());
        }

        private List<CommunityPostItem> GetPostsPool()
        {
            var cached = _api.TryGetCachedCommunityPosts();
            return cached ?? (_postsPool ?? new List<CommunityPostItem>());
        }

        private void ResetScatterState()
        {
            _stepsUntilShorts = FeedMixer.NextScatterGap();
            _stepsUntilPost = FeedMixer.NextScatterGap();
            _scatterShortPool = new List<VideoItem>(GetShortsPool());
            _scatterPostPool = new List<CommunityPostItem>(GetPostsPool());
        }

        private void MergeExtrasIntoScatterPools()
        {
            if (_scatterShortPool == null)
            {
                _scatterShortPool = new List<VideoItem>();
            }

            if (_scatterPostPool == null)
            {
                _scatterPostPool = new List<CommunityPostItem>();
            }

            var shorts = GetShortsPool();
            for (int i = 0; i < shorts.Count; i++)
            {
                if (!ContainsShort(_scatterShortPool, shorts[i]))
                {
                    _scatterShortPool.Add(shorts[i]);
                }
            }

            var posts = GetPostsPool();
            for (int i = 0; i < posts.Count; i++)
            {
                if (!ContainsPost(_scatterPostPool, posts[i]))
                {
                    _scatterPostPool.Add(posts[i]);
                }
            }
        }

        private static bool ContainsShort(List<VideoItem> list, VideoItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.VideoId))
            {
                return true;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].VideoId, item.VideoId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsPost(List<CommunityPostItem> list, CommunityPostItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.PostId))
            {
                return true;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].PostId, item.PostId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private FeedItem TryTakeScatteredExtra(int shortsShelfSize)
        {
            if (_scatterShortPool == null || _scatterPostPool == null)
            {
                return null;
            }

            if (_stepsUntilShorts <= 0 && _scatterShortPool.Count > 0)
            {
                var shelfCount = Math.Min(shortsShelfSize > 0 ? shortsShelfSize : 8, _scatterShortPool.Count);
                var shelf = new List<VideoItem>();
                for (int i = 0; i < shelfCount; i++)
                {
                    shelf.Add(_scatterShortPool[0]);
                    _scatterShortPool.RemoveAt(0);
                }

                _stepsUntilShorts = FeedMixer.NextScatterGap();
                _stepsUntilPost = Math.Max(1, _stepsUntilPost - 1);
                return FeedItem.ShortsShelf(shelf);
            }

            if (_stepsUntilPost <= 0 && _scatterPostPool.Count > 0)
            {
                var post = _scatterPostPool[0];
                _scatterPostPool.RemoveAt(0);
                _stepsUntilPost = FeedMixer.NextScatterGap();
                _stepsUntilShorts = Math.Max(1, _stepsUntilShorts - 1);
                return FeedItem.FromCommunity(post);
            }

            return null;
        }

        private async Task EnsureAnonymousVideosAsync()
        {
            if (_anonymousVideoQueue != null)
            {
                return;
            }

            var videos = await _api.GetHomeAsync();
            _anonymousVideoQueue = new List<FeedItem>();
            var channelIds = new List<string>();
            foreach (var video in videos)
            {
                if (VideoClassifier.IsHomeFeedJunk(video))
                {
                    continue;
                }

                _anonymousVideoQueue.Add(FeedItem.FromVideo(video));
                if (!string.IsNullOrEmpty(video.AuthorId) && !channelIds.Contains(video.AuthorId))
                {
                    channelIds.Add(video.AuthorId);
                }
            }

            _anonymousVideoIndex = 0;
            ResetScatterState();

            if (!_extrasLoaded && !_extrasLoading)
            {
                PrefetchExtrasAsync();
            }

            if (channelIds.Count > 0)
            {
                var ids = channelIds.Count > 10 ? channelIds.GetRange(0, 10) : channelIds;
                Task.Run(async () =>
                {
                    try
                    {
                        var channelPosts = await _api.GetCommunityPostsForChannelsAsync(ids, 12);
                        if (channelPosts != null && channelPosts.Count > 0)
                        {
                            _postsPool = channelPosts;
                            MergeExtrasIntoScatterPools();
                        }
                    }
                    catch
                    {
                    }
                });
            }
        }

        private async Task<FeedItem> LoadAnonymousNextItemAsync()
        {
            await EnsureAnonymousVideosAsync();
            MergeExtrasIntoScatterPools();

            var extra = TryTakeScatteredExtra(6);
            if (extra != null)
            {
                return extra;
            }

            if (_anonymousVideoIndex >= _anonymousVideoQueue.Count)
            {
                return null;
            }

            var item = _anonymousVideoQueue[_anonymousVideoIndex];
            _anonymousVideoIndex++;
            _stepsUntilShorts--;
            _stepsUntilPost--;
            return item;
        }

        private async Task<FeedItem> LoadSignedInNextItemAsync()
        {
            if (_subscriptionChannels == null)
            {
                _subscriptionChannels = await _data.GetSubscribedChannelIdsAsync(50);
                _subscriptionChannelIndex = 0;
                ResetScatterState();

                if (!_extrasLoaded && !_extrasLoading)
                {
                    PrefetchExtrasAsync();
                }
            }

            if (_subscriptionChannels.Count == 0)
            {
                return await LoadAnonymousNextItemAsync();
            }

            MergeExtrasIntoScatterPools();

            var extra = TryTakeScatteredExtra(6);
            if (extra != null)
            {
                return extra;
            }

            while (_subscriptionChannelIndex < _subscriptionChannels.Count)
            {
                var channelId = _subscriptionChannels[_subscriptionChannelIndex];
                _subscriptionChannelIndex++;
                var latest = await FetchChannelLatestSafeAsync(channelId);
                if (latest != null && !string.IsNullOrEmpty(latest.VideoId))
                {
                    _stepsUntilShorts--;
                    _stepsUntilPost--;
                    return FeedItem.FromVideo(latest);
                }
            }

            return null;
        }

        private async Task<VideoItem> FetchChannelLatestSafeAsync(string channelId)
        {
            try
            {
                return await _data.GetChannelLatestVideoAsync(channelId);
            }
            catch
            {
                return null;
            }
        }

        public List<FeedItem> CreatePlaceholders(int count)
        {
            return new List<FeedItem>();
        }

        public static List<FeedItem> MixRelatedFeed(List<VideoItem> related, List<VideoItem> shorts, List<CommunityPostItem> posts)
        {
            var feedVideos = new List<FeedItem>();
            if (related != null)
            {
                foreach (var video in related)
                {
                    if (video == null || video.IsShort || video.IsAd || string.IsNullOrEmpty(video.VideoId))
                    {
                        continue;
                    }

                    feedVideos.Add(FeedItem.FromVideo(video));
                }
            }

            if ((shorts == null || shorts.Count == 0) && (posts == null || posts.Count == 0))
            {
                return feedVideos;
            }

            return FeedMixer.ScatterExtras(feedVideos, shorts, posts, 4);
        }
    }
}
