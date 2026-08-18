using App1.Helpers;
using App1.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace App1
{
    public sealed partial class ChannelPage : Page
    {
        private ChannelItem _channel;
        private readonly ObservableCollection<VideoItem> _videos = new ObservableCollection<VideoItem>();
        private readonly ObservableCollection<VideoItem> _shorts = new ObservableCollection<VideoItem>();
        private readonly ObservableCollection<VideoItem> _forYou = new ObservableCollection<VideoItem>();
        private readonly ObservableCollection<ChannelItem> _featured = new ObservableCollection<ChannelItem>();
        private MiniPlayerUiHelper _miniPlayerUi;
        private bool _descriptionExpanded;
        private int _activeTab;
        private int _channelLoadGeneration;

        public string ChannelId
        {
            get { return _channel != null ? _channel.AuthorId : ""; }
        }

        public ChannelPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            VideosList.ItemsSource = _videos;
            ShortsList.ItemsSource = _shorts;
            ForYouList.ItemsSource = _forYou;
            FeaturedList.ItemsSource = _featured;
            _miniPlayerUi = new MiniPlayerUiHelper(
                MiniPlayer,
                MiniPlayerBar,
                MiniPlayerRow,
                MiniPlayerTitle,
                MiniPlayerAuthor,
                MiniPlayerThumb,
                MiniPlayerPlayIcon);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            _miniPlayerUi.SyncFromSession();
            UpdateBottomNav(-1);

            var channelId = e.Parameter as string;
            if (string.IsNullOrEmpty(channelId))
            {
                ErrorText.Text = "Missing channel.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            if (_channel != null && _channel.AuthorId == channelId && _videos.Count + _shorts.Count > 0)
            {
                return;
            }

            var cached = AppContentCache.GetChannel(channelId);
            if (cached != null && cached.Channel != null)
            {
                ApplyChannelSnapshot(cached);
                return;
            }

            BeginLoadChannel(channelId);
        }

        private void BeginLoadChannel(string channelId)
        {
            _channelLoadGeneration++;
            var gen = _channelLoadGeneration;
            PageLoading.IsActive = true;
            _videos.Clear();
            _shorts.Clear();
            _forYou.Clear();
            _featured.Clear();
            _descriptionExpanded = false;
            SelectTab(0);
            ErrorText.Visibility = Visibility.Collapsed;
            Task.Run(async () => await LoadChannelWorker(channelId, gen));
        }

        private async System.Threading.Tasks.Task LoadChannelWorker(string channelId, int gen)
        {
            try
            {
                var channel = await App.Api.GetChannelAsync(channelId);
                if (gen != _channelLoadGeneration)
                {
                    return;
                }

                await UiDispatcher.RunNormal(() =>
                {
                    if (gen != _channelLoadGeneration)
                    {
                        return;
                    }

                    _channel = channel;
                    NavigationContext.SetChannel(_channel.AuthorId);
                    BindChannelHeader();
                    PageLoading.IsActive = false;
                });

                var videos = await App.Api.GetChannelVideosAsync(channelId);
                if (gen != _channelLoadGeneration)
                {
                    return;
                }

                for (int i = 0; i < videos.Count; i++)
                {
                    if (gen != _channelLoadGeneration)
                    {
                        return;
                    }

                    var video = videos[i];
                    await UiDispatcher.RunLow(() =>
                    {
                        if (gen != _channelLoadGeneration || _channel == null)
                        {
                            return;
                        }

                        if (video.IsShort)
                        {
                            video.IsShort = true;
                            VideoClassifier.EnrichFromChannel(video, _channel);
                            _shorts.Add(video);
                        }
                        else
                        {
                            VideoClassifier.EnrichFromChannel(video, _channel);
                            _videos.Add(video);
                        }
                    });

                    if (i < videos.Count - 1)
                    {
                        await Task.Delay(40);
                    }
                }

                if (gen != _channelLoadGeneration)
                {
                    return;
                }

                Task.Run(async () => await LoadChannelShortsWorker(channelId, gen));

                await UiDispatcher.RunNormal(() =>
                {
                    if (gen != _channelLoadGeneration)
                    {
                        return;
                    }

                    FinalizeChannelLoad();
                });
            }
            catch (Exception ex)
            {
                await UiDispatcher.RunNormal(() =>
                {
                    if (gen != _channelLoadGeneration)
                    {
                        return;
                    }

                    ErrorText.Text = "Couldn't load this channel. " + ex.Message;
                    ErrorText.Visibility = Visibility.Visible;
                    PageLoading.IsActive = false;
                });
            }
        }

        private async System.Threading.Tasks.Task LoadChannelShortsWorker(string channelId, int gen)
        {
            try
            {
                var shorts = await App.Api.GetChannelShortsAsync(channelId);
                if (gen != _channelLoadGeneration)
                {
                    return;
                }

                await UiDispatcher.RunLow(() =>
                {
                    if (gen != _channelLoadGeneration || _channel == null)
                    {
                        return;
                    }

                    foreach (var video in shorts)
                    {
                        var exists = false;
                        foreach (var existing in _shorts)
                        {
                            if (existing.VideoId == video.VideoId)
                            {
                                exists = true;
                                break;
                            }
                        }

                        if (!exists)
                        {
                            video.IsShort = true;
                            VideoClassifier.EnrichFromChannel(video, _channel);
                            _shorts.Add(video);
                        }
                    }

                    TabShorts.Visibility = _shorts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                    if (_shorts.Count == 0 && _activeTab == 2)
                    {
                        SelectTab(0);
                    }

                    SaveChannelSnapshot();
                });
            }
            catch
            {
            }
        }

        private void FinalizeChannelLoad()
        {
            MetaText.Text = FormatUtil.JoinMeta(_channel.SubCountText, _channel.VideoCountText);

            BuildForYou();
            BuildFeatured();
            TabShorts.Visibility = _shorts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_shorts.Count == 0 && _activeTab == 2)
            {
                SelectTab(0);
            }

            SaveChannelSnapshot();
            PageLoading.IsActive = false;
        }

        private void ApplyChannelSnapshot(ChannelSnapshot snapshot)
        {
            _channel = snapshot.Channel;
            NavigationContext.SetChannel(_channel.AuthorId);
            BindChannelHeader();

            _videos.Clear();
            if (snapshot.Videos != null)
            {
                foreach (var video in snapshot.Videos)
                {
                    _videos.Add(video);
                }
            }

            _shorts.Clear();
            if (snapshot.Shorts != null)
            {
                foreach (var video in snapshot.Shorts)
                {
                    _shorts.Add(video);
                }
            }

            ErrorText.Visibility = Visibility.Collapsed;
            PageLoading.IsActive = false;
            BuildForYou();
            BuildFeatured();
            TabShorts.Visibility = _shorts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SaveChannelSnapshot()
        {
            if (_channel == null || string.IsNullOrEmpty(_channel.AuthorId))
            {
                return;
            }

            var videos = new List<VideoItem>();
            foreach (var video in _videos)
            {
                videos.Add(video);
            }

            var shorts = new List<VideoItem>();
            foreach (var video in _shorts)
            {
                shorts.Add(video);
            }

            AppContentCache.PutChannel(_channel.AuthorId, new ChannelSnapshot
            {
                Channel = _channel,
                Videos = videos,
                Shorts = shorts
            });
        }

        private async System.Threading.Tasks.Task LoadChannelAsync(string channelId)
        {
            BeginLoadChannel(channelId);
        }

        private void BindChannelHeader()
        {
            AuthorText.Text = _channel.Author ?? "";
            HandleText.Text = FormatUtil.FormatHandle(_channel.AuthorHandle, _channel.AuthorId);
            MetaText.Text = !string.IsNullOrEmpty(_channel.MetaText)
                ? _channel.MetaText
                : FormatUtil.JoinMeta(_channel.SubCountText, _channel.VideoCountText);
            DescriptionText.Text = _channel.Description ?? "";
            DescriptionMore.Visibility = string.IsNullOrEmpty(_channel.Description)
                ? Visibility.Collapsed
                : Visibility.Visible;
            AuthorBrush.ImageSource = ImageUrl.ToBitmap(_channel.AuthorThumbnail, 176);
            BannerImage.Source = ImageUrl.ToBitmap(_channel.AuthorBanner, 640);
            UpdateSubscribeChrome();
        }

        private void BuildForYou()
        {
            _forYou.Clear();
            var count = 0;
            foreach (var video in _videos)
            {
                if (count >= 10)
                {
                    break;
                }

                _forYou.Add(video);
                count++;
            }
        }

        private void BuildFeatured()
        {
            _featured.Clear();
            foreach (var entry in App.Library.Subscriptions)
            {
                var channel = App.Library.ToChannelItem(entry);
                if (channel == null || string.IsNullOrEmpty(channel.AuthorId))
                {
                    continue;
                }

                if (channel.AuthorId == _channel.AuthorId)
                {
                    continue;
                }

                _featured.Add(channel);
            }

            var show = _featured.Count > 0;
            FeaturedHeaderRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            FeaturedList.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateSubscribeChrome()
        {
            var subscribed = _channel != null && App.Library.IsSubscribed(_channel.AuthorId);
            SubscribeLabel.Text = subscribed ? "Subscribed" : "Subscribe";
            SubscribeButton.Background = subscribed
                ? new SolidColorBrush(Color.FromArgb(255, 58, 58, 58))
                : new SolidColorBrush(Colors.White);
            SubscribeButton.Foreground = subscribed
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.Black);
            SubscribeBellIcon.Visibility = subscribed ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SelectTab(int index)
        {
            _activeTab = index;
            HomeTabContent.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            VideosTabContent.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            ShortsTabContent.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
            PostsTabContent.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;

            SetTabChrome(TabHomeLabel, TabHomeUnderline, index == 0);
            SetTabChrome(TabVideosLabel, TabVideosUnderline, index == 1);
            SetTabChrome(TabShortsLabel, TabShortsUnderline, index == 2);
            SetTabChrome(TabPostsLabel, TabPostsUnderline, index == 3);

            if (index == 3)
            {
                LoadPostsAsync();
            }
        }

        private static void SetTabChrome(TextBlock label, Border underline, bool active)
        {
            label.Foreground = new SolidColorBrush(active ? Colors.White : Color.FromArgb(255, 138, 138, 138));
            label.FontWeight = active ? Windows.UI.Text.FontWeights.SemiBold : Windows.UI.Text.FontWeights.Normal;
            underline.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateBottomNav(int selectedTab)
        {
            SetNav(NavHomeIcon, NavHomeLabel, "home", selectedTab == 0);
            SetNav(NavShortsIcon, NavShortsLabel, "shorts", selectedTab == 1);
            SetNav(NavSubsIcon, NavSubsLabel, "subscriptions", selectedTab == 2);
            SetNav(NavYouIcon, NavYouLabel, "library", selectedTab == 3);
        }

        private static void SetNav(Image icon, TextBlock label, string name, bool selected)
        {
            YtIcons.SetToggle(icon, name, selected);
            label.Foreground = new SolidColorBrush(selected ? Colors.White : Color.FromArgb(255, 144, 144, 144));
        }

        public void HandleHardwareBack()
        {
            if (Frame != null && Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            HandleHardwareBack();
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            AppNavigation.GoMain(Frame, 0);
        }

        private void More_Click(object sender, RoutedEventArgs e)
        {
            AppNavigation.GoSettings(this);
        }

        private void DescriptionMore_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ShowInfoOverlay("About", _channel != null ? _channel.Description : "");
        }

        private void ChannelInfo_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (_channel == null)
            {
                return;
            }

            var body = (_channel.Description ?? "") + "\n\n" + (_channel.MetaText ?? "");
            ShowInfoOverlay(_channel.Author ?? "Channel", body.Trim());
        }

        private bool _headerCollapsed;

        private void ChannelContentScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (ChannelContentScroll == null || ChannelHeaderPanel == null)
            {
                return;
            }

            var collapsed = ChannelContentScroll.VerticalOffset > 72;
            if (collapsed == _headerCollapsed)
            {
                return;
            }

            _headerCollapsed = collapsed;
            if (collapsed)
            {
                AnimationHelper.Hide(ChannelHeaderPanel, 150);
            }
            else
            {
                ChannelHeaderPanel.Visibility = Visibility.Visible;
                AnimationHelper.Show(ChannelHeaderPanel, 150);
            }
        }

        private async void LoadPostsAsync()
        {
            if (_channel == null || string.IsNullOrEmpty(_channel.AuthorId) || PostsList.ItemsSource != null)
            {
                return;
            }

            PostsLoading.IsActive = true;
            PostsEmpty.Visibility = Visibility.Collapsed;
            try
            {
                var posts = await App.Api.GetCommunityPostsForChannelsAsync(new List<string> { _channel.AuthorId }, 20);
                var feed = new List<FeedItem>();
                foreach (var post in posts)
                {
                    feed.Add(FeedItem.FromCommunity(post));
                }

                PostsList.ItemsSource = feed;
                PostsEmpty.Visibility = feed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                PostsEmpty.Visibility = Visibility.Visible;
            }
            finally
            {
                PostsLoading.IsActive = false;
            }
        }

        private void TabPosts_Tapped(object sender, TappedRoutedEventArgs e)
        {
            SelectTab(3);
        }

        private void ShowInfoOverlay(string title, string body)
        {
            InfoOverlayTitle.Text = title ?? "";
            InfoOverlayBody.Text = body ?? "";
            InfoOverlay.Visibility = Visibility.Visible;
            AnimationHelper.Show(InfoOverlay, 180);
        }

        private void InfoOverlayClose_Click(object sender, RoutedEventArgs e)
        {
            AnimationHelper.Hide(InfoOverlay, 150);
        }

        private void InfoOverlayBackground_Tapped(object sender, TappedRoutedEventArgs e)
        {
            InfoOverlayClose_Click(sender, null);
        }

        private async void Subscribe_Click(object sender, RoutedEventArgs e)
        {
            if (_channel == null)
            {
                return;
            }

            await App.Library.ToggleSubscriptionAsync(_channel);
            UpdateSubscribeChrome();
        }

        private void Join_Click(object sender, RoutedEventArgs e)
        {
        }

        private void TabHome_Tapped(object sender, TappedRoutedEventArgs e)
        {
            SelectTab(0);
        }

        private void TabVideos_Tapped(object sender, TappedRoutedEventArgs e)
        {
            SelectTab(1);
        }

        private void TabShorts_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (_shorts.Count > 0)
            {
                SelectTab(2);
            }
        }

        private void Video_ItemClick(object sender, ItemClickEventArgs e)
        {
            var video = e.ClickedItem as VideoItem;
            if (video != null && !string.IsNullOrEmpty(video.VideoId))
            {
                AppNavigation.OpenVideoFromChannel(this, video.VideoId);
            }
        }

        private void Featured_ItemClick(object sender, ItemClickEventArgs e)
        {
            var channel = e.ClickedItem as ChannelItem;
            if (channel != null && !string.IsNullOrEmpty(channel.AuthorId))
            {
                AppNavigation.GoChannel(this, channel.AuthorId);
            }
        }

        private void NavHome_Tapped(object sender, TappedRoutedEventArgs e)
        {
            AppNavigation.GoMain(Frame, 0);
        }

        private void NavShorts_Tapped(object sender, TappedRoutedEventArgs e)
        {
            AppNavigation.GoMain(Frame, 1);
        }

        private void NavSubs_Tapped(object sender, TappedRoutedEventArgs e)
        {
            AppNavigation.GoMain(Frame, 2);
        }

        private void NavYou_Tapped(object sender, TappedRoutedEventArgs e)
        {
            AppNavigation.GoMain(Frame, 3);
        }

        private void MiniPlayerExpand_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _miniPlayerUi.Expand(this);
        }

        private void MiniPlayerPlayPause_Click(object sender, RoutedEventArgs e)
        {
            _miniPlayerUi.TogglePlayPause();
        }

        private void MiniPlayerClose_Click(object sender, RoutedEventArgs e)
        {
            _miniPlayerUi.Close();
        }
    }
}
