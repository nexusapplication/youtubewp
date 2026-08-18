using App1.Helpers;
using System.Collections.Generic;
using System.ComponentModel;

namespace App1.Models
{
    public class VideoItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string VideoId { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string AuthorId { get; set; }
        public string AuthorHandle { get; set; }
        public string AuthorThumbnail { get; set; }
        public string Thumbnail { get; set; }
        public string Description { get; set; }
        public string PublishedText { get; set; }
        public string LengthText { get; set; }
        public string ViewCountText { get; set; }
        public string Subtitle { get; set; }
        public string LiveBadge { get; set; }
        public int LengthSeconds { get; set; }
        public long ViewCount { get; set; }
        public long Published { get; set; }
        public bool IsLive { get; set; }
        public bool IsShort { get; set; }
        public bool HasDuration { get; set; }
        public bool IsAd { get; set; }
        public string ClickUrl { get; set; }

        public double CardHeight { get; set; }

        public void Notify(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    public sealed class CommentItem
    {
        public string Author { get; set; }
        public string AuthorThumbnail { get; set; }
        public string Content { get; set; }
        public string PublishedText { get; set; }
        public string LikeText { get; set; }
        public string ReplyText { get; set; }
        public bool IsOwner { get; set; }
        public bool IsPinned { get; set; }
        public string BadgeText { get; set; }
    }

    public sealed class ChannelItem
    {
        public string Author { get; set; }
        public string AuthorId { get; set; }
        public string AuthorThumbnail { get; set; }
        public string AuthorBanner { get; set; }
        public string Description { get; set; }
        public string SubCountText { get; set; }
        public string MetaText { get; set; }
        public string AuthorHandle { get; set; }
        public string VideoCountText { get; set; }
        public long SubCount { get; set; }
        public long TotalViews { get; set; }
    }

    public sealed class VideoDetails : VideoItem
    {
        public string SubCountText { get; set; }
        public string LikeText { get; set; }
        public string DislikeText { get; set; }
        public string RatingText { get; set; }
        public string Genre { get; set; }
        public int LikeCount { get; set; }
        public int DislikeCount { get; set; }
        public int CommentCount { get; set; }
        public double LikeRatio { get; set; }
        public string StreamUrl { get; set; }
        public string HlsUrl { get; set; }
        public List<string> PlaybackUrls { get; set; }
    }

    public static class VideoClassifier
    {
        public static bool IsShortVideo(string type, bool isShortFlag, int lengthSeconds, string title)
        {
            if (isShortFlag)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(type) &&
                string.Equals(type, "short", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(title) &&
                title.IndexOf("#shorts", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return lengthSeconds > 0 && lengthSeconds <= 60;
        }

        public static string BuildSubtitle(VideoItem item)
        {
            return FormatUtil.JoinMeta(
                item.Author,
                item.ViewCountText,
                item.PublishedText);
        }

        public static void EnrichFromChannel(VideoItem item, ChannelItem channel)
        {
            if (item == null || channel == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(item.Author))
            {
                item.Author = channel.Author;
            }

            if (string.IsNullOrEmpty(item.AuthorId))
            {
                item.AuthorId = channel.AuthorId;
            }

            if (string.IsNullOrEmpty(item.AuthorThumbnail))
            {
                item.AuthorThumbnail = channel.AuthorThumbnail;
            }

            item.Subtitle = BuildSubtitle(item);
        }

        public static void EnrichFromDetails(VideoItem item, VideoDetails details)
        {
            if (item == null || details == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(item.Author))
            {
                item.Author = details.Author;
            }

            if (string.IsNullOrEmpty(item.AuthorId))
            {
                item.AuthorId = details.AuthorId;
            }

            if (string.IsNullOrEmpty(item.AuthorThumbnail))
            {
                item.AuthorThumbnail = details.AuthorThumbnail;
            }

            if (string.IsNullOrEmpty(item.ViewCountText))
            {
                item.ViewCountText = details.ViewCountText;
            }

            if (string.IsNullOrEmpty(item.PublishedText))
            {
                item.PublishedText = details.PublishedText;
            }

            item.Subtitle = BuildSubtitle(item);
        }

        public static bool IsHomeFeedJunk(VideoItem item)
        {
            if (item == null)
            {
                return true;
            }

            if (item.IsAd)
            {
                return true;
            }

            if (string.IsNullOrEmpty(item.VideoId))
            {
                return true;
            }

            if (string.IsNullOrEmpty(item.Title) && string.IsNullOrEmpty(item.Thumbnail))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(item.Thumbnail) &&
                string.IsNullOrEmpty(item.Title) &&
                string.IsNullOrEmpty(item.Author) &&
                string.IsNullOrEmpty(item.ViewCountText) &&
                string.IsNullOrEmpty(item.PublishedText))
            {
                return true;
            }

            return false;
        }
    }
}
