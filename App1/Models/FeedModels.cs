using System.Collections.Generic;

namespace App1.Models
{
    public enum FeedItemKind
    {
        Video,
        Placeholder,
        ShortsShelf,
        CommunityPost
    }

    public sealed class CommunityPostItem
    {
        public string PostId { get; set; }
        public string Author { get; set; }
        public string AuthorId { get; set; }
        public string AuthorThumbnail { get; set; }
        public string Content { get; set; }
        public string PublishedText { get; set; }
        public string ImageUrl { get; set; }
        public string VoteText { get; set; }
        public string LikeText { get; set; }
    }

    public sealed class FeedItem
    {
        public FeedItemKind Kind { get; set; }
        public VideoItem Video { get; set; }
        public CommunityPostItem CommunityPost { get; set; }
        public List<VideoItem> Shorts { get; set; }
        public string PlaceholderKey { get; set; }

        public static FeedItem FromVideo(VideoItem video)
        {
            var item = new FeedItem();
            item.Kind = FeedItemKind.Video;
            item.Video = video;
            return item;
        }

        public static FeedItem Placeholder(string key)
        {
            var item = new FeedItem();
            item.Kind = FeedItemKind.Placeholder;
            item.PlaceholderKey = key ?? "";
            return item;
        }

        public static FeedItem ShortsShelf(List<VideoItem> shorts)
        {
            var item = new FeedItem();
            item.Kind = FeedItemKind.ShortsShelf;
            item.Shorts = shorts ?? new List<VideoItem>();
            return item;
        }

        public static FeedItem FromCommunity(CommunityPostItem post)
        {
            var item = new FeedItem();
            item.Kind = FeedItemKind.CommunityPost;
            item.CommunityPost = post;
            return item;
        }
    }
}
