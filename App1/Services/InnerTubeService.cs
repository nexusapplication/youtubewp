using App1.Helpers;
using App1.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage.Streams;
using Windows.Web.Http;
using Windows.Web.Http.Headers;

namespace App1.Services
{
    public sealed class InnerTubeService
    {
        private const string WebKey = "AIzaSyAO_FJ2SlqU8Q4STEHLGCilw_Y9_11qcW8";
        private const string AndroidKey = "AIzaSyA8eiZmM1FaDVjRy-df2KTyQ_vz_EUa7ts";
        private const string WebVersion = "2.20260216.01.00";
        private const string AndroidVersion = "20.49.37";
        private const string AndroidVrVersion = "1.62.27";
        private const string IosVersion = "20.10.4";
        private const string VideosParams = "EgZ2aWRlb3PyBgQKAjoA";
        private const string ShortsParams = "EgZzaG9ydHPyBgUKA5oBAA==";
        private const string ShortsHashtagParams = "6gUKCgZzaG9ydHMYAQ%3D%3D";

        private readonly HttpClient _http;
        private readonly Dictionary<string, List<string>> _playbackCache = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, int> _commentCountCache = new Dictionary<string, int>();
        private readonly Dictionary<string, List<CommentItem>> _commentsCache = new Dictionary<string, List<CommentItem>>();
        private readonly Dictionary<string, List<string>> _commentContinuationCache = new Dictionary<string, List<string>>();
        private string _visitorData;

        public InnerTubeService()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Accept.Add(new HttpMediaTypeWithQualityHeaderValue("application/json"));
        }

        public string GetPlaybackUrl(string videoId, string itag)
        {
            List<string> urls;
            if (!_playbackCache.TryGetValue(videoId, out urls) || urls == null || urls.Count == 0)
            {
                return "";
            }

            if (!string.IsNullOrEmpty(itag))
            {
                for (int i = 0; i < urls.Count; i++)
                {
                    if (urls[i].IndexOf("itag=" + itag, StringComparison.Ordinal) >= 0)
                    {
                        return urls[i];
                    }
                }
            }

            return urls[0];
        }

        public async Task<List<VideoItem>> GetHomeAsync()
        {
            var browseTask = TryGetAsync(() => BrowseVideosCachedAsync("FEwhat_to_watch", null, false, ClientKind.Web));
            var trendingTask = TryGetAsync(() => SearchVideosAsync("trending", false));
            var musicTask = TryGetAsync(() => SearchVideosAsync("music", false));
            await Task.WhenAll(browseTask, trendingTask, musicTask);

            var combined = new List<VideoItem>();
            TryAdd(combined, browseTask.Result);
            TryAdd(combined, trendingTask.Result);
            TryAdd(combined, musicTask.Result);

            if (combined.Count < 8)
            {
                TryAdd(combined, await TryGetAsync(() => SearchVideosAsync("popular", false)));
            }

            if (combined.Count < 4)
            {
                TryAdd(combined, await TryGetAsync(() => BrowseVideosCachedAsync("FEhashtag", ShortsHashtagParams, false, ClientKind.Web)));
            }

            return Deduplicate(FilterByShorts(combined, false));
        }

        public async Task<List<VideoItem>> GetShortsAsync()
        {
            var hashtagTask = TryGetAsync(() => BrowseVideosCachedAsync("FEhashtag", ShortsHashtagParams, true, ClientKind.Web));
            var searchTask = TryGetAsync(() => SearchVideosAsync("shorts", true));
            await Task.WhenAll(hashtagTask, searchTask);

            var combined = new List<VideoItem>();
            TryAdd(combined, hashtagTask.Result);
            TryAdd(combined, searchTask.Result);

            if (combined.Count < 4)
            {
                TryAdd(combined, await TryGetAsync(() => SearchVideosAsync("#shorts", true)));
            }

            return Deduplicate(FilterByShorts(combined, true));
        }

        public async Task<List<CommunityPostItem>> GetCommunityPostsAsync()
        {
            var combined = new List<CommunityPostItem>();
            if (!string.IsNullOrEmpty(_lastHomeBrowseJson))
            {
                TryAddPosts(combined, ParseCommunityPosts(_lastHomeBrowseJson));
            }

            if (combined.Count < 4)
            {
                try
                {
                    var subs = await BrowseRawCachedAsync("FEsubscriptions", null, ClientKind.Web);
                    TryAddPosts(combined, ParseCommunityPosts(subs));
                }
                catch
                {
                }
            }

            if (combined.Count < 4)
            {
                try
                {
                    var trending = await BrowseRawCachedAsync("FEtrending", null, ClientKind.Web);
                    TryAddPosts(combined, ParseCommunityPosts(trending));
                }
                catch
                {
                }
            }

            return combined;
        }

