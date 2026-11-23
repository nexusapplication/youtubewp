using App1.Models;
using App1.Services;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace App1.Helpers
{
    internal static class AppNavigation
    {
        public static void OpenVideo(Page page, string videoId)
        {
            if (page == null || string.IsNullOrEmpty(videoId))
            {
                return;
            }

            var frame = page.Frame;
            if (frame == null)
            {
                return;
            }

            if (frame.CurrentSourcePageType == typeof(VideoPage))
            {
                var videoPage = frame.Content as VideoPage;
                if (videoPage != null)
                {
                    videoPage.ReloadVideo(videoId);
                    return;
                }
            }

            frame.ContentTransitions = null;
            frame.Navigate(typeof(VideoPage), videoId);
        }

        public static void GoMain(Frame frame, int tabIndex)
        {
            if (frame == null)
            {
                return;
            }

            NavigationContext.SetMain(tabIndex);
            var mainPage = frame.Content as MainPage;
            if (mainPage != null)
            {
                mainPage.SelectMainTab(tabIndex);
                return;
            }

            frame.ContentTransitions = null;
            frame.Navigate(typeof(MainPage), tabIndex);
        }

        public static void GoChannel(Page page, string channelId)
        {
            if (page == null || string.IsNullOrEmpty(channelId))
            {
                return;
            }

            var frame = page.Frame;
            if (frame == null)
            {
                return;
            }

            var videoPage = frame.Content as VideoPage;
            if (videoPage != null)
            {
                videoPage.StashMiniPlayer();
                if (frame.CanGoBack)
                {
                    frame.GoBack();
                }
            }

            NavigationContext.SetChannel(channelId);
            frame.ContentTransitions = null;
            frame.Navigate(typeof(ChannelPage), channelId);
        }

        public static void OpenPlaylist(Page page, PlaylistSummary summary)
        {
            if (page == null || summary == null || string.IsNullOrEmpty(summary.PlaylistId))
            {
                return;
            }

            var frame = page.Frame;
            if (frame == null)
            {
                return;
            }

            frame.ContentTransitions = null;
            frame.Navigate(typeof(PlaylistPage), summary);
        }

        public static void GoSettings(Page page)
        {
            if (page == null || page.Frame == null)
            {
                return;
            }

            // A quick double tap must not stack two Settings pages.
            if (page.Frame.CurrentSourcePageType == typeof(SettingsPage))
            {
                return;
            }

            page.Frame.ContentTransitions = null;
            page.Frame.Navigate(typeof(SettingsPage));
        }

        public static void OpenVideoFromChannel(ChannelPage channelPage, string videoId)
        {
            if (channelPage == null || string.IsNullOrEmpty(videoId))
            {
                return;
            }

            NavigationContext.SetChannel(channelPage.ChannelId);
            OpenVideo(channelPage, videoId);
        }
    }

}
