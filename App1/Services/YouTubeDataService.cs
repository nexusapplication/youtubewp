using App1.Helpers;
using App1.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Web.Http;

namespace App1.Services
{
    public sealed class YouTubeDataService
    {
        private readonly GoogleAuthService _auth;
        private readonly HttpClient _http = new HttpClient();

        public YouTubeDataService(GoogleAuthService auth)
        {
            _auth = auth;
        }

        public async Task<List<string>> GetSubscribedChannelIdsAsync(int maxResults)
        {
            var ids = new List<string>();
            var token = await _auth.GetValidAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                return ids;
            }

            string pageToken = "";
            while (ids.Count < maxResults)
            {
                var take = Math.Min(50, maxResults - ids.Count);
                var uri =
                    "https://www.googleapis.com/youtube/v3/subscriptions?part=snippet&mine=true&maxResults=" + take
                    + (string.IsNullOrEmpty(pageToken) ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));

                var json = await GetJsonAsync(uri, token);
                var obj = JsonUtil.ParseObject(json);
                var items = JsonUtil.GetArray(obj, "items");
                if (items == null)
                {
                    break;
                }

                foreach (var entry in items)
                {
                    if (entry.ValueType != JsonValueType.Object)
                    {
                        continue;
                    }

                    var item = entry.GetObject();
                    var snippet = JsonUtil.GetObject(item, "snippet");
                    var resource = JsonUtil.GetObject(snippet, "resourceId");
                    var channelId = JsonUtil.GetString(resource, "channelId");
                    if (!string.IsNullOrEmpty(channelId))
                    {
                        ids.Add(channelId);
                    }
                }

                pageToken = JsonUtil.GetString(obj, "nextPageToken");
                if (string.IsNullOrEmpty(pageToken))
                {
                    break;
                }
            }

