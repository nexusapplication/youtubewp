using App1.Helpers;
using App1.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;

namespace App1.Services
{
    public sealed class LocalLibrary
    {
        private const string HistoryFile = "history.json";
        private const string SubsFile = "subscriptions.json";
        private readonly SemaphoreSlim _ioLock = new SemaphoreSlim(1, 1);
        private List<JsonObject> _history;
        private List<JsonObject> _subscriptions;

        public async Task InitializeAsync()
        {
            _history = await ReadListAsync(HistoryFile);
            _subscriptions = await ReadListAsync(SubsFile);
        }

        public IReadOnlyList<JsonObject> History
        {
            get { return _history ?? new List<JsonObject>(); }
        }

        public IReadOnlyList<JsonObject> Subscriptions
        {
            get { return _subscriptions ?? new List<JsonObject>(); }
        }

        public async Task AddHistoryAsync(Models.VideoItem video)
        {
            if (video == null || string.IsNullOrEmpty(video.VideoId))
            {
                return;
            }

            await _ioLock.WaitAsync();
            try
            {
                EnsureLoaded();
                RemoveById(_history, "videoId", video.VideoId);
                _history.Insert(0, ToVideoObject(video));
                while (_history.Count > 50)
                {
                    _history.RemoveAt(_history.Count - 1);
                }

                await SaveListAsync(HistoryFile, _history);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public bool IsSubscribed(string authorId)
        {
            EnsureLoaded();
            return FindIndex(_subscriptions, "authorId", authorId) >= 0;
        }

        public async Task<bool> ToggleSubscriptionAsync(Models.ChannelItem channel)
        {
            if (channel == null || string.IsNullOrEmpty(channel.AuthorId))
            {
                return false;
            }

            await _ioLock.WaitAsync();
            try
            {
                return await ToggleSubscriptionCoreAsync(channel);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private async Task<bool> ToggleSubscriptionCoreAsync(Models.ChannelItem channel)
        {
            EnsureLoaded();
            var index = FindIndex(_subscriptions, "authorId", channel.AuthorId);
            var subscribed = index >= 0;

            try
            {
                if (App.Api != null && App1.App.Api.GoogleAuth.IsSignedIn)
                {
                    if (subscribed)
                    {
                        await App1.App.Api.DataApi.UnsubscribeFromChannelAsync(channel.AuthorId);
                    }
                    else
                    {
                        await App1.App.Api.DataApi.SubscribeToChannelAsync(channel.AuthorId);
                    }
                }
            }
            catch
            {
            }

            if (subscribed)
            {
                index = FindIndex(_subscriptions, "authorId", channel.AuthorId);
                if (index >= 0)
                {
                    _subscriptions.RemoveAt(index);
                }

                await SaveListAsync(SubsFile, _subscriptions);
                return false;
            }

            var obj = new JsonObject();
            obj["authorId"] = JsonValue.CreateStringValue(channel.AuthorId ?? "");
            obj["author"] = JsonValue.CreateStringValue(channel.Author ?? "");
            obj["authorThumbnail"] = JsonValue.CreateStringValue(channel.AuthorThumbnail ?? "");
            obj["subCountText"] = JsonValue.CreateStringValue(channel.SubCountText ?? "");
            _subscriptions.Insert(0, obj);
            await SaveListAsync(SubsFile, _subscriptions);
            return true;
        }

        public async Task SyncSubscriptionsFromApiAsync()
        {
            if (App1.App.Api == null || !App1.App.Api.GoogleAuth.IsSignedIn)
            {
                return;
            }

            await _ioLock.WaitAsync();
            try
            {
                // A null result means the fetch failed: keep whatever is stored
                // locally instead of wiping the subscription list.
                var channels = await App1.App.Api.DataApi.GetSubscribedChannelsAsync(500);
                if (channels == null)
                {
                    return;
                }

                EnsureLoaded();
                _subscriptions.Clear();
                foreach (var channel in channels)
                {
                    var obj = new JsonObject();
                    obj["authorId"] = JsonValue.CreateStringValue(channel.AuthorId ?? "");
                    obj["author"] = JsonValue.CreateStringValue(channel.Author ?? "");
                    obj["authorThumbnail"] = JsonValue.CreateStringValue(channel.AuthorThumbnail ?? "");
                    obj["subCountText"] = JsonValue.CreateStringValue(channel.SubCountText ?? "");
                    _subscriptions.Add(obj);
                }

                await SaveListAsync(SubsFile, _subscriptions);
            }
            catch
            {
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public Models.VideoItem ToVideoItem(JsonObject obj)
        {
            var item = new Models.VideoItem();
            item.VideoId = Get(obj, "videoId");
            item.Title = Get(obj, "title");
            item.Author = Get(obj, "author");
            item.AuthorId = Get(obj, "authorId");
            item.AuthorThumbnail = Get(obj, "authorThumbnail");
            item.Thumbnail = Get(obj, "thumbnail");
            item.PublishedText = Get(obj, "publishedText");
            item.LengthText = Get(obj, "lengthText");
            item.ViewCountText = Get(obj, "viewCountText");
            item.Subtitle = Get(obj, "subtitle");
            item.LengthSeconds = (int)GetLong(obj, "lengthSeconds");
            item.ViewCount = GetLong(obj, "viewCount");
            item.IsShort = Get(obj, "isShort") == "true";
            item.HasDuration = !string.IsNullOrEmpty(item.LengthText);
            return item;
        }

        public Models.ChannelItem ToChannelItem(JsonObject obj)
        {
            var item = new Models.ChannelItem();
            item.AuthorId = Get(obj, "authorId");
            item.Author = Get(obj, "author");
            item.AuthorThumbnail = Get(obj, "authorThumbnail");
            item.SubCountText = Get(obj, "subCountText");
            item.MetaText = item.SubCountText;
            return item;
        }

        private JsonObject ToVideoObject(Models.VideoItem video)
        {
            var obj = new JsonObject();
            obj["videoId"] = JsonValue.CreateStringValue(video.VideoId ?? "");
            obj["title"] = JsonValue.CreateStringValue(video.Title ?? "");
            obj["author"] = JsonValue.CreateStringValue(video.Author ?? "");
            obj["authorId"] = JsonValue.CreateStringValue(video.AuthorId ?? "");
            obj["authorThumbnail"] = JsonValue.CreateStringValue(video.AuthorThumbnail ?? "");
            obj["thumbnail"] = JsonValue.CreateStringValue(video.Thumbnail ?? "");
            obj["publishedText"] = JsonValue.CreateStringValue(video.PublishedText ?? "");
            obj["lengthText"] = JsonValue.CreateStringValue(video.LengthText ?? "");
            obj["viewCountText"] = JsonValue.CreateStringValue(video.ViewCountText ?? "");
            obj["subtitle"] = JsonValue.CreateStringValue(video.Subtitle ?? "");
            obj["lengthSeconds"] = JsonValue.CreateNumberValue(video.LengthSeconds);
            obj["viewCount"] = JsonValue.CreateNumberValue(video.ViewCount);
            obj["isShort"] = JsonValue.CreateStringValue(video.IsShort ? "true" : "false");
            return obj;
        }

        private void EnsureLoaded()
        {
            if (_history == null)
            {
                _history = new List<JsonObject>();
            }

            if (_subscriptions == null)
            {
                _subscriptions = new List<JsonObject>();
            }
        }

        private static string Get(JsonObject obj, string key)
        {
            if (obj == null || !obj.ContainsKey(key) || obj[key].ValueType == JsonValueType.String)
            {
                return obj != null && obj.ContainsKey(key) ? obj[key].GetString() : "";
            }

            if (obj[key].ValueType == JsonValueType.Number)
            {
                return obj[key].GetNumber().ToString();
            }

            return "";
        }

        private static long GetLong(JsonObject obj, string key)
        {
            if (obj == null || !obj.ContainsKey(key) || obj[key].ValueType != JsonValueType.Number)
            {
                return 0;
            }

            return (long)obj[key].GetNumber();
        }

        private static int FindIndex(List<JsonObject> list, string key, string value)
        {
            if (list == null || string.IsNullOrEmpty(value))
            {
                return -1;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (Get(list[i], key) == value)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void RemoveById(List<JsonObject> list, string key, string value)
        {
            var index = FindIndex(list, key, value);
            if (index >= 0)
            {
                list.RemoveAt(index);
            }
        }

        private static async Task<List<JsonObject>> ReadListAsync(string fileName)
        {
            var result = new List<JsonObject>();
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.GetFileAsync(fileName);
                var json = await FileIO.ReadTextAsync(file);
                var array = JsonArray.Parse(json);
                foreach (var item in array)
                {
                    if (item.ValueType == JsonValueType.Object)
                    {
                        result.Add(item.GetObject());
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static async Task SaveListAsync(string fileName, List<JsonObject> items)
        {
            var array = new JsonArray();
            foreach (var item in items)
            {
                array.Add(item);
            }

            var folder = ApplicationData.Current.LocalFolder;
            var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(file, array.Stringify());
        }
    }
}