        public async Task<List<CommunityPostItem>> GetCommunityPostsForChannelsAsync(List<string> channelIds, int maxPosts)
        {
            var combined = new List<CommunityPostItem>();
            if (channelIds == null || channelIds.Count == 0)
            {
                return combined;
            }

            var paramVariants = new string[]
            {
                "EgVwb3N0c6AIEgZoLXNoGBA%3D%3D",
                "EgVwb3N0cyA%3D",
                "EgVwb3N0cyICCAU%3D"
            };

            for (int i = 0; i < channelIds.Count && combined.Count < maxPosts; i++)
            {
                var channelId = NormalizeChannelId(channelIds[i]);
                if (string.IsNullOrEmpty(channelId))
                {
                    continue;
                }

                for (int p = 0; p < paramVariants.Length && combined.Count < maxPosts; p++)
                {
                    try
                    {
                        var json = await BrowseRawAsync(channelId, paramVariants[p], ClientKind.Web);
                        TryAddPosts(combined, ParseCommunityPosts(json));
                        if (combined.Count > 0)
                        {
                            break;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return combined;
        }

        public async Task EnrichSingleVideoAsync(VideoItem video)
        {
            if (video == null)
            {
                return;
            }

            var list = new List<VideoItem> { video };
            await EnrichVideoMetadataAsync(list);
        }

        private static void TryAddPosts(List<CommunityPostItem> target, List<CommunityPostItem> extra)
        {
            if (extra == null)
            {
                return;
            }

            foreach (var post in extra)
            {
                foreach (var existing in target)
                {
                    if (existing.PostId == post.PostId && existing.Content == post.Content)
                    {
                        goto skip;
                    }
                }

                target.Add(post);
                skip:;
            }
        }

        private List<CommunityPostItem> ParseCommunityPosts(string json)
        {
            var root = JsonUtil.ParseObject(json);
            RememberVisitor(root);
            var list = new List<CommunityPostItem>();
            JsonUtil.WalkObjects(root, node =>
            {
                TryAddPostRenderer(list, JsonUtil.GetObject(node, "backstagePostRenderer"));
                TryAddPostRenderer(list, JsonUtil.GetObject(node, "communityPostRenderer"));
                TryAddPostRenderer(list, JsonUtil.GetObject(node, "postRenderer"));
                TryAddPostRenderer(list, JsonUtil.GetObject(node, "sharedPostRenderer"));
            });
            return list;
        }

        private void TryAddPostRenderer(List<CommunityPostItem> list, JsonObject renderer)
        {
            if (renderer == null)
            {
                return;
            }

            var item = new CommunityPostItem();
            item.PostId = FirstNonEmpty(
                JsonUtil.GetString(renderer, "postId"),
                JsonUtil.GetString(renderer, "id"));
            item.Author = FirstNonEmpty(
                JsonUtil.GetText(renderer, "authorText"),
                JsonUtil.GetText(JsonUtil.GetPath(renderer, "author", "displayName")));
            item.Content = FirstNonEmpty(
                JsonUtil.GetText(renderer, "contentText"),
                JsonUtil.GetText(JsonUtil.GetPath(renderer, "content", "content")),
                JsonUtil.GetText(renderer, "text"));
            item.PublishedText = JsonUtil.GetText(renderer, "publishedTimeText");
            item.ImageUrl = FirstNonEmpty(
                PickImageUrl(JsonUtil.GetObject(renderer, "backstageAttachment"), 640),
                PickImageUrl(JsonUtil.GetObject(renderer, "image"), 640),
                PickImageUrl(JsonUtil.GetObject(renderer, "attachment"), 640));
            item.VoteText = JsonUtil.GetText(renderer, "voteCount");
            item.LikeText = FormatUtil.Compact(JsonUtil.GetInt64(renderer, "likeCount"));
            item.AuthorThumbnail = PickImageUrl(JsonUtil.GetObject(renderer, "authorThumbnail"), 88);
            if (string.IsNullOrEmpty(item.AuthorThumbnail))
            {
                item.AuthorThumbnail = PickImageUrl(JsonUtil.GetPath(renderer, "author", "avatar"), 88);
            }

            var browseId = FindBrowseId(renderer);
            item.AuthorId = browseId;

            if (string.IsNullOrEmpty(item.Content) && string.IsNullOrEmpty(item.ImageUrl))
            {
                return;
            }

            foreach (var existing in list)
            {
                if (existing.PostId == item.PostId && existing.Content == item.Content)
                {
                    return;
                }
            }

            list.Add(item);
        }

        public async Task<List<VideoItem>> SearchVideosAsync(string query, bool shortsOnly)
        {
            var videos = await TryGetAsync(() => SearchWithClientAsync(query, shortsOnly, ClientKind.Web));
            if (videos.Count == 0)
            {
                TryAdd(videos, await TryGetAsync(() => SearchWithClientAsync(query, shortsOnly, ClientKind.Android)));
            }

            return shortsOnly ? FilterByShorts(videos, true) : videos;
        }

        private async Task<List<VideoItem>> SearchWithClientAsync(string query, bool shortsOnly, ClientKind kind)
        {
            var body = CreateBody(kind);
            body["query"] = JsonValue.CreateStringValue(query ?? "");
            var json = await PostAsync("search", body, kind);
            return ParseVideos(json, shortsOnly);
        }

        public async Task<VideoDetails> GetVideoAsync(string videoId)
        {
            VideoDetails cached;
            if (_detailsCache.TryGetValue(videoId, out cached) && cached != null)
            {
                return cached;
            }

            VideoDetails details;
            Task<string> nextTask = null;

            if (_playbackOnlyCache.TryGetValue(videoId, out details) && details != null
                && details.PlaybackUrls != null && details.PlaybackUrls.Count > 0)
            {
                nextTask = PostAsync("next", WithVideoId(CreateWebBody(), videoId), ClientKind.Web);
            }
            else
            {
                var playerTask = GetPlayerFastAsync(videoId);
                nextTask = PostAsync("next", WithVideoId(CreateWebBody(), videoId), ClientKind.Web);
                var player = await playerTask;
                details = BuildDetailsFromPlayer(videoId, player);
            }

            JsonObject next = null;
            try
            {
                var nextJson = await nextTask;
                next = await BackgroundWork.Run(() => JsonUtil.ParseObject(nextJson));
            }
            catch
            {
            }

            if (next != null)
            {
                FillFromNext(details, next);
                CacheRelated(videoId, next);
                CacheCommentsSeed(videoId, next);
            }

            details.AuthorThumbnail = ImageUrl.Normalize(details.AuthorThumbnail, 88);
            details.Subtitle = VideoClassifier.BuildSubtitle(details);
            details.LikeText = details.LikeCount > 0 ? FormatUtil.Compact(details.LikeCount) : "Like";
            details.DislikeText = details.DislikeCount > 0 ? FormatUtil.Compact(details.DislikeCount) : "Hidden";
            var total = details.LikeCount + details.DislikeCount;
            details.LikeRatio = total <= 0 ? 1 : (double)details.LikeCount / total;
            details.RatingText = details.LikeCount > 0
                ? FormatUtil.Compact(details.LikeCount) + " likes"
                : "";
            _detailsCache[videoId] = details;
            return details;
        }

        public async Task<VideoDetails> GetVideoPlaybackAsync(string videoId)
        {
            VideoDetails cached;
            if (_detailsCache.TryGetValue(videoId, out cached) && cached != null
                && cached.PlaybackUrls != null && cached.PlaybackUrls.Count > 0)
            {
                return cached;
            }

            if (_playbackOnlyCache.TryGetValue(videoId, out cached) && cached != null)
            {
                return cached;
            }

            var player = await GetPlayerFastAsync(videoId);
            var details = BuildDetailsFromPlayer(videoId, player);
            _playbackOnlyCache[videoId] = details;
            return details;
        }

        private async Task<JsonObject> GetPlayerFastAsync(string videoId)
        {
            try
            {
                var json = await PostAsync(
                    "player",
                    WithVideoId(CreateBody(ClientKind.Android), videoId),
                    ClientKind.Android);
                var obj = await BackgroundWork.Run(() => JsonUtil.ParseObject(json));
                if (JsonUtil.GetObject(obj, "streamingData") != null)
                {
                    return obj;
                }
            }
            catch
            {
            }

            return await GetPlayerAsync(videoId);
        }

        private VideoDetails BuildDetailsFromPlayer(string videoId, JsonObject player)
        {
            var details = new VideoDetails();
            details.VideoId = videoId;
            FillFromPlayer(details, player);
            details.PlaybackUrls = CollectPlaybackUrls(player, videoId);
            if (details.PlaybackUrls.Count > 0)
            {
                details.StreamUrl = details.PlaybackUrls[0];
            }

            details.AuthorThumbnail = ImageUrl.Normalize(details.AuthorThumbnail, 88);
            details.LikeText = details.LikeCount > 0 ? FormatUtil.Compact(details.LikeCount) : "Like";
            details.DislikeText = details.DislikeCount > 0 ? FormatUtil.Compact(details.DislikeCount) : "Hidden";
            _playbackCache[videoId] = details.PlaybackUrls;
            return details;
        }

        public async Task<List<VideoItem>> GetRecommendedAsync(string videoId)
        {
            List<VideoItem> cached;
            if (_relatedCache.TryGetValue(videoId, out cached) && cached != null && cached.Count > 0)
            {
                return cached;
            }

            var json = await PostAsync("next", WithVideoId(CreateWebBody(), videoId), ClientKind.Web);
            var videos = await BackgroundWork.Run(() => ParseVideos(json, false));
            _relatedCache[videoId] = videos;
            return videos;
        }

        public async Task EnrichVideoMetadataAsync(List<VideoItem> videos)
        {
            if (videos == null || videos.Count == 0)
            {
                return;
            }

            var pending = new List<string>();
            foreach (var video in videos)
            {
                if (string.IsNullOrEmpty(video.AuthorId))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(video.AuthorThumbnail) || string.IsNullOrEmpty(video.Author))
                {
                    if (!_channelCache.ContainsKey(video.AuthorId) && !pending.Contains(video.AuthorId))
                    {
                        pending.Add(video.AuthorId);
                    }
                }
            }

            for (int i = 0; i < pending.Count && i < 1; i++)
            {
                try
                {
                    _channelCache[pending[i]] = await GetChannelAsync(pending[i]);
                }
                catch
                {
                }
            }

            foreach (var video in videos)
            {
                if (string.IsNullOrEmpty(video.AuthorId))
                {
                    video.Subtitle = VideoClassifier.BuildSubtitle(video);
                    continue;
                }

                ChannelItem channel;
                if (_channelCache.TryGetValue(video.AuthorId, out channel))
                {
                    VideoClassifier.EnrichFromChannel(video, channel);
                }
                else
                {
                    video.Subtitle = VideoClassifier.BuildSubtitle(video);
                }
            }
        }

        public async Task<List<CommentItem>> GetCommentsAsync(string videoId)
        {
            List<CommentItem> cachedList;
            if (_commentsCache.TryGetValue(videoId, out cachedList) && cachedList != null && cachedList.Count > 0)
            {
                return new List<CommentItem>(cachedList);
            }

            var comments = new List<CommentItem>();
            var continuations = new List<string>();
            List<string> seedTokens;
            if (_commentContinuationCache.TryGetValue(videoId, out seedTokens) && seedTokens != null)
            {
                for (int i = 0; i < seedTokens.Count; i++)
                {
                    continuations.Add(seedTokens[i]);
                }
            }

            try
            {
                var webJson = await PostAsync("next", WithVideoId(CreateWebBody(), videoId), ClientKind.Web);
                await BackgroundWork.Run(() =>
                {
                    var webRoot = JsonUtil.ParseObject(webJson);
                    CollectEntityBatchComments(webRoot, comments);
                    CollectComments(webRoot, comments, continuations, videoId);
                    CollectEngagementCommentContinuations(webRoot, continuations);
                });
            }
            catch
            {
            }

            if (comments.Count == 0)
            {
                try
                {
                    var androidJson = await PostAsync("next", WithVideoId(CreateBody(ClientKind.Android), videoId), ClientKind.Android);
                    await BackgroundWork.Run(() =>
                    {
                        var androidRoot = JsonUtil.ParseObject(androidJson);
                        CollectEntityBatchComments(androidRoot, comments);
                        CollectComments(androidRoot, comments, continuations, videoId);
                        CollectEngagementCommentContinuations(androidRoot, continuations);
                    });
                }
                catch
                {
                }
            }

            if (comments.Count == 0)
            {
                try
                {
                    var iosJson = await PostAsync("next", WithVideoId(CreateBody(ClientKind.Ios), videoId), ClientKind.Ios);
                    await BackgroundWork.Run(() =>
                    {
                        var iosRoot = JsonUtil.ParseObject(iosJson);
                        CollectEntityBatchComments(iosRoot, comments);
                        CollectComments(iosRoot, comments, continuations, videoId);
                        CollectEngagementCommentContinuations(iosRoot, continuations);
                    });
                }
                catch
                {
                }
            }

            var pages = 0;
            while (continuations.Count > 0 && comments.Count < 50 && pages < 6)
            {
                var token = continuations[0];
                continuations.RemoveAt(0);
                try
                {
                    var body = CreateWebBody();
                    body["continuation"] = JsonValue.CreateStringValue(token);
                    var moreJson = await PostAsync("next", body, ClientKind.Web);
                    await BackgroundWork.Run(() =>
                    {
                        var more = JsonUtil.ParseObject(moreJson);
                        CollectEntityBatchComments(more, comments);
                        CollectComments(more, comments, continuations, videoId);
                        CollectEngagementCommentContinuations(more, continuations);
                    });
                }
                catch
                {
                    try
                    {
                        var body = CreateBody(ClientKind.Android);
                        body["continuation"] = JsonValue.CreateStringValue(token);
                        var moreJson = await PostAsync("next", body, ClientKind.Android);
                        await BackgroundWork.Run(() =>
                        {
                            var more = JsonUtil.ParseObject(moreJson);
                            CollectEntityBatchComments(more, comments);
                            CollectComments(more, comments, continuations, videoId);
                            CollectEngagementCommentContinuations(more, continuations);
                        });
                    }
                    catch
                    {
                        break;
                    }
                }

                pages++;
            }

            if (comments.Count > 0)
            {
                int cached;
                if (!_commentCountCache.TryGetValue(videoId, out cached) || comments.Count > cached)
                {
                    _commentCountCache[videoId] = comments.Count;
                }

                _commentsCache[videoId] = new List<CommentItem>(comments);
            }

            return comments;
        }

        public async Task<int> GetCommentCountAsync(string videoId)
        {
            int count;
            if (_commentCountCache.TryGetValue(videoId, out count))
            {
                return count;
            }

            try
            {
                var comments = await GetCommentsAsync(videoId);
                return comments.Count;
            }
            catch
            {
                return 0;
            }
        }

        public async Task<ChannelItem> GetChannelAsync(string channelId)
        {
            var id = NormalizeChannelId(channelId);
            ChannelItem cached;
            if (_channelCache.TryGetValue(id, out cached) && cached != null && !string.IsNullOrEmpty(cached.Author))
            {
                return cached;
            }

            var json = await BrowseRawCachedAsync(id, null, ClientKind.Web);
            var root = JsonUtil.ParseObject(json);
            var item = new ChannelItem();
            item.AuthorId = channelId;
            JsonUtil.WalkObjects(root, node =>
            {
                if (string.IsNullOrEmpty(item.Author))
                {
                    var meta = JsonUtil.GetObject(node, "channelMetadataRenderer");
                    if (meta != null)
                    {
                        item.Author = JsonUtil.GetString(meta, "title");
                        item.Description = JsonUtil.GetString(meta, "description");
                        item.AuthorId = JsonUtil.GetString(meta, "externalId", item.AuthorId);
                        item.AuthorThumbnail = PickImageUrl(JsonUtil.GetObject(meta, "avatar"), 88);
                        var vanity = JsonUtil.GetString(meta, "vanityChannelUrl");
                        if (!string.IsNullOrEmpty(vanity))
                        {
                            item.AuthorHandle = NormalizeChannelHandle(vanity);
                        }
                    }
                }

                var header = JsonUtil.GetObject(node, "c4TabbedHeaderRenderer");
                if (header != null)
                {
                    if (string.IsNullOrEmpty(item.Author))
                    {
                        item.Author = JsonUtil.GetText(header, "title");
                    }

                    item.SubCountText = FirstNonEmpty(item.SubCountText, JsonUtil.GetText(header, "subscriberCountText"));
                    item.VideoCountText = FirstNonEmpty(item.VideoCountText, JsonUtil.GetText(header, "videosCountText"));
                    item.AuthorThumbnail = FirstNonEmpty(item.AuthorThumbnail, PickImageUrl(JsonUtil.GetObject(header, "avatar"), 88));
                    item.AuthorBanner = FirstNonEmpty(item.AuthorBanner, PickImageUrl(JsonUtil.GetObject(header, "banner"), 640));
                    item.AuthorId = JsonUtil.GetString(header, "channelId", item.AuthorId);
                }

                var bannerVm = JsonUtil.GetObject(node, "imageBannerViewModel");
                if (bannerVm != null)
                {
                    item.AuthorBanner = FirstNonEmpty(item.AuthorBanner, PickImageUrl(JsonUtil.GetObject(bannerVm, "image"), 640));
                }

                var pageHeader = JsonUtil.GetObject(node, "pageHeaderRenderer") ?? JsonUtil.GetObject(node, "pageHeaderViewModel");
                if (pageHeader != null)
                {
                    if (string.IsNullOrEmpty(item.Author))
                    {
                        item.Author = FirstNonEmpty(JsonUtil.GetText(pageHeader, "title"), JsonUtil.GetText(pageHeader, "pageTitle"));
                    }

                    item.AuthorThumbnail = FirstNonEmpty(item.AuthorThumbnail, PickImageUrl(pageHeader, 88));
                    item.AuthorBanner = FirstNonEmpty(item.AuthorBanner, PickImageUrl(JsonUtil.GetObject(pageHeader, "banner"), 640));
                }

                var avatarVm = JsonUtil.GetObject(node, "avatarViewModel");
                if (avatarVm != null)
                {
                    item.AuthorThumbnail = FirstNonEmpty(item.AuthorThumbnail, PickImageUrl(avatarVm, 88));
                }
            });

            item.AuthorThumbnail = ImageUrl.Normalize(item.AuthorThumbnail, 176);
            item.AuthorBanner = ImageUrl.Normalize(item.AuthorBanner, 640);

            item.SubCount = FormatUtil.ParseCount(item.SubCountText);
            if (string.IsNullOrEmpty(item.SubCountText) && item.SubCount > 0)
            {
                item.SubCountText = FormatUtil.Subscribers(item.SubCount);
            }

            item.MetaText = FormatUtil.JoinMeta(item.SubCountText, item.VideoCountText);
            if (string.IsNullOrEmpty(item.AuthorHandle) && !string.IsNullOrEmpty(item.AuthorId))
            {
                item.AuthorHandle = item.AuthorId.StartsWith("@") ? item.AuthorId : "";
            }

            _channelCache[id] = item;
            return item;
        }

        public async Task<List<VideoItem>> GetChannelVideosAsync(string channelId)
        {
            return FilterByShorts(
                await BrowseVideosCachedAsync(NormalizeChannelId(channelId), VideosParams, false, ClientKind.Web),
                false);
        }

        public async Task<List<VideoItem>> GetChannelShortsAsync(string channelId)
        {
            var id = NormalizeChannelId(channelId);
            var shorts = await BrowseVideosCachedAsync(id, ShortsParams, true, ClientKind.Web);
            if (shorts.Count == 0)
            {
                shorts = FilterByShorts(await BrowseVideosCachedAsync(id, null, false, ClientKind.Web), true);
            }

            foreach (var video in shorts)
            {
                video.IsShort = true;
            }

            return shorts;
        }

        private readonly Dictionary<string, VideoDetails> _detailsCache = new Dictionary<string, VideoDetails>();
        private readonly Dictionary<string, VideoDetails> _playbackOnlyCache = new Dictionary<string, VideoDetails>();
        private readonly Dictionary<string, List<VideoItem>> _relatedCache = new Dictionary<string, List<VideoItem>>();
        private readonly Dictionary<string, ChannelItem> _channelCache = new Dictionary<string, ChannelItem>();
        private readonly Dictionary<string, string> _browseJsonCache = new Dictionary<string, string>();
        private string _lastHomeBrowseJson;

        private async Task<List<VideoItem>> BrowseVideosCachedAsync(string browseId, string browseParams, bool shortsOnly, ClientKind client)
        {
            var json = await BrowseRawCachedAsync(browseId, browseParams, client);
            if (browseId == "FEwhat_to_watch" && string.IsNullOrEmpty(browseParams))
            {
                _lastHomeBrowseJson = json;
            }

            return await BackgroundWork.Run(() => ParseVideos(json, shortsOnly));
        }

        private async Task<string> BrowseRawCachedAsync(string browseId, string browseParams, ClientKind client)
        {
            var key = browseId + "|" + (browseParams ?? "") + "|" + client.ToString();
            string cached;
            if (_browseJsonCache.TryGetValue(key, out cached))
            {
                return cached;
            }

            var json = await BrowseRawAsync(browseId, browseParams, client);
            if (!string.IsNullOrWhiteSpace(json))
            {
                _browseJsonCache[key] = json;
            }

            return json ?? "";
        }

        public void ClearBrowseCache()
        {
            _browseJsonCache.Clear();
            _lastHomeBrowseJson = "";
        }

        public void ClearAllCaches()
        {
            ClearBrowseCache();
            _detailsCache.Clear();
            _playbackOnlyCache.Clear();
            _relatedCache.Clear();
            _channelCache.Clear();
            _commentCountCache.Clear();
            _commentsCache.Clear();
            _commentContinuationCache.Clear();
        }

        private async Task<JsonObject> GetPlayerAsync(string videoId)
        {
            Exception last = null;
            var kinds = new ClientKind[] { ClientKind.Android, ClientKind.Web };
            for (int i = 0; i < kinds.Length; i++)
            {
                try
                {
                    var json = await PostAsync("player", WithVideoId(CreateBody(kinds[i]), videoId), kinds[i]);
                    var obj = JsonUtil.ParseObject(json);
                    var streaming = JsonUtil.GetObject(obj, "streamingData");
                    if (streaming != null)
                    {
                        return obj;
                    }

                    if (JsonUtil.GetObject(obj, "videoDetails") != null)
                    {
                        last = new Exception("Player returned metadata without streams.");
                        if (i == kinds.Length - 1)
                        {
                            return obj;
                        }
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }

            throw last ?? new Exception("InnerTube player request failed.");
        }

        private async Task<List<VideoItem>> BrowseVideosAsync(string browseId, string browseParams, bool shorts)
        {
            return await BrowseVideosAsync(browseId, browseParams, shorts, ClientKind.Web);
        }

        private async Task<List<VideoItem>> BrowseVideosAsync(string browseId, string browseParams, bool shorts, ClientKind client)
        {
            var json = await BrowseRawAsync(browseId, browseParams, client);
            return ParseVideos(json, shorts);
        }

        private async Task<string> BrowseRawAsync(string browseId, string browseParams)
        {
            return await BrowseRawAsync(browseId, browseParams, ClientKind.Web);
        }

        private async Task<string> BrowseRawAsync(string browseId, string browseParams, ClientKind client)
        {
            var body = CreateBody(client);
            body["browseId"] = JsonValue.CreateStringValue(browseId ?? "");
            if (!string.IsNullOrEmpty(browseParams))
            {
                body["params"] = JsonValue.CreateStringValue(browseParams);
            }

            return await PostAsync("browse", body, client);
        }

        private void FillFromPlayer(VideoDetails details, JsonObject player)
        {
            var video = JsonUtil.GetObject(player, "videoDetails");
            if (video == null)
            {
                return;
            }

            details.Title = JsonUtil.GetString(video, "title");
            details.Author = JsonUtil.GetString(video, "author");
            details.AuthorId = JsonUtil.GetString(video, "channelId");
            details.Description = JsonUtil.GetString(video, "shortDescription");
            details.LengthSeconds = JsonUtil.GetInt32(video, "lengthSeconds");
            details.ViewCount = JsonUtil.GetInt64(video, "viewCount");
            details.IsLive = JsonUtil.GetBool(video, "isLive") || JsonUtil.GetBool(video, "isLiveContent");
            details.LengthText = details.IsLive ? "LIVE" : FormatUtil.Duration(details.LengthSeconds);
            details.HasDuration = !string.IsNullOrEmpty(details.LengthText);
            details.LiveBadge = details.IsLive ? "LIVE" : "";
            details.ViewCountText = details.ViewCount > 0 ? FormatUtil.Views(details.ViewCount) : "";
            details.Thumbnail = ImageUrl.VideoThumb(details.VideoId);
            details.IsShort = VideoClassifier.IsShortVideo("", false, details.LengthSeconds, details.Title);
            details.HlsUrl = JsonUtil.GetString(JsonUtil.GetObject(player, "streamingData"), "hlsManifestUrl");
        }

        private void FillFromNext(VideoDetails details, JsonObject next)
        {
            JsonUtil.WalkObjects(next, node =>
            {
                var owner = JsonUtil.GetObject(node, "videoOwnerRenderer");
                if (owner != null)
                {
                    if (string.IsNullOrEmpty(details.Author))
                    {
                        details.Author = JsonUtil.GetText(owner, "title");
                    }

                    details.AuthorThumbnail = FirstNonEmpty(details.AuthorThumbnail, PickImageUrl(JsonUtil.GetObject(owner, "thumbnail"), 88));
                    details.SubCountText = FirstNonEmpty(details.SubCountText, JsonUtil.GetText(owner, "subscriberCountText"));
                    var browseId = FindBrowseId(owner);
                    if (!string.IsNullOrEmpty(browseId))
                    {
                        details.AuthorId = browseId;
                    }

                    if (string.IsNullOrEmpty(details.AuthorHandle))
                    {
                        var url = JsonUtil.GetString(JsonUtil.GetPath(owner, "navigationEndpoint", "commandMetadata", "webCommandMetadata"), "url");
                        details.AuthorHandle = NormalizeChannelHandle(url);
                    }
                }

                var primary = JsonUtil.GetObject(node, "videoPrimaryInfoRenderer");
                if (primary != null)
                {
                    if (string.IsNullOrEmpty(details.Title))
                    {
                        details.Title = JsonUtil.GetText(primary, "title");
                    }

                    details.PublishedText = FirstNonEmpty(details.PublishedText, JsonUtil.GetText(primary, "dateText"));
                    var viewRenderer = JsonUtil.GetPath(primary, "viewCount", "videoViewCountRenderer");
                    var views = JsonUtil.GetText(viewRenderer, "viewCount");
                    if (!string.IsNullOrEmpty(views))
                    {
                        details.ViewCountText = views;
                        if (details.ViewCount <= 0)
                        {
                            details.ViewCount = FormatUtil.ParseCount(views);
                        }
                    }
                }

                var secondary = JsonUtil.GetObject(node, "videoSecondaryInfoRenderer");
                if (secondary != null && string.IsNullOrEmpty(details.Description))
                {
                    var desc = JsonUtil.GetText(JsonUtil.GetObject(secondary, "description"));
                    if (string.IsNullOrEmpty(desc))
                    {
                        desc = JsonUtil.GetText(secondary, "attributedDescription");
                    }

                    if (!string.IsNullOrEmpty(desc))
                    {
                        details.Description = desc;
                    }
                }

                if (details.LikeCount <= 0)
                {
                    var likeText = JsonUtil.GetText(node, "likeCountIfLiked") + " " + JsonUtil.GetText(node, "title");
                    var label = JsonUtil.GetText(JsonUtil.GetPath(node, "accessibility", "accessibilityData"));
                    var combined = likeText + " " + label + " " + JsonUtil.GetText(node, "defaultText");
                    if (combined.IndexOf("like", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var parsed = FormatUtil.ParseCount(combined);
                        if (parsed > details.LikeCount && parsed < 10000000000)
                        {
                            details.LikeCount = (int)Math.Min(parsed, int.MaxValue);
                        }
                    }
                }

                var commentsEntry = JsonUtil.GetObject(node, "commentsEntryPointHeaderRenderer");
                if (commentsEntry != null)
                {
                    var commentCountText = FirstNonEmpty(
                        JsonUtil.GetText(commentsEntry, "commentCount"),
                        JsonUtil.GetText(commentsEntry, "onTap"));
                    var parsedCount = FormatUtil.ParseCount(commentCountText);
                    if (parsedCount > details.CommentCount)
                    {
                        details.CommentCount = (int)Math.Min(parsedCount, int.MaxValue);
                        _commentCountCache[details.VideoId] = details.CommentCount;
                    }
                }

                if (string.IsNullOrEmpty(details.Genre))
                {
                    var badge = JsonUtil.GetObject(node, "metadataBadgeRenderer");
                    if (badge != null)
                    {
                        var badgeLabel = JsonUtil.GetText(badge, "label");
                        if (!string.IsNullOrEmpty(badgeLabel))
                        {
                            details.Genre = badgeLabel;
                        }
                    }
                }
            });
        }

        private List<string> CollectPlaybackUrls(JsonObject player, string videoId)
        {
            var urls = new List<string>();
            var streaming = JsonUtil.GetObject(player, "streamingData");
            if (streaming == null)
            {
                return urls;
            }

            AddFormatUrls(JsonUtil.GetArray(streaming, "formats"), urls, new string[] { "18", "22", "17", "36" });
            if (urls.Count == 0)
            {
                AddFormatUrls(JsonUtil.GetArray(streaming, "adaptiveFormats"), urls, new string[] { "134", "133", "160" });
            }

            var hls = JsonUtil.GetString(streaming, "hlsManifestUrl");
            if (!string.IsNullOrEmpty(hls) && !urls.Contains(hls))
            {
                urls.Add(hls);
            }

            return urls;
        }

        private static void AddFormatUrls(JsonArray formats, List<string> urls, string[] preferredItags)
        {
            if (formats == null)
            {
                return;
            }

            var byItag = new Dictionary<string, string>();
            var extras = new List<string>();
            foreach (var entry in formats)
            {
                if (entry.ValueType != JsonValueType.Object)
                {
                    continue;
                }

                var format = entry.GetObject();
                var url = JsonUtil.GetString(format, "url");
                if (string.IsNullOrEmpty(url))
                {
                    continue;
                }

                var itag = JsonUtil.GetString(format, "itag");
                if (!string.IsNullOrEmpty(itag) && !byItag.ContainsKey(itag))
                {
                    byItag[itag] = url;
                }
                else
                {
                    extras.Add(url);
                }
            }

            for (int i = 0; i < preferredItags.Length; i++)
            {
                string url;
                if (byItag.TryGetValue(preferredItags[i], out url) && !urls.Contains(url))
                {
                    urls.Add(url);
                    byItag.Remove(preferredItags[i]);
                }
            }

            foreach (var pair in byItag)
            {
                if (!urls.Contains(pair.Value))
                {
                    urls.Add(pair.Value);
                }
            }

            foreach (var extra in extras)
            {
                if (!urls.Contains(extra))
                {
                    urls.Add(extra);
                }
            }
        }

        private List<VideoItem> ParseVideos(string json, bool forceShort)
        {
            var root = JsonUtil.ParseObject(json);
            RememberVisitor(root);
            var list = new List<VideoItem>();
            JsonUtil.WalkObjects(root, node =>
            {
                TryAddRenderer(list, JsonUtil.GetObject(node, "videoRenderer"), forceShort, IsPromotedRenderer(node));
                TryAddRenderer(list, JsonUtil.GetObject(node, "compactVideoRenderer"), forceShort, IsPromotedRenderer(node));
                TryAddRenderer(list, JsonUtil.GetObject(node, "gridVideoRenderer"), forceShort, IsPromotedRenderer(node));
                TryAddRenderer(list, JsonUtil.GetObject(node, "playlistVideoRenderer"), forceShort, IsPromotedRenderer(node));
                TryAddRenderer(list, JsonUtil.GetObject(node, "promotedVideoRenderer"), forceShort, true);
                TryAddRenderer(list, JsonUtil.GetObject(node, "compactPromotedItemRenderer"), forceShort, true);
                TryAddVideoWithContext(list, JsonUtil.GetObject(node, "videoWithContextRenderer"), forceShort);
                TryAddRichItem(list, JsonUtil.GetObject(node, "richItemRenderer"), forceShort);
                TryAddRenderer(list, JsonUtil.GetObject(node, "reelItemRenderer"), true);
                TryAddShortsLockup(list, JsonUtil.GetObject(node, "shortsLockupViewModel"), forceShort);
                TryAddLockup(list, JsonUtil.GetObject(node, "lockupViewModel"), forceShort);
                TryAddReelShelf(list, JsonUtil.GetObject(node, "reelShelfRenderer"), forceShort);
                TryAddDisplayAd(list, JsonUtil.GetObject(node, "displayAdRenderer"));
                TryAddDisplayAd(list, JsonUtil.GetObject(node, "compactDisplayAdRenderer"));
                TryAddDisplayAd(list, JsonUtil.GetObject(node, "searchPyvAdRenderer"));
                TryAddDisplayAd(list, JsonUtil.GetObject(node, "promotedSparklesWebRenderer"));
                TryAddDisplayAd(list, JsonUtil.GetObject(node, "promotedSparklesTextSearchRenderer"));
            });
            return Deduplicate(list);
        }

        private void TryAddRenderer(List<VideoItem> list, JsonObject renderer, bool forceShort, bool isPromoted = false)
        {
            if (renderer == null)
            {
                return;
            }

            var item = ParseRenderer(renderer, forceShort);
            if (item != null && !string.IsNullOrEmpty(item.VideoId))
            {
                if (isPromoted || IsPromotedRenderer(renderer))
                {
                    item.IsAd = true;
                }

                list.Add(item);
            }
        }

        private void TryAddVideoWithContext(List<VideoItem> list, JsonObject contextRenderer, bool forceShort)
        {
            if (contextRenderer == null)
            {
                return;
            }

            var promoted = IsPromotedRenderer(contextRenderer);
            var renderer = JsonUtil.GetObject(contextRenderer, "videoRenderer");
            if (renderer == null)
            {
                var content = JsonUtil.GetObject(contextRenderer, "content");
                renderer = JsonUtil.GetObject(content, "videoRenderer");
            }

            TryAddRenderer(list, renderer, forceShort, promoted);
        }

        private void TryAddRichItem(List<VideoItem> list, JsonObject richItem, bool forceShort)
        {
            if (richItem == null)
            {
                return;
            }

            var promoted = IsPromotedRenderer(richItem);
            var content = JsonUtil.GetObject(richItem, "content");
            if (content == null)
            {
                return;
            }

            TryAddRenderer(list, JsonUtil.GetObject(content, "videoRenderer"), forceShort, promoted);
            TryAddRenderer(list, JsonUtil.GetObject(content, "compactVideoRenderer"), forceShort, promoted);
            TryAddRenderer(list, JsonUtil.GetObject(content, "gridVideoRenderer"), forceShort, promoted);
            TryAddLockup(list, JsonUtil.GetObject(content, "lockupViewModel"), forceShort);
            TryAddShortsLockup(list, JsonUtil.GetObject(content, "shortsLockupViewModel"), forceShort);
        }

        private void TryAddDisplayAd(List<VideoItem> list, JsonObject ad)
        {
            if (ad == null)
            {
                return;
            }

            var item = new VideoItem();
            item.IsAd = true;
            item.Title = FirstNonEmpty(
                JsonUtil.GetText(ad, "title"),
                JsonUtil.GetText(ad, "headline"),
                JsonUtil.GetText(ad, "description"));
            item.Author = FirstNonEmpty(
                JsonUtil.GetText(ad, "advertiserName"),
                JsonUtil.GetText(ad, "subtitle"),
                JsonUtil.GetText(ad, "shortBylineText"));
            item.Thumbnail = FirstNonEmpty(
                PickImageUrl(JsonUtil.GetObject(ad, "thumbnail"), 480),
                PickImageUrl(JsonUtil.GetObject(ad, "image"), 480),
                PickImageUrl(JsonUtil.GetPath(ad, "bannerImage", "image"), 480));
            item.ClickUrl = FindClickUrl(ad);
            item.Subtitle = item.Author;
            if (string.IsNullOrEmpty(item.Title) && string.IsNullOrEmpty(item.Thumbnail))
            {
                return;
            }

            list.Add(item);
        }

        private static string FindClickUrl(JsonObject node)
        {
            if (node == null)
            {
                return "";
            }

            var nav = JsonUtil.GetObject(node, "navigationEndpoint");
            if (nav == null)
            {
                nav = JsonUtil.GetPath(node, "actionButton", "buttonRenderer", "navigationEndpoint");
            }

            if (nav == null)
            {
                nav = JsonUtil.GetPath(node, "ctaButton", "buttonRenderer", "navigationEndpoint");
            }

            var url = JsonUtil.GetString(JsonUtil.GetObject(nav, "urlEndpoint"), "url");
            if (!string.IsNullOrEmpty(url))
            {
                return url;
            }

            var command = JsonUtil.GetObject(nav, "commandMetadata");
            url = JsonUtil.GetString(JsonUtil.GetObject(command, "webCommandMetadata"), "url");
            return url ?? "";
        }

        private void TryAddShortsLockup(List<VideoItem> list, JsonObject lockup, bool forceShort)
        {
            if (lockup == null)
            {
                return;
            }

            var item = new VideoItem();
            item.VideoId = FirstNonEmpty(
                JsonUtil.GetString(lockup, "contentId"),
                JsonUtil.GetString(JsonUtil.GetPath(lockup, "onTap", "innertubeCommand", "reelWatchEndpoint"), "videoId"),
                JsonUtil.GetString(JsonUtil.GetPath(lockup, "onTap", "innertubeCommand", "watchEndpoint"), "videoId"));
            if (string.IsNullOrEmpty(item.VideoId) || item.VideoId.Length != 11)
            {
                return;
            }

            item.Title = FirstNonEmpty(
                JsonUtil.GetString(lockup, "accessibilityText"),
                JsonUtil.GetText(JsonUtil.GetPath(lockup, "metadata", "lockupMetadataViewModel"), "title"),
                JsonUtil.GetText(JsonUtil.GetPath(lockup, "metadata", "lockupMetadataViewModel", "metadata"), "content"),
                JsonUtil.GetText(lockup, "title"));
            item.Author = FirstNonEmpty(
                JsonUtil.GetText(JsonUtil.GetPath(lockup, "metadata", "lockupMetadataViewModel", "metadata"), "content"),
                JsonUtil.GetText(JsonUtil.GetPath(lockup, "metadata", "lockupMetadataViewModel"), "metadata"),
                JsonUtil.GetText(JsonUtil.GetPath(lockup, "avatarStack", "avatarStackViewModel"), "text"));
            item.AuthorId = FirstNonEmpty(
                item.AuthorId,
                FindBrowseId(JsonUtil.GetPath(lockup, "metadata", "lockupMetadataViewModel")));
            item.AuthorThumbnail = FirstNonEmpty(
                item.AuthorThumbnail,
                PickImageUrl(JsonUtil.GetPath(lockup, "metadata", "lockupMetadataViewModel", "image"), 88),
                PickAvatarFromStack(lockup));
            item.Thumbnail = FirstNonEmpty(
                PickImageUrl(JsonUtil.GetPath(lockup, "onTap", "innertubeCommand", "reelWatchEndpoint", "thumbnail"), 480),
                PickImageUrl(JsonUtil.GetPath(lockup, "contentImage", "image"), 480),
                PickImageUrl(JsonUtil.GetPath(lockup, "contentImage", "thumbnailViewModel", "image"), 480),
                ImageUrl.VideoThumb(item.VideoId));
            item.IsShort = forceShort ||
                           JsonUtil.GetPath(lockup, "onTap", "innertubeCommand", "reelWatchEndpoint") != null ||
                           VideoClassifier.IsShortVideo("", false, 0, item.Title);
            item.Subtitle = VideoClassifier.BuildSubtitle(item);
            list.Add(item);
        }

        private void TryAddReelShelf(List<VideoItem> list, JsonObject shelf, bool forceShort)
        {
            if (shelf == null)
            {
                return;
            }

            var items = JsonUtil.GetArray(shelf, "items");
            if (items == null)
            {
                return;
            }

            foreach (var entry in items)
            {
                if (entry.ValueType != JsonValueType.Object)
                {
                    continue;
                }

                var node = entry.GetObject();
                TryAddShortsLockup(list, JsonUtil.GetObject(node, "shortsLockupViewModel"), true);
                TryAddRenderer(list, JsonUtil.GetObject(node, "reelItemRenderer"), true);
            }
        }

        private void TryAddLockup(List<VideoItem> list, JsonObject lockup, bool forceShort)
        {
            if (lockup == null)
            {
                return;
            }

            var id = JsonUtil.GetString(lockup, "contentId");
            if (string.IsNullOrEmpty(id) || id.Length != 11)
            {
                var reel = JsonUtil.GetPath(lockup, "onTap", "innertubeCommand", "reelWatchEndpoint");
                id = JsonUtil.GetString(reel, "videoId");
            }

            if (string.IsNullOrEmpty(id) || id.Length != 11)
            {
                var watch = JsonUtil.GetPath(lockup, "onTap", "innertubeCommand", "watchEndpoint");
                id = JsonUtil.GetString(watch, "videoId");
            }

            if (string.IsNullOrEmpty(id) || id.Length != 11)
            {
                JsonUtil.WalkObjects(lockup, node =>
                {
                    if (!string.IsNullOrEmpty(id) && id.Length == 11)
                    {
                        return;
                    }

                    var nestedWatch = JsonUtil.GetObject(node, "watchEndpoint");
                    if (nestedWatch != null)
                    {
                        id = JsonUtil.GetString(nestedWatch, "videoId");
                    }
                });
            }

            if (string.IsNullOrEmpty(id) || id.Length != 11)
            {
                return;
            }

            var item = new VideoItem();
            item.VideoId = id;
            var metadata = JsonUtil.GetPath(lockup, "metadata", "lockupMetadataViewModel");
            item.Title = FirstNonEmpty(
                JsonUtil.GetText(metadata, "title"),
                JsonUtil.GetString(lockup, "accessibilityText"),
                JsonUtil.GetText(JsonUtil.GetPath(metadata, "metadata", "lockupMetadataViewModel"), "title"),
                JsonUtil.GetText(JsonUtil.GetObject(metadata, "metadata")));
            item.Author = FirstNonEmpty(
                JsonUtil.GetText(JsonUtil.GetPath(metadata, "metadata", "lockupMetadataViewModel"), "title"),
                JsonUtil.GetText(JsonUtil.GetObject(metadata, "metadata")));
            item.Author = CleanAuthorName(item.Author);
            item.Thumbnail = FirstNonEmpty(
                PickImageUrl(JsonUtil.GetPath(lockup, "contentImage", "thumbnailViewModel", "image"), 480),
                PickImageUrl(JsonUtil.GetPath(lockup, "contentImage", "image"), 480),
                PickImageUrl(JsonUtil.GetObject(lockup, "thumbnail"), 480),
                ImageUrl.VideoThumb(id));
            item.IsShort = forceShort ||
                           JsonUtil.GetPath(lockup, "onTap", "innertubeCommand", "reelWatchEndpoint") != null ||
                           VideoClassifier.IsShortVideo("", false, 0, item.Title);
            item.Subtitle = VideoClassifier.BuildSubtitle(item);
            list.Add(item);
        }

        private VideoItem ParseRenderer(JsonObject renderer, bool forceShort)
        {
            var item = new VideoItem();
            item.VideoId = JsonUtil.GetString(renderer, "videoId");
            if (string.IsNullOrEmpty(item.VideoId))
            {
                item.VideoId = JsonUtil.GetString(JsonUtil.GetPath(renderer, "navigationEndpoint", "reelWatchEndpoint"), "videoId");
            }

            if (string.IsNullOrEmpty(item.VideoId))
            {
                item.VideoId = JsonUtil.GetString(JsonUtil.GetPath(renderer, "onTap", "innertubeCommand", "reelWatchEndpoint"), "videoId");
            }

            if (string.IsNullOrEmpty(item.VideoId))
            {
                return null;
            }

            item.Title = FirstNonEmpty(
                JsonUtil.GetString(renderer, "accessibilityText"),
                JsonUtil.GetText(renderer, "title"),
                JsonUtil.GetText(renderer, "headline"),
                JsonUtil.GetText(renderer, "overlayMetadata"));
            ParseBylineFields(item, renderer, "ownerText");
            ParseBylineFields(item, renderer, "shortBylineText");
            ParseBylineFields(item, renderer, "longBylineText");
            if (string.IsNullOrEmpty(item.AuthorId))
            {
                var channelNav = JsonUtil.GetPath(renderer, "channelThumbnailSupportedRenderers", "channelThumbnailWithLinkRenderer", "navigationEndpoint", "browseEndpoint");
                item.AuthorId = JsonUtil.GetString(channelNav, "browseId");
            }

            if (string.IsNullOrEmpty(item.AuthorId))
            {
                item.AuthorId = FindBrowseId(renderer);
            }

            if (string.IsNullOrEmpty(item.Author))
            {
                item.Author = FirstNonEmpty(
                    JsonUtil.GetText(renderer, "ownerText"),
                    JsonUtil.GetText(renderer, "shortBylineText"),
                    JsonUtil.GetText(renderer, "longBylineText"));
            }

            item.Author = CleanAuthorName(item.Author);
            item.LengthText = JsonUtil.GetText(renderer, "lengthText");
            if (string.IsNullOrEmpty(item.LengthText))
            {
                item.LengthText = JsonUtil.GetText(JsonUtil.GetPath(renderer, "thumbnailOverlays"), "lengthText");
            }

            JsonUtil.WalkObjects(renderer, node =>
            {
                if (string.IsNullOrEmpty(item.LengthText))
                {
                    var overlay = JsonUtil.GetObject(node, "thumbnailOverlayTimeStatusRenderer");
                    if (overlay != null)
                    {
                        item.LengthText = JsonUtil.GetText(overlay, "text");
                    }
                }
            });

            item.LengthSeconds = FormatUtil.ParseDuration(item.LengthText);
            item.IsLive = !string.IsNullOrEmpty(item.LengthText) &&
                          item.LengthText.IndexOf("LIVE", StringComparison.OrdinalIgnoreCase) >= 0;
            item.HasDuration = !string.IsNullOrEmpty(item.LengthText);
            item.LiveBadge = item.IsLive ? "LIVE" : "";
            item.ViewCountText = FirstNonEmpty(
                JsonUtil.GetText(renderer, "viewCountText"),
                JsonUtil.GetText(renderer, "shortViewCountText"));
            item.ViewCount = FormatUtil.ParseCount(item.ViewCountText);
            item.PublishedText = JsonUtil.GetText(renderer, "publishedTimeText");
            item.Thumbnail = PickImageUrl(JsonUtil.GetObject(renderer, "thumbnail"), 480);
            if (string.IsNullOrEmpty(item.Thumbnail))
            {
                item.Thumbnail = FirstNonEmpty(
                    PickImageUrl(JsonUtil.GetPath(renderer, "onTap", "innertubeCommand", "reelWatchEndpoint", "thumbnail"), 480),
                    ImageUrl.VideoThumb(item.VideoId));
            }
            var channelThumbs = JsonUtil.GetPath(renderer, "channelThumbnailSupportedRenderers", "channelThumbnailWithLinkRenderer", "thumbnail");
            item.AuthorThumbnail = PickImageUrl(channelThumbs ?? JsonUtil.GetObject(renderer, "channelThumbnail"), 88);
            item.IsShort = forceShort ||
                           JsonUtil.GetPath(renderer, "navigationEndpoint", "reelWatchEndpoint") != null ||
                           JsonUtil.GetPath(renderer, "onTap", "innertubeCommand", "reelWatchEndpoint") != null ||
                           VideoClassifier.IsShortVideo("", false, item.LengthSeconds, item.Title);
            FillRendererMetadata(item, renderer);
            if (IsPromotedRenderer(renderer))
            {
                item.IsAd = true;
            }

            return item;
        }

        private static void FillRendererMetadata(VideoItem item, JsonObject renderer)
        {
            if (item == null || renderer == null)
            {
                return;
            }

            JsonUtil.WalkObjects(renderer, node =>
            {
                if (string.IsNullOrEmpty(item.Title))
                {
                    item.Title = FirstNonEmpty(
                        item.Title,
                        JsonUtil.GetText(node, "title"),
                        JsonUtil.GetText(node, "headline"),
                        JsonUtil.GetText(node, "primaryHeading"),
                        JsonUtil.GetText(node, "overlayMetadata"));
                }

                if (string.IsNullOrEmpty(item.ViewCountText))
                {
                    item.ViewCountText = FirstNonEmpty(
                        item.ViewCountText,
                        JsonUtil.GetText(node, "viewCountText"),
                        JsonUtil.GetText(node, "shortViewCountText"),
                        JsonUtil.GetText(node, "views"));
                }

                if (string.IsNullOrEmpty(item.PublishedText))
                {
                    item.PublishedText = JsonUtil.GetText(node, "publishedTimeText");
                }

                if (string.IsNullOrEmpty(item.AuthorThumbnail))
                {
                    item.AuthorThumbnail = FirstNonEmpty(
                        item.AuthorThumbnail,
                        PickImageUrl(JsonUtil.GetObject(node, "channelThumbnail"), 88),
                        PickImageUrl(JsonUtil.GetObject(node, "avatar"), 88),
                        PickImageUrl(JsonUtil.GetPath(node, "channelThumbnailSupportedRenderers", "channelThumbnailWithLinkRenderer", "thumbnail"), 88),
                        PickImageUrl(JsonUtil.GetPath(node, "avatar", "thumbnails"), 88),
                        PickImageUrl(JsonUtil.GetPath(node, "avatarStack", "avatar", "avatarViewModel", "image"), 88));
                }

                if (string.IsNullOrEmpty(item.AuthorId))
                {
                    var browseId = FindBrowseId(node);
                    if (!string.IsNullOrEmpty(browseId))
                    {
                        item.AuthorId = browseId;
                    }
                }
            });

            item.ViewCount = FormatUtil.ParseCount(item.ViewCountText);
            item.Subtitle = VideoClassifier.BuildSubtitle(item);
        }

        private static void ParseBylineFields(VideoItem item, JsonObject renderer, string key)
        {
            if (item == null || renderer == null || string.IsNullOrEmpty(key))
            {
                return;
            }

            var byline = JsonUtil.GetObject(renderer, key);
            if (byline == null)
            {
                return;
            }

            var runs = JsonUtil.GetArray(byline, "runs");
            if (runs != null && runs.Count > 0)
            {
                for (int i = 0; i < runs.Count; i++)
                {
                    if (runs[i].ValueType != JsonValueType.Object)
                    {
                        continue;
                    }

                    var run = runs[i].GetObject();
                    var text = JsonUtil.GetString(run, "text").Trim();
                    var browseId = JsonUtil.GetString(JsonUtil.GetPath(run, "navigationEndpoint", "browseEndpoint"), "browseId");
                    if (!string.IsNullOrEmpty(browseId) && browseId.StartsWith("UC"))
                    {
                        item.AuthorId = browseId;
                        if (string.IsNullOrEmpty(item.Author) && !string.IsNullOrEmpty(text) && text != "·")
                        {
                            item.Author = text;
                        }
                    }
                    else if (string.IsNullOrEmpty(item.ViewCountText) &&
                             text.IndexOf("view", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        item.ViewCountText = text;
                    }
                    else if (string.IsNullOrEmpty(item.Author) && !string.IsNullOrEmpty(text) && text != "·")
                    {
                        item.Author = text;
                    }
                }
            }
            else if (string.IsNullOrEmpty(item.Author))
            {
                item.Author = JsonUtil.GetText(byline);
            }

            if (string.IsNullOrEmpty(item.AuthorId))
            {
                var browseId = JsonUtil.GetString(JsonUtil.GetPath(byline, "navigationEndpoint", "browseEndpoint"), "browseId");
                if (!string.IsNullOrEmpty(browseId) && browseId.StartsWith("UC"))
                {
                    item.AuthorId = browseId;
                }
            }
        }

        private static bool IsPromotedRenderer(JsonObject renderer)
        {
            if (renderer == null)
            {
                return false;
            }

            if (JsonUtil.GetObject(renderer, "adBadge") != null ||
                JsonUtil.GetObject(renderer, "promotedBadge") != null ||
                JsonUtil.GetObject(renderer, "adMetadata") != null)
            {
                return true;
            }

            var style = JsonUtil.GetString(renderer, "style");
            if (!string.IsNullOrEmpty(style) &&
                style.IndexOf("PROMOTED", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            var badges = JsonUtil.GetArray(renderer, "badges");
            if (badges != null)
            {
                foreach (var badge in badges)
                {
                    if (badge.ValueType != JsonValueType.Object)
                    {
                        continue;
                    }

                    var label = FirstNonEmpty(
                        JsonUtil.GetText(badge.GetObject(), "label"),
                        JsonUtil.GetText(JsonUtil.GetObject(badge.GetObject(), "metadataBadgeRenderer"), "label"));
                    if (!string.IsNullOrEmpty(label) &&
                        label.IndexOf("ad", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string NormalizeChannelHandle(string vanity)
        {
            if (string.IsNullOrEmpty(vanity))
            {
                return "";
            }

            vanity = vanity.Trim();
            if (vanity.StartsWith("@"))
            {
                return vanity;
            }

            var marker = "youtube.com/@";
            var index = vanity.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var handle = vanity.Substring(index + marker.Length).Trim('/');
                if (!string.IsNullOrEmpty(handle))
                {
                    return "@" + handle;
                }
            }

            if (vanity.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return "@" + vanity.TrimStart('/');
        }

        private static void CollectEngagementCommentContinuations(JsonObject root, List<string> continuations)
        {
            JsonUtil.WalkObjects(root, node =>
            {
                var panel = JsonUtil.GetObject(node, "engagementPanelSectionListRenderer");
                if (panel == null)
                {
                    return;
                }

                var panelId = JsonUtil.GetString(panel, "panelIdentifier");
                var targetId = JsonUtil.GetString(panel, "targetId");
                var combined = (panelId + " " + targetId).ToLowerInvariant();
                if (combined.IndexOf("comment", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return;
                }

                CollectContinuationTokens(panel, continuations);
                CollectContinuationTokens(JsonUtil.GetObject(panel, "header"), continuations);
                CollectContinuationTokens(JsonUtil.GetObject(panel, "content"), continuations);

                var sectionList = JsonUtil.GetObject(JsonUtil.GetObject(panel, "content"), "sectionListRenderer");
                if (sectionList != null)
                {
                    CollectContinuationTokens(sectionList, continuations);
                }
            });
        }

        private static void CollectEntityBatchComments(JsonObject root, List<CommentItem> comments)
        {
            if (root == null)
            {
                return;
            }

            var framework = JsonUtil.GetObject(root, "frameworkUpdates");
            var batch = JsonUtil.GetObject(framework, "entityBatchUpdate");
            var mutations = JsonUtil.GetArray(batch, "mutations");
            if (mutations == null)
            {
                return;
            }

            foreach (var entry in mutations)
            {
                if (entry.ValueType != JsonValueType.Object)
                {
                    continue;
                }

                var mutation = entry.GetObject();
                TryAddCommentPayload(JsonUtil.GetPath(mutation, "payload", "commentEntityPayload"), comments);
                TryAddCommentViewModel(JsonUtil.GetObject(mutation, "commentViewModel"), comments);
            }
        }

        private void CollectComments(JsonObject root, List<CommentItem> comments, List<string> continuations, string videoId)
        {
            JsonUtil.WalkObjects(root, node =>
            {
                var thread = JsonUtil.GetObject(node, "commentThreadRenderer");
                if (thread != null)
                {
                    TryAddCommentRenderer(JsonUtil.GetPath(thread, "comment", "commentRenderer"), comments);
                    TryAddCommentViewModel(JsonUtil.GetObject(thread, "commentViewModel"), comments);
                }

                TryAddCommentRenderer(JsonUtil.GetObject(node, "commentRenderer"), comments);
                TryAddCommentViewModel(JsonUtil.GetObject(node, "commentViewModel"), comments);
                TryAddCommentPayload(JsonUtil.GetObject(node, "commentEntityPayload"), comments);
                TryAddCommentRenderer(JsonUtil.GetObject(node, "expandableCommentBodyRenderer"), comments);

                CollectContinuationTokens(node, continuations);

                if (!string.IsNullOrEmpty(videoId))
                {
                    var countText = JsonUtil.GetText(JsonUtil.GetObject(node, "commentsHeaderRenderer"), "countText");
                    if (!string.IsNullOrEmpty(countText))
                    {
                        var parsed = FormatUtil.ParseCount(countText);
                        if (parsed > 0)
                        {
                            _commentCountCache[videoId] = (int)Math.Min(parsed, int.MaxValue);
                        }
                    }

                    var commentsCount = JsonUtil.GetText(JsonUtil.GetObject(node, "commentsCount"), "text");
                    if (!string.IsNullOrEmpty(commentsCount))
                    {
                        var parsed = FormatUtil.ParseCount(commentsCount);
                        if (parsed > 0)
                        {
                            _commentCountCache[videoId] = (int)Math.Min(parsed, int.MaxValue);
                        }
                    }
                }
            });
        }

        private static void CollectContinuationTokens(JsonObject node, List<string> continuations)
        {
            var token = JsonUtil.GetString(JsonUtil.GetPath(node, "continuationEndpoint", "continuationCommand"), "token");
            if (string.IsNullOrEmpty(token))
            {
                token = JsonUtil.GetString(JsonUtil.GetObject(node, "continuationCommand"), "token");
            }

            if (string.IsNullOrEmpty(token))
            {
                token = JsonUtil.GetString(JsonUtil.GetPath(node, "continuationItemRenderer", "continuationEndpoint", "continuationCommand"), "token");
            }

            if (!string.IsNullOrEmpty(token) && !continuations.Contains(token))
            {
                continuations.Add(token);
            }
        }

        private static void TryAddCommentRenderer(JsonObject renderer, List<CommentItem> comments)
        {
            if (renderer == null)
            {
                return;
            }

            var item = new CommentItem();
            item.Author = FirstNonEmpty(
                JsonUtil.GetText(renderer, "authorText"),
                JsonUtil.GetText(JsonUtil.GetPath(renderer, "author", "displayName")),
                JsonUtil.GetText(JsonUtil.GetPath(renderer, "author", "name")));
            item.Content = FirstNonEmpty(
                JsonUtil.GetText(renderer, "contentText"),
                JsonUtil.GetText(JsonUtil.GetPath(renderer, "content", "content")),
                JsonUtil.GetText(renderer, "text"),
                JsonUtil.GetText(renderer, "commentText"),
                JsonUtil.GetText(JsonUtil.GetPath(renderer, "properties", "content", "content")),
                JsonUtil.GetText(JsonUtil.GetPath(renderer, "expandableText", "content")));
            item.PublishedText = JsonUtil.GetText(renderer, "publishedTimeText");
            item.LikeText = FormatUtil.Compact(JsonUtil.GetInt64(renderer, "likeCount"));
            item.IsOwner = JsonUtil.GetBool(renderer, "authorIsChannelOwner");
            item.IsPinned = JsonUtil.GetObject(renderer, "pinnedCommentBadge") != null;
            item.AuthorThumbnail = PickImageUrl(JsonUtil.GetObject(renderer, "authorThumbnail"), 48);
            if (string.IsNullOrEmpty(item.AuthorThumbnail))
            {
                item.AuthorThumbnail = PickImageUrl(JsonUtil.GetPath(renderer, "author", "avatar"), 48);
            }

            var replies = JsonUtil.GetInt32(renderer, "replyCount");
            item.ReplyText = replies > 0 ? replies.ToString() + " replies" : "";
            if (item.IsPinned)
            {
                item.BadgeText = "Pinned";
            }
            else if (item.IsOwner)
            {
                item.BadgeText = "Creator";
            }

            TryAddCommentItem(comments, item);
        }

        private static void TryAddCommentViewModel(JsonObject viewModel, List<CommentItem> comments)
        {
            if (viewModel == null)
            {
                return;
            }

            var item = new CommentItem();
            item.Author = FirstNonEmpty(
                JsonUtil.GetText(viewModel, "authorName"),
                JsonUtil.GetText(JsonUtil.GetPath(viewModel, "author", "displayName")),
                JsonUtil.GetText(JsonUtil.GetPath(viewModel, "author", "name")));
            item.Content = FirstNonEmpty(
                JsonUtil.GetText(viewModel, "content"),
                JsonUtil.GetText(JsonUtil.GetPath(viewModel, "content", "content")),
                JsonUtil.GetText(JsonUtil.GetPath(viewModel, "properties", "content", "content")),
                JsonUtil.GetText(JsonUtil.GetPath(viewModel, "properties", "content")));
            item.PublishedText = FirstNonEmpty(
                JsonUtil.GetText(viewModel, "publishedTime"),
                JsonUtil.GetText(JsonUtil.GetPath(viewModel, "toolbar", "publishedTime")));
            item.LikeText = FormatUtil.Compact(JsonUtil.GetInt64(viewModel, "likeCount"));
            item.AuthorThumbnail = PickImageUrl(JsonUtil.GetPath(viewModel, "author", "avatar"), 48);
            TryAddCommentItem(comments, item);
        }

        private static void TryAddCommentPayload(JsonObject payload, List<CommentItem> comments)
        {
            if (payload == null)
            {
                return;
            }

            var props = JsonUtil.GetObject(payload, "properties");
            var item = new CommentItem();
            item.Author = FirstNonEmpty(
                JsonUtil.GetString(payload, "authorName"),
                JsonUtil.GetText(JsonUtil.GetPath(props, "author", "displayName")),
                JsonUtil.GetText(JsonUtil.GetPath(payload, "author", "displayName")));
            item.Content = FirstNonEmpty(
                JsonUtil.GetString(payload, "content"),
                JsonUtil.GetText(payload, "content"),
                JsonUtil.GetText(JsonUtil.GetPath(props, "content", "content")),
                JsonUtil.GetText(JsonUtil.GetObject(props, "content")));
            item.PublishedText = FirstNonEmpty(
                JsonUtil.GetString(payload, "publishedTime"),
                JsonUtil.GetText(JsonUtil.GetPath(props, "publishedTime")));
            item.LikeText = FormatUtil.Compact(JsonUtil.GetInt64(payload, "likeCount"));
            item.AuthorThumbnail = PickImageUrl(JsonUtil.GetObject(payload, "avatar"), 48);
            if (string.IsNullOrEmpty(item.AuthorThumbnail))
            {
                item.AuthorThumbnail = PickImageUrl(JsonUtil.GetPath(props, "author", "avatar"), 48);
            }

            TryAddCommentItem(comments, item);
        }

        private static void TryAddCommentItem(List<CommentItem> comments, CommentItem item)
        {
            if (item == null || (string.IsNullOrEmpty(item.Content) && string.IsNullOrEmpty(item.Author)))
            {
                return;
            }

            foreach (var existing in comments)
            {
                if (existing.Author == item.Author && existing.Content == item.Content)
                {
                    return;
                }
            }

            comments.Add(item);
        }

        private void CacheRelated(string videoId, JsonObject next)
        {
            _relatedCache[videoId] = ParseVideos(next.Stringify(), false);
        }

        private void CacheCommentsSeed(string videoId, JsonObject next)
        {
            var comments = new List<CommentItem>();
            var continuations = new List<string>();
            CollectEntityBatchComments(next, comments);
            CollectComments(next, comments, continuations, videoId);
            CollectEngagementCommentContinuations(next, continuations);
            if (continuations.Count > 0)
            {
                _commentContinuationCache[videoId] = new List<string>(continuations);
            }

            if (comments.Count > 0)
            {
                _commentsCache[videoId] = new List<CommentItem>(comments);
                _commentCountCache[videoId] = comments.Count;
            }
        }

        private async Task<List<VideoItem>> TryGetAsync(Func<Task<List<VideoItem>>> loader)
        {
            try
            {
                return await loader();
            }
            catch
            {
                return new List<VideoItem>();
            }
        }

        private static void TryAdd(List<VideoItem> target, List<VideoItem> extra)
        {
            if (extra != null)
            {
                target.AddRange(extra);
            }
        }

        private JsonObject CreateWebBody()
        {
            return CreateBody(ClientKind.Web);
        }

        private JsonObject CreateBody(ClientKind kind)
        {
            var client = new JsonObject();
            client["hl"] = JsonValue.CreateStringValue("en");
            client["gl"] = JsonValue.CreateStringValue("US");
            if (!string.IsNullOrEmpty(_visitorData))
            {
                client["visitorData"] = JsonValue.CreateStringValue(_visitorData);
            }

            switch (kind)
            {
                case ClientKind.Android:
                    client["clientName"] = JsonValue.CreateStringValue("ANDROID");
                    client["clientVersion"] = JsonValue.CreateStringValue(AndroidVersion);
                    client["androidSdkVersion"] = JsonValue.CreateNumberValue(30);
                    client["osName"] = JsonValue.CreateStringValue("Android");
                    client["osVersion"] = JsonValue.CreateStringValue("11");
                    break;
                case ClientKind.AndroidVr:
                    client["clientName"] = JsonValue.CreateStringValue("ANDROID_VR");
                    client["clientVersion"] = JsonValue.CreateStringValue(AndroidVrVersion);
                    client["deviceMake"] = JsonValue.CreateStringValue("Oculus");
                    client["deviceModel"] = JsonValue.CreateStringValue("Quest 3");
                    client["androidSdkVersion"] = JsonValue.CreateNumberValue(32);
                    client["osName"] = JsonValue.CreateStringValue("Android");
                    client["osVersion"] = JsonValue.CreateStringValue("12L");
                    break;
                case ClientKind.Ios:
                    client["clientName"] = JsonValue.CreateStringValue("IOS");
                    client["clientVersion"] = JsonValue.CreateStringValue(IosVersion);
                    client["deviceMake"] = JsonValue.CreateStringValue("Apple");
                    client["deviceModel"] = JsonValue.CreateStringValue("iPhone16,2");
                    client["osName"] = JsonValue.CreateStringValue("iPhone");
                    client["osVersion"] = JsonValue.CreateStringValue("18.3.2.22D82");
                    break;
                default:
                    client["clientName"] = JsonValue.CreateStringValue("WEB");
                    client["clientVersion"] = JsonValue.CreateStringValue(WebVersion);
                    break;
            }

            var context = new JsonObject();
            context["client"] = client;
            var body = new JsonObject();
            body["context"] = context;
            body["contentCheckOk"] = JsonValue.CreateBooleanValue(true);
            body["racyCheckOk"] = JsonValue.CreateBooleanValue(true);
            return body;
        }

        private static JsonObject WithVideoId(JsonObject body, string videoId)
        {
            body["videoId"] = JsonValue.CreateStringValue(videoId ?? "");
            return body;
        }

        private async Task<string> PostAsync(string endpoint, JsonObject body, ClientKind kind)
        {
            var key = kind == ClientKind.Web ? WebKey : AndroidKey;
            var uri = new Uri("https://www.youtube.com/youtubei/v1/" + endpoint + "?prettyPrint=false&key=" + key);
            var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new HttpStringContent(body.Stringify(), UnicodeEncoding.Utf8, "application/json");
            ApplyHeaders(request, kind);
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            try
            {
                var response = await _http.SendRequestAsync(request).AsTask(cts.Token);
                var text = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception("InnerTube HTTP " + ((int)response.StatusCode) + " for " + endpoint);
                }

                RememberVisitor(await BackgroundWork.Run(() => JsonUtil.ParseObject(text)));
                return text;
            }
            finally
            {
                cts.Dispose();
                request.Dispose();
            }
        }

        private void ApplyHeaders(HttpRequestMessage request, ClientKind kind)
        {
            string ua;
            string clientName;
            string version;
            switch (kind)
            {
                case ClientKind.Android:
                    ua = "com.google.android.youtube/" + AndroidVersion + " (Linux; U; Android 11) gzip";
                    clientName = "3";
                    version = AndroidVersion;
                    break;
                case ClientKind.AndroidVr:
                    ua = "com.google.android.apps.youtube.vr.oculus/" + AndroidVrVersion + " (Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip";
                    clientName = "28";
                    version = AndroidVrVersion;
                    break;
                case ClientKind.Ios:
                    ua = "com.google.ios.youtube/" + IosVersion + " (iPhone16,2; U; CPU iOS 18_3_2 like Mac OS X;)";
                    clientName = "5";
                    version = IosVersion;
                    break;
                default:
                    ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
                    clientName = "1";
                    version = WebVersion;
                    break;
            }

            request.Headers.TryAppendWithoutValidation("User-Agent", ua);
            request.Headers.TryAppendWithoutValidation("X-YouTube-Client-Name", clientName);
            request.Headers.TryAppendWithoutValidation("X-YouTube-Client-Version", version);
            if (kind == ClientKind.Web)
            {
                request.Headers.TryAppendWithoutValidation("Origin", "https://www.youtube.com");
                request.Headers.TryAppendWithoutValidation("Referer", "https://www.youtube.com/");
            }

            if (!string.IsNullOrEmpty(_visitorData))
            {
                request.Headers.TryAppendWithoutValidation("X-Goog-Visitor-Id", _visitorData);
            }
        }

        private void RememberVisitor(JsonObject root)
        {
            if (root == null || !string.IsNullOrEmpty(_visitorData))
            {
                return;
            }

            var visitor = JsonUtil.GetString(JsonUtil.GetObject(root, "responseContext"), "visitorData");
            if (!string.IsNullOrEmpty(visitor))
            {
                _visitorData = visitor;
            }
        }

        private static string PickAvatarFromStack(JsonObject lockup)
        {
            var stack = JsonUtil.GetPath(lockup, "avatarStack", "avatarStackViewModel");
            if (stack == null)
            {
                return "";
            }

            var avatars = JsonUtil.GetArray(stack, "avatars");
            if (avatars == null || avatars.Count == 0)
            {
                return "";
            }

            if (avatars[0].ValueType != JsonValueType.Object)
            {
                return "";
            }

            return PickImageUrl(avatars[0].GetObject(), 88);
        }

        private static string PickImageUrl(JsonObject container, int preferredWidth)
        {
            return ImageUrl.Normalize(PickThumbnails(container, preferredWidth), preferredWidth);
        }

        private static string PickThumbnails(JsonObject container, int preferredWidth)
        {
            if (container == null)
            {
                return "";
            }

            var array = JsonUtil.GetArray(container, "thumbnails") ?? JsonUtil.GetArray(container, "sources");
            if (array == null)
            {
                var nested = JsonUtil.GetObject(container, "thumbnail");
                if (nested != null)
                {
                    return PickThumbnails(nested, preferredWidth);
                }

                var image = JsonUtil.GetObject(container, "image");
                if (image != null)
                {
                    return PickThumbnails(image, preferredWidth);
                }

                return JsonUtil.GetString(container, "url");
            }

            string best = "";
            string bestJpeg = "";
            var bestDelta = int.MaxValue;
            var bestJpegDelta = int.MaxValue;
            foreach (var entry in array)
            {
                if (entry.ValueType != JsonValueType.Object)
                {
                    continue;
                }

                var img = entry.GetObject();
                var url = JsonUtil.GetString(img, "url");
                if (string.IsNullOrEmpty(url))
                {
                    continue;
                }

                if (url.StartsWith("//"))
                {
                    url = "https:" + url;
                }

                var width = JsonUtil.GetInt32(img, "width");
                var delta = width > 0 ? Math.Abs(width - preferredWidth) : 10000;
                var isWebp = url.IndexOf("webp", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isWebp && delta < bestJpegDelta)
                {
                    bestJpegDelta = delta;
                    bestJpeg = url;
                }

                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = url;
                }
            }

            return string.IsNullOrEmpty(bestJpeg) ? best : bestJpeg;
        }

        private static string FindBrowseId(JsonObject obj)
        {
            string id = "";
            JsonUtil.WalkObjects(obj, node =>
            {
                if (string.IsNullOrEmpty(id))
                {
                    var browseId = JsonUtil.GetString(node, "browseId");
                    if (!string.IsNullOrEmpty(browseId) && browseId.StartsWith("UC"))
                    {
                        id = browseId;
                    }
                }
            });
            return id;
        }

        private static string NormalizeChannelId(string channelId)
        {
            if (string.IsNullOrEmpty(channelId))
            {
                return "";
            }

            if (channelId.StartsWith("UC") || channelId.StartsWith("FE") || channelId.StartsWith("VL"))
            {
                return channelId;
            }

            if (channelId.StartsWith("@"))
            {
                return channelId;
            }

            return channelId;
        }

        private static string CleanAuthorName(string author)
        {
            if (string.IsNullOrEmpty(author))
            {
                return "";
            }

            var index = author.IndexOf('·');
            if (index >= 0)
            {
                author = author.Substring(0, index);
            }

            return author.Trim();
        }

        private static string FirstNonEmpty(params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i];
                }
            }

            return "";
        }

        private static List<VideoItem> FilterByShorts(List<VideoItem> videos, bool shorts)
        {
            var list = new List<VideoItem>();
            if (videos == null)
            {
                return list;
            }

            foreach (var video in videos)
            {
                if (video.IsShort == shorts)
                {
                    list.Add(video);
                }
            }

            return list;
        }

        private static List<VideoItem> Deduplicate(List<VideoItem> videos)
        {
            var result = new List<VideoItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var video in videos)
            {
                if (video == null || string.IsNullOrEmpty(video.VideoId) || seen.Contains(video.VideoId))
                {
                    continue;
                }

                seen.Add(video.VideoId);
                result.Add(video);
            }

            return result;
        }

        private enum ClientKind
        {
            Web,
            Android,
            AndroidVr,
            Ios
        }
    }
}
