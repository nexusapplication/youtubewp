using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace App1.Helpers
{
    internal sealed class MiniPlayerUiHelper
    {
        private readonly MediaElement _player;
        private readonly FrameworkElement _bar;
        private readonly RowDefinition _row;
        private readonly TextBlock _title;
        private readonly TextBlock _author;
        private readonly Image _thumb;
        private readonly Image _playIcon;

        public MiniPlayerUiHelper(
            MediaElement player,
            FrameworkElement bar,
            RowDefinition row,
            TextBlock title,
            TextBlock author,
            Image thumb,
            Image playIcon)
        {
            _player = player;
            _bar = bar;
            _row = row;
            _title = title;
            _author = author;
            _thumb = thumb;
            _playIcon = playIcon;
            _player.MediaOpened += Player_MediaOpened;
        }

        public void SyncFromSession()
        {
            if (!MiniPlayerSession.PendingShow && !MiniPlayerSession.IsActive)
            {
                Hide();
                return;
            }

            if (!MiniPlayerSession.IsActive || MiniPlayerSession.Source == null)
            {
                MiniPlayerSession.Clear();
                Hide();
                return;
            }

            _title.Text = MiniPlayerSession.Title;
            _author.Text = MiniPlayerSession.Author;
            _thumb.Source = ImageUrl.ToBitmap(MiniPlayerSession.Thumbnail, 128);
            _thumb.Visibility = Visibility.Visible;
            _bar.Visibility = Visibility.Visible;
            _row.Height = new GridLength(56);
            MiniPlayerSession.MarkShown();
            var source = MiniPlayerSession.Source;
            if (source != null)
            {
                if (_player.Source == null || !_player.Source.Equals(source))
                {
                    _player.Source = source;
                }
            }
            UpdatePlayIcon();
        }

        public void Hide()
        {
            _bar.Visibility = Visibility.Collapsed;
            _row.Height = new GridLength(0);
        }

        public void Close()
        {
            StopMedia();
            MiniPlayerSession.Clear();
            Hide();
        }

        public void StopMedia()
        {
            try
            {
                _player.Stop();
                _player.Source = null;
            }
            catch
            {
            }
        }

        public void UpdatePlayIcon()
        {
            var playing = _player.CurrentState == MediaElementState.Playing;
            YtIcons.Set(_playIcon, playing ? "pause.png" : "play.png");
        }

        public void Expand(Page page)
        {
            if (!MiniPlayerSession.IsActive)
            {
                return;
            }

            MiniPlayerSession.UpdatePlayback(
                _player.Source,
                _player.Position.TotalSeconds,
                _player.CurrentState == MediaElementState.Playing);
            StopMedia();
            Hide();
            var mainPage = page as MainPage;
            if (mainPage != null)
            {
                NavigationContext.SetMain(mainPage.CurrentTabIndex);
            }
            else
            {
                var channelPage = page as ChannelPage;
                if (channelPage != null)
                {
                    NavigationContext.SetChannel(channelPage.ChannelId);
                }
                else
                {
                    NavigationContext.SetMain(0);
                }
            }

            page.Frame.Navigate(typeof(VideoPage), MiniPlayerSession.VideoId);
        }

        public void TogglePlayPause()
        {
            if (_player.CurrentState == MediaElementState.Playing)
            {
                _player.Pause();
            }
            else
            {
                _player.Play();
            }

            UpdatePlayIcon();
        }

        private void Player_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (MiniPlayerSession.PositionSeconds > 0)
            {
                _player.Position = System.TimeSpan.FromSeconds(MiniPlayerSession.PositionSeconds);
            }

            _thumb.Visibility = Visibility.Collapsed;
            if (MiniPlayerSession.ShouldPlay)
            {
                _player.Play();
            }

            UpdatePlayIcon();
        }
    }

}