            return ids;
        }

        public async Task<VideoItem> GetChannelLatestVideoAsync(string channelId)
        {
            var token = await _auth.GetValidAccessTokenAsync();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(channelId))
            {
                return null;
            }

            var uploadsId = ToUploadsPlaylistId(channelId);
            var uri =
                "https://www.googleapis.com/youtube/v3/playlistItems?part=snippet&playlistId="
                + Uri.EscapeDataString(uploadsId)
                + "&maxResults=1";

            var json = await GetJsonAsync(uri, token);
            var obj = JsonUtil.ParseObject(json);
            var items = JsonUtil.GetArray(obj, "items");
            if (items == null || items.Count == 0)
            {
                return null;
            }

            return ParsePlaylistVideo(items[0].GetObject());
        }

        public async Task<List<VideoItem>> GetHistoryVideosAsync(int maxResults)
        {
            return await GetPlaylistVideosAsync("HL", maxResults);
        }

        public async Task<List<PlaylistSummary>> GetPlaylistsAsync(int maxResults)
        {
            var list = new List<PlaylistSummary>();
            var token = await _auth.GetValidAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                return list;
            }

            var uri =
                "https://www.googleapis.com/youtube/v3/playlists?part=snippet,contentDetails&mine=true&maxResults="
                + Math.Min(50, maxResults);

            var json = await GetJsonAsync(uri, token);
            var obj = JsonUtil.ParseObject(json);
            var items = JsonUtil.GetArray(obj, "items");
            if (items == null)
            {
                return list;
            }

            foreach (var entry in items)
            {
                if (entry.ValueType != JsonValueType.Object)
                {
                    continue;
                }

                var item = entry.GetObject();
                var snippet = JsonUtil.GetObject(item, "snippet");
                var contentDetails = JsonUtil.GetObject(item, "contentDetails");
                var summary = new PlaylistSummary();
                summary.PlaylistId = JsonUtil.GetString(item, "id");
                summary.Title = JsonUtil.GetString(snippet, "title");
                summary.Description = JsonUtil.GetString(snippet, "description");
                summary.ItemCount = JsonUtil.GetInt32(contentDetails, "itemCount");
                if (summary.ItemCount <= 0)
                {
                    summary.ItemCount = JsonUtil.GetInt32(snippet, "itemCount");
                }
                summary.Thumbnail = PickThumb(JsonUtil.GetObject(snippet, "thumbnails"));
                list.Add(summary);
            }

            return list;
        }

        public async Task<List<VideoItem>> GetPlaylistVideosAsync(string playlistId, int maxResults)
        {
            var videos = new List<VideoItem>();
            var token = await _auth.GetValidAccessTokenAsync();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(playlistId))
            {
                return videos;
            }

            var pageToken = "";
            while (videos.Count < maxResults)
            {
                var take = Math.Min(50, maxResults - videos.Count);
                var uri =
                    "https://www.googleapis.com/youtube/v3/playlistItems?part=snippet&playlistId="
                    + Uri.EscapeDataString(playlistId)
                    + "&maxResults=" + take
                    + (string.IsNullOrEmpty(pageToken) ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));

                var json = await GetJsonAsync(uri, token);
                var obj = JsonUtil.ParseObject(json);
                var items = JsonUtil.GetArray(obj, "items");
                if (items == null)
                {
                    break;
                }

                foreach (var entry in items)
                {
                    if (entry.ValueType != JsonValueType.Object)
                    {
                        continue;
                    }

                    var video = ParsePlaylistVideo(entry.GetObject());
                    if (video != null && !string.IsNullOrEmpty(video.VideoId))
                    {
                        videos.Add(video);
                    }
                }

                pageToken = JsonUtil.GetString(obj, "nextPageToken");
                if (string.IsNullOrEmpty(pageToken))
                {
                    break;
                }
            }

            return videos;
        }

        // Returns null when the subscriptions could not be fetched (expired or
        // unusable token, network failure) so callers can tell that apart from an
        // account that really has no subscriptions.
        public async Task<List<ChannelItem>> GetSubscribedChannelsAsync(int maxResults)
        {
            var channels = new List<ChannelItem>();
            var token = await _auth.GetValidAccessTokenAsync();
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            string pageToken = "";
            while (channels.Count < maxResults)
            {
                var take = Math.Min(50, maxResults - channels.Count);
                var uri =
                    "https://www.googleapis.com/youtube/v3/subscriptions?part=snippet&mine=true&maxResults=" + take
                    + (string.IsNullOrEmpty(pageToken) ? "" : "&pageToken=" + Uri.EscapeDataString(pageToken));

                var json = await GetJsonAsync(uri, token);
                var obj = JsonUtil.ParseObject(json);
                var items = JsonUtil.GetArray(obj, "items");
                if (items == null)
                {
                    break;
                }

                foreach (var entry in items)
                {
                    if (entry.ValueType != JsonValueType.Object)
                    {
                        continue;
                    }

                    var item = entry.GetObject();
                    var snippet = JsonUtil.GetObject(item, "snippet");
                    var resource = JsonUtil.GetObject(snippet, "resourceId");
                    var channelId = JsonUtil.GetString(resource, "channelId");
                    if (string.IsNullOrEmpty(channelId))
                    {
                        continue;
                    }

                    var channel = new ChannelItem();
                    channel.AuthorId = channelId;
                    channel.Author = JsonUtil.GetString(snippet, "title");
                    channel.AuthorThumbnail = PickThumb(JsonUtil.GetObject(snippet, "thumbnails"));
                    channels.Add(channel);
                }

                pageToken = JsonUtil.GetString(obj, "nextPageToken");
                if (string.IsNullOrEmpty(pageToken))
                {
                    break;
                }
            }

            return channels;
        }

        public async Task<bool> SubscribeToChannelAsync(string channelId)
        {
            var token = await _auth.GetValidAccessTokenAsync();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(channelId))
            {
                return false;
            }

            var body = new JsonObject();
            var snippet = new JsonObject();
            var resource = new JsonObject();
            resource["kind"] = JsonValue.CreateStringValue("youtube#channel");
            resource["channelId"] = JsonValue.CreateStringValue(channelId);
            snippet["resourceId"] = resource;
            body["snippet"] = snippet;

            await SendJsonAsync(
                "https://www.googleapis.com/youtube/v3/subscriptions?part=snippet",
                HttpMethod.Post,
                token,
                body.Stringify());
            return true;
        }

        public async Task<bool> UnsubscribeFromChannelAsync(string channelId)
        {
            var token = await _auth.GetValidAccessTokenAsync();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(channelId))
            {
                return false;
            }

            var listUri =
                "https://www.googleapis.com/youtube/v3/subscriptions?part=id&mine=true&forChannelId="
                + Uri.EscapeDataString(channelId);
            var json = await GetJsonAsync(listUri, token);
            var obj = JsonUtil.ParseObject(json);
            var items = JsonUtil.GetArray(obj, "items");
            if (items == null || items.Count == 0)
            {
                return false;
            }

            var subId = JsonUtil.GetString(items[0].GetObject(), "id");
            if (string.IsNullOrEmpty(subId))
            {
                return false;
            }

            await SendJsonAsync(
                "https://www.googleapis.com/youtube/v3/subscriptions?id=" + Uri.EscapeDataString(subId),
                HttpMethod.Delete,
                token,
                null);
            return true;
        }

        private async Task SendJsonAsync(string uri, HttpMethod method, string accessToken, string body)
        {
            using (var request = new HttpRequestMessage(method, new Uri(uri)))
            {
                request.Headers.TryAppendWithoutValidation("Authorization", "Bearer " + accessToken);
                if (!string.IsNullOrEmpty(body))
                {
                    request.Content = new HttpStringContent(body, Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");
                }

                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    var response = await _http.SendRequestAsync(request).AsTask(cts.Token);
                    var text = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception("YouTube Data API " + ((int)response.StatusCode) + ": " + text);
                    }
                }
                finally
                {
                    cts.Dispose();
                }
            }
        }

        private VideoItem ParsePlaylistVideo(JsonObject item)
        {
            var snippet = JsonUtil.GetObject(item, "snippet");
            if (snippet == null)
            {
                return null;
            }

            var resource = JsonUtil.GetObject(snippet, "resourceId");
            var videoId = JsonUtil.GetString(resource, "videoId");
            if (string.IsNullOrEmpty(videoId))
            {
                return null;
            }

            var video = new VideoItem();
            video.VideoId = videoId;
            video.Title = JsonUtil.GetString(snippet, "title");
            video.Description = JsonUtil.GetString(snippet, "description");
            video.Author = JsonUtil.GetString(snippet, "videoOwnerChannelTitle");
            if (string.IsNullOrEmpty(video.Author))
            {
                video.Author = JsonUtil.GetString(snippet, "channelTitle");
            }

            video.AuthorId = JsonUtil.GetString(snippet, "channelId");
            video.PublishedText = JsonUtil.GetString(snippet, "publishedAt");
            video.Thumbnail = PickThumb(JsonUtil.GetObject(snippet, "thumbnails"));
            if (string.IsNullOrEmpty(video.Thumbnail))
            {
                video.Thumbnail = ImageUrl.VideoThumb(videoId);
            }

            video.Subtitle = VideoClassifier.BuildSubtitle(video);
            return video;
        }

        private static string PickThumb(JsonObject thumbs)
        {
            if (thumbs == null)
            {
                return "";
            }

            var high = JsonUtil.GetObject(thumbs, "high");
            var medium = JsonUtil.GetObject(thumbs, "medium");
            var def = JsonUtil.GetObject(thumbs, "default");
            return FirstNonEmpty(
                JsonUtil.GetString(high, "url"),
                JsonUtil.GetString(medium, "url"),
                JsonUtil.GetString(def, "url"));
        }

        private static string FirstNonEmpty(string a, string b, string c)
        {
            if (!string.IsNullOrEmpty(a))
            {
                return a;
            }

            if (!string.IsNullOrEmpty(b))
            {
                return b;
            }

            return c ?? "";
        }

        private static string ToUploadsPlaylistId(string channelId)
        {
            if (string.IsNullOrEmpty(channelId))
            {
                return "";
            }

            if (channelId.StartsWith("UC", StringComparison.Ordinal))
            {
                return "UU" + channelId.Substring(2);
            }

            return channelId;
        }

        private static string FirstNonEmpty(string a, string b)
        {
            return !string.IsNullOrEmpty(a) ? a : b ?? "";
        }

        private async Task<string> GetJsonAsync(string relativeOrFullUri, string accessToken)
        {
            var uri = relativeOrFullUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? new Uri(relativeOrFullUri)
                : new Uri("https://www.googleapis.com/youtube/v3/" + relativeOrFullUri);

            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                request.Headers.TryAppendWithoutValidation("Authorization", "Bearer " + accessToken);
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    var response = await _http.SendRequestAsync(request).AsTask(cts.Token);
                    var text = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception("YouTube Data API " + ((int)response.StatusCode) + ": " + text);
                    }

                    return text;
                }
                finally
                {
                    cts.Dispose();
                }
            }
        }
    }

    public sealed class PlaylistSummary
    {
        public string PlaylistId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Thumbnail { get; set; }
        public int ItemCount { get; set; }
        public string ItemCountText
        {
            get
            {
                return ItemCount > 0 ? ItemCount.ToString() + " videos" : "";
            }
        }
    }
}
