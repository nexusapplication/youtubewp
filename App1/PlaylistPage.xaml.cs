using App1.Helpers;
using App1.Models;
using App1.Services;
using System.Collections.ObjectModel;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace App1
{
    public sealed partial class PlaylistPage : Page
    {
        private readonly ObservableCollection<VideoItem> _videos = new ObservableCollection<VideoItem>();

        public PlaylistPage()
        {
            this.InitializeComponent();
            VideosList.ItemsSource = _videos;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            var summary = e.Parameter as PlaylistSummary;
            if (summary == null || string.IsNullOrEmpty(summary.PlaylistId))
            {
                EmptyText.Visibility = Windows.UI.Xaml.Visibility.Visible;
                return;
            }

            TitleText.Text = summary.Title ?? "Playlist";
            PageLoading.IsActive = true;
            _videos.Clear();
            try
            {
                var videos = await App.Api.DataApi.GetPlaylistVideosAsync(summary.PlaylistId, 100);
                foreach (var video in videos)
                {
                    if (!video.IsShort)
                    {
                        _videos.Add(video);
                    }
                }

                EmptyText.Visibility = _videos.Count == 0
                    ? Windows.UI.Xaml.Visibility.Visible
                    : Windows.UI.Xaml.Visibility.Collapsed;
            }
            catch
            {
                EmptyText.Visibility = Windows.UI.Xaml.Visibility.Visible;
            }
            finally
            {
                PageLoading.IsActive = false;
            }
        }

        private void Back_Click(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (Frame != null && Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private void Video_ItemClick(object sender, ItemClickEventArgs e)
        {
            var video = e.ClickedItem as VideoItem;
            if (video == null || string.IsNullOrEmpty(video.VideoId))
            {
                return;
            }

            App.Api.PrefetchVideoPlayback(video.VideoId);
            AppNavigation.OpenVideo(this, video.VideoId);
        }
    }
}
