using App1.Helpers;
using App1.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.Web.Http;
using Windows.Web.Http.Headers;

namespace App1.Services
{
    public sealed class InvidiousService
    {
        private static readonly string[] FallbackInstances = new string[]
        {
            "https://inv.nadeko.net",
            "https://invidious.nerdvpn.de",
            "https://yt.chocolatemoo53.com",
            "https://invidious.tiekoetter.com",
            "https://invidious.f5.si",
            "https://yewtu.be",
            "https://invidious.fdn.fr",
            "https://invidious.materialio.us"
        };

        private readonly HttpClient _http;
        private List<string> _instances;
        private bool _instancesLoaded;

        public InvidiousService()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows Phone 8.1; ARM) App1/1.0");
            _http.DefaultRequestHeaders.Accept.Add(new HttpMediaTypeWithQualityHeaderValue("application/json"));
            CurrentInstance = LoadSavedInstance();
        }

        public string CurrentInstance { get; private set; }

        public bool UseCustomInstance
        {
            get
            {
                var value = ApplicationData.Current.LocalSettings.Values["UseCustomInstance"];
                return value is bool && (bool)value;
            }
            set
            {
                ApplicationData.Current.LocalSettings.Values["UseCustomInstance"] = value;
            }
        }

        public void SetCustomInstance(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            url = url.Trim().TrimEnd('/');
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            CurrentInstance = url;
            ApplicationData.Current.LocalSettings.Values["InstanceUrl"] = url;
            UseCustomInstance = true;
            _instancesLoaded = false;
        }

        public void UseAutomaticInstance()
        {
            UseCustomInstance = false;
            _instancesLoaded = false;
        }

        public string GetPlaybackUrl(string videoId, string itag)
        {
            var instance = string.IsNullOrEmpty(CurrentInstance) ? FallbackInstances[0] : CurrentInstance;
            return instance.TrimEnd('/') + "/latest_version?id=" + Uri.EscapeDataString(videoId) + "&local=true&itag=" + itag;
        }

        public async Task<List<VideoItem>> GetHomeAsync()
        {
            var combined = new List<VideoItem>();
            TryAdd(combined, await TryGetAsync(() => GetPopularAsync()));
            if (combined.Count < 16)
            {
                TryAdd(combined, await TryGetAsync(() => SearchVideosAsync("music", false)));
                TryAdd(combined, await TryGetAsync(() => SearchVideosAsync("gaming", false)));
                TryAdd(combined, await TryGetAsync(() => SearchVideosAsync("comedy", false)));
            }

            if (combined.Count < 8)
            {
                TryAdd(combined, await TryGetAsync(() => GetTrendingAsync("default")));
            }

            return Deduplicate(FilterByShorts(combined, false));
        }

        public async Task<List<VideoItem>> GetShortsAsync()
        {
            var combined = new List<VideoItem>();
            TryAdd(combined, await TryGetAsync(() => SearchVideosAsync("#shorts", true)));
            TryAdd(combined, await TryGetAsync(() => SearchVideosAsync("shorts", true)));
            TryAdd(combined, FilterByShorts(await TryGetAsync(() => GetTrendingAsync("default")), true));
            TryAdd(combined, FilterByShorts(await TryGetAsync(() => GetPopularAsync()), true));
            return Deduplicate(FilterByShorts(combined, true));
        }

        public async Task<List<VideoItem>> GetTrendingAsync(string type)
        {
            var path = "/api/v1/trending?hl=en&region=US";
            if (!string.IsNullOrEmpty(type) && type != "default")
            {
                path += "&type=" + Uri.EscapeDataString(type);
            }

            var json = await FetchAsync(path);
            return ParseVideoArray(json);
        }

        public async Task<List<VideoItem>> GetPopularAsync()
        {
            var json = await FetchAsync("/api/v1/popular?hl=en");
            return ParseVideoArray(json);
        }

        public async Task<List<VideoItem>> SearchVideosAsync(string query, bool shortsOnly)
        {
            var path = "/api/v1/search?hl=en&region=US&type=video&q=" + Uri.EscapeDataString(query ?? "");
            if (shortsOnly)
            {
                path += "&duration=short";
            }

            var json = await FetchAsync(path);
            var videos = ParseVideoArray(json);
            return shortsOnly ? FilterByShorts(videos, true) : videos;
        }

        public async Task<VideoDetails> GetVideoAsync(string videoId)
        {
            var json = await FetchAsync("/api/v1/videos/" + Uri.EscapeDataString(videoId) + "?hl=en&region=US");
            var obj = JsonUtil.ParseObject(json);
            var details = new VideoDetails();
            FillVideo(details, obj);
            details.Description = JsonUtil.GetString(obj, "description");
            details.LikeCount = JsonUtil.GetInt32(obj, "likeCount");
            details.DislikeCount = JsonUtil.GetInt32(obj, "dislikeCount");
            details.SubCountText = JsonUtil.GetString(obj, "subCountText");
            details.Genre = JsonUtil.GetString(obj, "genre");
            details.HlsUrl = ResolveUrl(JsonUtil.GetString(obj, "hlsUrl"));
            details.LikeText = FormatUtil.Compact(details.LikeCount);
            details.DislikeText = FormatUtil.Compact(details.DislikeCount);
            var total = details.LikeCount + details.DislikeCount;
            details.LikeRatio = total <= 0 ? 1 : (double)details.LikeCount / total;
            details.RatingText = total <= 0
                ? details.LikeText + " likes"
                : ((int)Math.Round(details.LikeRatio * 100)).ToString() + "% liked";
            details.StreamUrl = GetPlaybackUrl(details.VideoId, "18");
            details.PlaybackUrls = new List<string>();
            details.PlaybackUrls.Add(GetPlaybackUrl(details.VideoId, "18"));
            details.PlaybackUrls.Add(GetPlaybackUrl(details.VideoId, "22"));
            details.PlaybackUrls.Add(GetPlaybackUrl(details.VideoId, "36"));

            var authorThumbs = JsonUtil.GetArray(obj, "authorThumbnails");
            details.AuthorThumbnail = PickImage(authorThumbs, 88);

            return details;
        }

        public async Task<List<VideoItem>> GetRecommendedAsync(string videoId)
        {
            var json = await FetchAsync("/api/v1/videos/" + Uri.EscapeDataString(videoId) + "?hl=en&region=US");
            var obj = JsonUtil.ParseObject(json);
            var array = JsonUtil.GetArray(obj, "recommendedVideos");
            return ParseVideoArray(array);
        }

        public async Task EnrichVideoMetadataAsync(List<VideoItem> videos)
        {
            if (videos == null)
            {
                return;
            }

            foreach (var video in videos)
            {
                video.Subtitle = VideoClassifier.BuildSubtitle(video);
            }
        }

        public async Task<List<CommentItem>> GetCommentsAsync(string videoId)
        {
            var json = await FetchAsync("/api/v1/comments/" + Uri.EscapeDataString(videoId) + "?hl=en&sort_by=top");
            var obj = JsonUtil.ParseObject(json);
            var array = JsonUtil.GetArray(obj, "comments");
            var list = new List<CommentItem>();
            if (array == null)
            {
                return list;
            }

            foreach (var entry in array)
            {
                if (entry.ValueType != JsonValueType.Object)
                {
                    continue;
                }

                var comment = entry.GetObject();
                var item = new CommentItem();
                item.Author = JsonUtil.GetString(comment, "author");
                item.AuthorThumbnail = ImageUrl.Normalize(ResolveUrl(JsonUtil.GetString(comment, "authorThumbnail")), 48);
                item.Content = JsonUtil.GetString(comment, "content");
                item.PublishedText = JsonUtil.GetString(comment, "publishedText");
                item.LikeText = FormatUtil.Compact(JsonUtil.GetInt64(comment, "likeCount"));
                item.IsOwner = JsonUtil.GetBool(comment, "authorIsChannelOwner");
                item.IsPinned = JsonUtil.GetBool(comment, "isPinned");
                var replies = JsonUtil.GetObject(comment, "replies");
                var replyCount = replies != null ? JsonUtil.GetInt32(replies, "replyCount") : 0;
                item.ReplyText = replyCount > 0 ? replyCount.ToString() + " replies" : "";
                if (item.IsPinned)
                {
                    item.BadgeText = "Pinned";
                }
                else if (item.IsOwner)
                {
                    item.BadgeText = "Creator";
                }

                list.Add(item);
            }

            return list;
        }

        public async Task<int> GetCommentCountAsync(string videoId)
        {
            try
            {
                var json = await FetchAsync("/api/v1/comments/" + Uri.EscapeDataString(videoId) + "?hl=en&sort_by=top");
                var obj = JsonUtil.ParseObject(json);
                return JsonUtil.GetInt32(obj, "commentCount");
            }
            catch
            {
                return 0;
            }
        }

        public async Task<ChannelItem> GetChannelAsync(string channelId)
        {
            var json = await FetchAsync("/api/v1/channels/" + Uri.EscapeDataString(channelId) + "?hl=en");
            var obj = JsonUtil.ParseObject(json);
            var item = new ChannelItem();
            item.Author = JsonUtil.GetString(obj, "author");
            item.AuthorId = JsonUtil.GetString(obj, "authorId");
            item.Description = JsonUtil.GetString(obj, "description");
            item.SubCount = JsonUtil.GetInt64(obj, "subCount");
            item.TotalViews = JsonUtil.GetInt64(obj, "totalViews");
            item.SubCountText = FormatUtil.Subscribers(item.SubCount);
            item.MetaText = FormatUtil.JoinMeta(item.SubCountText, FormatUtil.Views(item.TotalViews));
            item.AuthorThumbnail = PickImage(JsonUtil.GetArray(obj, "authorThumbnails"), 88);
            item.AuthorBanner = PickImage(JsonUtil.GetArray(obj, "authorBanners"), 640);
            return item;
        }

        public async Task<List<VideoItem>> GetChannelVideosAsync(string channelId)
        {
            var json = await FetchAsync("/api/v1/channels/" + Uri.EscapeDataString(channelId) + "/videos?hl=en");
            return ParseVideoArray(json);
        }

        public async Task<List<VideoItem>> GetChannelShortsAsync(string channelId)
        {
            var json = await FetchAsync("/api/v1/channels/" + Uri.EscapeDataString(channelId) + "/shorts?hl=en");
            var videos = ParseVideoArray(json);
            foreach (var video in videos)
            {
                video.IsShort = true;
            }

            return videos;
        }

        public async Task<List<string>> GetInstancesAsync(bool forceRefresh)
        {
            await EnsureInstancesAsync(forceRefresh);
            return new List<string>(_instances);
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
            if (extra == null)
            {
                return;
            }

            target.AddRange(extra);
        }

        private List<VideoItem> ParseVideoArray(string json)
        {
            JsonArray array = null;
            try
            {
                var trimmed = (json ?? "").TrimStart();
                if (trimmed.StartsWith("["))
                {
                    array = JsonArray.Parse(json);
                }
                else
                {
                    var obj = JsonObject.Parse(json);
                    array = JsonUtil.GetArray(obj, "videos") ?? JsonUtil.GetArray(obj, "results");
                }
            }
            catch
            {
                array = new JsonArray();
            }

            return ParseVideoArray(array);
        }

        private List<VideoItem> ParseVideoArray(JsonArray array)
        {
            var list = new List<VideoItem>();
            if (array == null)
            {
                return list;
            }

            foreach (var entry in array)
            {
                if (entry.ValueType != JsonValueType.Object)
                {
                    continue;
                }

                var obj = entry.GetObject();
                var type = JsonUtil.GetString(obj, "type");
                if (type == "channel" || type == "playlist" || type == "hashtag")
                {
                    continue;
                }

                if (!obj.ContainsKey("videoId"))
                {
                    continue;
                }

                var item = new VideoItem();
                FillVideo(item, obj);
                list.Add(item);
            }

            return list;
        }

        private void FillVideo(VideoItem item, JsonObject obj)
        {
            item.VideoId = JsonUtil.GetString(obj, "videoId");
            item.Title = JsonUtil.GetString(obj, "title");
            item.Author = JsonUtil.GetString(obj, "author");
            item.AuthorId = JsonUtil.GetString(obj, "authorId");
            item.Description = JsonUtil.GetString(obj, "description");
            item.LengthSeconds = JsonUtil.GetInt32(obj, "lengthSeconds");
            item.ViewCount = JsonUtil.GetInt64(obj, "viewCount");
            item.Published = JsonUtil.GetInt64(obj, "published");
            item.IsLive = JsonUtil.GetBool(obj, "liveNow");
            item.LengthText = item.IsLive ? "LIVE" : FormatUtil.Duration(item.LengthSeconds);
            item.HasDuration = !string.IsNullOrEmpty(item.LengthText);
            item.LiveBadge = item.IsLive ? "LIVE" : "";
            item.ViewCountText = JsonUtil.GetString(obj, "viewCountText");
            if (string.IsNullOrEmpty(item.ViewCountText) && item.ViewCount > 0)
            {
                item.ViewCountText = FormatUtil.Views(item.ViewCount);
            }

            item.PublishedText = FormatUtil.Published(JsonUtil.GetString(obj, "publishedText"), item.Published);
            item.Thumbnail = PickThumbnail(JsonUtil.GetArray(obj, "videoThumbnails"), item.VideoId);
            item.AuthorThumbnail = PickImage(JsonUtil.GetArray(obj, "authorThumbnails"), 88);
            item.IsShort = VideoClassifier.IsShortVideo(
                JsonUtil.GetString(obj, "type"),
                JsonUtil.GetBool(obj, "isShort"),
                item.LengthSeconds,
                item.Title);
            item.Subtitle = VideoClassifier.BuildSubtitle(item);
        }

        private string PickThumbnail(JsonArray thumbnails, string videoId)
        {
            if (!string.IsNullOrEmpty(videoId))
            {
                return ImageUrl.VideoThumb(videoId);
            }

            return ImageUrl.Normalize(PickImage(thumbnails, 320), 320);
        }

        private string PickImage(JsonArray images, int preferredWidth)
        {
            if (images == null || images.Count == 0)
            {
                return "";
            }

            string fallback = "";
            string preferred = "";
            int preferredDelta = int.MaxValue;
            foreach (var entry in images)
            {
                if (entry.ValueType != JsonValueType.Object)
                {
                    continue;
                }

                var img = entry.GetObject();
                var url = ResolveUrl(JsonUtil.GetString(img, "url"));
                if (string.IsNullOrEmpty(url) || url.IndexOf("webp", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                fallback = url;
                var quality = JsonUtil.GetString(img, "quality");
                if (quality == "medium" || quality == "high" || quality == "hqdefault" || quality == "sddefault")
                {
                    preferred = url;
                }

                var width = JsonUtil.GetInt32(img, "width");
                var delta = Math.Abs(width - preferredWidth);
                if (width > 0 && delta < preferredDelta)
                {
                    preferredDelta = delta;
                    preferred = url;
                }
            }

            return ImageUrl.Normalize(string.IsNullOrEmpty(preferred) ? fallback : preferred, preferredWidth);
        }

        private string ResolveUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return "";
            }

            if (url.StartsWith("//"))
            {
                return "https:" + url;
            }

            if (url.StartsWith("/") && !string.IsNullOrEmpty(CurrentInstance))
            {
                return CurrentInstance.TrimEnd('/') + url;
            }

            return url;
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

        private async Task<string> FetchAsync(string relativePath)
        {
            await EnsureInstancesAsync(false);
            Exception lastError = null;
            var ordered = new List<string>();
            if (!string.IsNullOrEmpty(CurrentInstance))
            {
                ordered.Add(CurrentInstance);
            }

            foreach (var instance in _instances)
            {
                if (!string.Equals(instance, CurrentInstance, StringComparison.OrdinalIgnoreCase))
                {
                    ordered.Add(instance);
                }
            }

            foreach (var instance in ordered)
            {
                CancellationTokenSource cts = null;
                try
                {
                    cts = new CancellationTokenSource(TimeSpan.FromSeconds(18));
                    var uri = new Uri(instance.TrimEnd('/') + relativePath);
                    using (var response = await _http.GetAsync(uri).AsTask(cts.Token))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            CurrentInstance = instance;
                            ApplicationData.Current.LocalSettings.Values["InstanceUrl"] = instance;
                            return await response.Content.ReadAsStringAsync();
                        }

                        lastError = new Exception("HTTP " + ((int)response.StatusCode).ToString());
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
                finally
                {
                    if (cts != null)
                    {
                        cts.Dispose();
                    }
                }

                if (UseCustomInstance)
                {
                    break;
                }
            }

            throw new Exception("Could not reach an Invidious instance.", lastError);
        }

        private async Task EnsureInstancesAsync(bool forceRefresh)
        {
            if (_instancesLoaded && !forceRefresh && _instances != null && _instances.Count > 0)
            {
                return;
            }

            var list = new List<string>();
            if (UseCustomInstance && !string.IsNullOrEmpty(CurrentInstance))
            {
                list.Add(CurrentInstance);
                _instances = list;
                _instancesLoaded = true;
                return;
            }

            if (!forceRefresh)
            {
                var cached = ApplicationData.Current.LocalSettings.Values["InstanceList"] as string;
                AppendSplit(list, cached);
            }

            if (list.Count == 0 || forceRefresh)
            {
                CancellationTokenSource cts = null;
                try
                {
                    cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                    using (var response = await _http.GetAsync(new Uri("https://api.invidious.io/instances.json")).AsTask(cts.Token))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            var json = await response.Content.ReadAsStringAsync();
                            ParseInstances(json, list);
                        }
                    }
                }
                catch
                {
                }
                finally
                {
                    if (cts != null)
                    {
                        cts.Dispose();
                    }
                }
            }

            foreach (var fallback in FallbackInstances)
            {
                if (!ContainsInstance(list, fallback))
                {
                    list.Add(fallback);
                }
            }

            if (!string.IsNullOrEmpty(CurrentInstance) && !ContainsInstance(list, CurrentInstance))
            {
                list.Insert(0, CurrentInstance);
            }

            _instances = list;
            _instancesLoaded = true;
            ApplicationData.Current.LocalSettings.Values["InstanceList"] = string.Join(",", list.ToArray());
        }

        private static void ParseInstances(string json, List<string> list)
        {
            var root = JsonArray.Parse(json);
            foreach (var row in root)
            {
                if (row.ValueType != JsonValueType.Array)
                {
                    continue;
                }

                var pair = row.GetArray();
                if (pair.Count < 2 || pair[1].ValueType != JsonValueType.Object)
                {
                    continue;
                }

                var info = pair[1].GetObject();
                var type = JsonUtil.GetString(info, "type");
                var uri = JsonUtil.GetString(info, "uri");
                var api = !info.ContainsKey("api") || info["api"].ValueType != JsonValueType.Boolean || info["api"].GetBoolean();
                if (!api || string.IsNullOrEmpty(uri) || !string.Equals(type, "https", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                uri = uri.TrimEnd('/');
                if (!ContainsInstance(list, uri))
                {
                    list.Add(uri);
                }
            }
        }

        private static void AppendSplit(List<string> list, string cached)
        {
            if (string.IsNullOrEmpty(cached))
            {
                return;
            }

            var parts = cached.Split(',');
            foreach (var part in parts)
            {
                var url = part.Trim();
                if (!string.IsNullOrEmpty(url) && !ContainsInstance(list, url))
                {
                    list.Add(url);
                }
            }
        }

        private static bool ContainsInstance(List<string> list, string url)
        {
            foreach (var item in list)
            {
                if (string.Equals(item, url, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string LoadSavedInstance()
        {
            var saved = ApplicationData.Current.LocalSettings.Values["InstanceUrl"] as string;
            if (!string.IsNullOrEmpty(saved))
            {
                return saved.TrimEnd('/');
            }

            return FallbackInstances[0];
        }
    }
}
