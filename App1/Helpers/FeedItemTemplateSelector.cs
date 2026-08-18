using App1.Models;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace App1.Helpers
{
    public sealed class FeedItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate VideoTemplate { get; set; }
        public DataTemplate PlaceholderTemplate { get; set; }
        public DataTemplate ShortsShelfTemplate { get; set; }
        public DataTemplate CommunityPostTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item)
        {
            return Select(item);
        }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            return Select(item);
        }

        private DataTemplate Select(object item)
        {
            var feed = item as FeedItem;
            if (feed == null)
            {
                return VideoTemplate;
            }

            switch (feed.Kind)
            {
                case FeedItemKind.Placeholder:
                    return PlaceholderTemplate ?? VideoTemplate;
                case FeedItemKind.ShortsShelf:
                    return ShortsShelfTemplate ?? VideoTemplate;
                case FeedItemKind.CommunityPost:
                    return CommunityPostTemplate ?? VideoTemplate;
                default:
                    return VideoTemplate;
            }
        }
    }
}
