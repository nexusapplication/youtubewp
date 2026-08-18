using App1.Helpers;
using App1.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

namespace App1
{
    public sealed partial class MainPage : Page
    {
        private bool _updatingNav;
        private bool _searchOpen;
        private bool _loaded;
        private int _backendGeneration = -1;
        private int _shortIndex;
        private int _shortStreamAttempt;
        private bool _shortsMuted;
        private bool _shortLiked;
        private bool _shortDisliked;
        private bool _shortsDragging;
        private double _shortsDragY;
        private bool _shortSuppressTap;
        private bool _shortsStreamStarted;
        private bool _shareHooked;
        private string _loadingShortId;
        private VideoDetails _shortDetails;
        private readonly Dictionary<string, ShortPreloadEntry> _shortPreload = new Dictionary<string, ShortPreloadEntry>();
        private bool _homeLoadingMore;
        private bool _homeHasMore = true;
        private int _homeLoadGeneration;
        private const int HomeRevealBatchSize = 10;
        private const int HomeRevealDelayMs = 120;
        private int _shortPlayGeneration;
        private ScrollViewer _homeScrollViewer;
        private bool _shortsLoaded;
        private bool _subsLoaded;
        private bool _historyLoaded;
        private const int ShortsPreloadAhead = 1;
        private const int ShortPreloadMaxEntries = 4;
        private Storyboard _shortsSlideStoryboard;

        private sealed class ShortPreloadEntry
        {
            public VideoDetails Details;
            public bool IsLoading;
        }

        public ObservableCollection<FeedItem> HomeFeed { get; private set; }
        public ObservableCollection<VideoItem> HomeVideos { get; private set; }
        public ObservableCollection<VideoItem> ShortsVideos { get; private set; }
        public ObservableCollection<VideoItem> SubscriptionVideos { get; private set; }
        public ObservableCollection<VideoItem> HistoryVideos { get; private set; }
        public ObservableCollection<VideoItem> SearchVideos { get; private set; }

        public MainPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            HomeFeed = new ObservableCollection<FeedItem>();
            HomeVideos = new ObservableCollection<VideoItem>();
            ShortsVideos = new ObservableCollection<VideoItem>();
            SubscriptionVideos = new ObservableCollection<VideoItem>();
            HistoryVideos = new ObservableCollection<VideoItem>();
            SearchVideos = new ObservableCollection<VideoItem>();
            DataContext = this;
            Loaded += MainPage_Loaded;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            HidePivotHeaders(MainPivot);
            ClipPivotHost();
            PivotHost.SizeChanged += PivotHost_SizeChanged;
            if (Frame != null)
            {
                Frame.Navigated += Frame_Navigated;
            }

            SyncMiniPlayerFromSession();
        }

        private void Frame_Navigated(object sender, NavigationEventArgs e)
        {
            if (e.Content == this)
            {
                SyncMiniPlayerFromSession();
            }
        }

        private void PivotHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ClipPivotHost();
            HidePivotHeaders(MainPivot);
        }

        private void ClipPivotHost()
        {
            if (PivotHost.ActualWidth <= 0 || PivotHost.ActualHeight <= 0)
            {
                return;
            }

            PivotHost.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, PivotHost.ActualWidth, PivotHost.ActualHeight)
            };
        }

        private static void HidePivotHeaders(DependencyObject parent)
        {
            if (parent == null)
            {
                return;
            }

            var count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var element = child as FrameworkElement;
                if (element != null)
                {
                    var name = element.Name ?? "";
                    var typeName = element.GetType().Name;
                    if (name == "HeaderClipper" || name == "HeaderWrapper" ||
                        typeName.IndexOf("PivotHeader", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try
                        {
                            element.Visibility = Visibility.Collapsed;
                            element.Height = 0;
                            element.MaxHeight = 0;
                            element.Opacity = 0;
                            element.IsHitTestVisible = false;
                        }
                        catch
                        {
                        }
                    }
                }

                HidePivotHeaders(child);
            }
        }

        public int CurrentTabIndex
        {
            get { return MainPivot.SelectedIndex; }
        }

        public void SelectMainTab(int index)
        {
            SelectTab(index, false);
        }

        public bool HandleBack()
        {
            if (MiniPlayerBar.Visibility == Visibility.Visible)
            {
                CloseMiniPlayer();
                return true;
            }

            if (ShortsCommentsPanel.Visibility == Visibility.Visible)
            {
                ShortsCommentsPanel.Visibility = Visibility.Collapsed;
                return true;
            }

            if (_searchOpen)
            {
                CloseSearch();
                return true;
            }

            return false;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.Parameter is int)
            {
                SelectMainTab((int)e.Parameter);
            }

            if (_backendGeneration != App.Api.Generation)
            {
                _backendGeneration = App.Api.Generation;
                _loaded = false;
                _shortsLoaded = false;
                _subsLoaded = false;
                _historyLoaded = false;
            }

            InstanceLabel.Text = "Source: " + App.Api.StatusLabel;
            if (_loaded)
            {
                if (MainPivot.SelectedIndex == 1)
                {
                    PlayCurrentShort();
                }

                return;
            }

            _loaded = true;
            BeginHomeLoad(true);
            if (MainPivot.SelectedIndex == 1)
            {
                EnsureShortsLoadedAsync();
                PlayCurrentShort();
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            StopShortsPlayer();
        }

        private void BeginHomeLoad(bool reset)
        {
            if (reset)
            {
                _homeLoadGeneration++;
            }
            else if (_homeLoadingMore || !_homeHasMore)
            {
                return;
            }

            var generation = _homeLoadGeneration;
            Task.Run(async () => await HomeLoadPipelineAsync(reset, generation));
        }

        private async Task HomeLoadPipelineAsync(bool reset, int generation)
        {
            if (_homeLoadingMore && !reset)
            {
                return;
            }

            _homeLoadingMore = true;

            if (reset)
            {
                await UiDispatcher.RunNormal(() =>
                {
                    App.Api.ResetHomeFeed();
                    HomeFeed.Clear();
                    _homeHasMore = true;
                    HomeEmpty.Text = "Couldn't load the Home feed. Pull the Home tab again or pick another instance in Settings.";
                    ShowHomePlaceholders();
                });
            }

            if (!_homeHasMore)
            {
                _homeLoadingMore = false;
                return;
            }

            await UiDispatcher.RunNormal(() => HomeEmpty.Visibility = Visibility.Collapsed);

            var revealed = 0;
            var addedAny = false;

            try
            {
                while (generation == _homeLoadGeneration
                    && revealed < HomeRevealBatchSize
                    && _homeHasMore)
                {
                    FeedItem item = null;
                    try
                    {
                        item = await App.Api.LoadHomeFeedNextItemAsync();
                    }
                    catch (Exception ex)
                    {
                        if (!addedAny && generation == _homeLoadGeneration)
                        {
                            await UiDispatcher.RunNormal(() =>
                            {
                                if (HomeFeed.Count == 0)
                                {
                                    HomeEmpty.Text = "Couldn't load the Home feed. " + ex.Message;
                                    HomeEmpty.Visibility = Visibility.Visible;
                                }
                            });
                        }

                        break;
                    }

                    if (item == null)
                    {
                        _homeHasMore = false;
                        break;
                    }

                    if (generation != _homeLoadGeneration)
                    {
                        break;
                    }

                    await UiDispatcher.RunLow(() =>
                    {
                        ClearFeedPlaceholders();
                        HomeFeed.Add(item);
                    });

                    if (item.Kind == FeedItemKind.Video && item.Video != null)
                    {
                        var video = item.Video;
                        Task.Run(async () =>
                        {
                            try
                            {
                                await App.Api.EnrichSingleVideoAsync(video);
                            }
                            catch
                            {
                            }
                        });
                    }

                    addedAny = true;
                    revealed++;
                    await Task.Delay(HomeRevealDelayMs);
                }

                if (generation == _homeLoadGeneration)
                {
                    await UiDispatcher.RunLow(() =>
                    {
                        ClearFeedPlaceholders();
                        HomeEmpty.Visibility = HomeFeed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    });
                }
            }
            finally
            {
                if (generation == _homeLoadGeneration)
                {
                    _homeLoadingMore = false;
                }
            }
        }

        private void ShowHomePlaceholders()
        {
            ClearFeedPlaceholders();
            HomeFeed.Add(FeedItem.Placeholder("home0"));
        }

        private void ClearFeedPlaceholders()
        {
            for (int i = HomeFeed.Count - 1; i >= 0; i--)
            {
                if (HomeFeed[i].Kind == FeedItemKind.Placeholder)
                {
                    HomeFeed.RemoveAt(i);
                }
            }
        }

        private void SetShortsShimmer(bool visible)
        {
            ShortsShimmer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HomeList_Loaded(object sender, RoutedEventArgs e)
        {
            _homeScrollViewer = FindScrollViewer(HomeList);
            if (_homeScrollViewer != null)
            {
                _homeScrollViewer.ViewChanged -= HomeScroll_ViewChanged;
                _homeScrollViewer.ViewChanged += HomeScroll_ViewChanged;
            }
        }

        private void HomeScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_homeScrollViewer == null || _homeLoadingMore || !_homeHasMore)
            {
                return;
            }

            var vertical = _homeScrollViewer.VerticalOffset;
            var max = _homeScrollViewer.ScrollableHeight;
            if (max <= 0 || vertical < max - 120)
            {
                return;
            }

            BeginHomeLoad(false);
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null)
            {
                return null;
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var scroll = child as ScrollViewer;
                if (scroll != null)
                {
                    return scroll;
                }

                var nested = FindScrollViewer(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private async Task EnsureShortsLoadedAsync()
        {
            if (_shortsLoaded)
            {
                return;
            }

            await LoadShortsAsync();
            _shortsLoaded = true;
        }

        private async Task EnsureSubsLoadedAsync()
        {
            if (_subsLoaded)
            {
                return;
            }

            await LoadSubscriptionsAsync();
            _subsLoaded = true;
        }

        private async Task LoadShortsAsync()
        {
            var cachedList = App.Api.TryGetCachedShorts();
            if (cachedList != null && cachedList.Count > 0 && ShortsVideos.Count == 0)
            {
                PopulateShortsVideos(cachedList);
            }

            SetShortsShimmer(ShortsVideos.Count == 0);
            ShortsEmpty.Visibility = Visibility.Collapsed;
            try
            {
                var videos = await App.Api.GetShortsAsync();
                if (videos == null || videos.Count == 0)
                {
                    ShortsEmpty.Visibility = ShortsVideos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    return;
                }

                ClearShortPreload();
                ShortsVideos.Clear();
                PopulateShortsVideos(videos);

                ShortsEmpty.Visibility = ShortsVideos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (MainPivot.SelectedIndex == 1 && ShortsVideos.Count > 0)
                {
                    _shortIndex = 0;
                    PlayCurrentShort();
                }
            }
            catch (Exception ex)
            {
                if (ShortsVideos.Count == 0)
                {
                    ShortsEmpty.Text = "No Shorts found right now. " + ex.Message;
                    ShortsEmpty.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                SetShortsShimmer(false);
            }
        }

        private void PopulateShortsVideos(List<VideoItem> videos)
        {
            var height = Math.Max(420, Window.Current.Bounds.Height - 104);
            foreach (var video in videos)
            {
                video.IsShort = true;
                video.CardHeight = height;
                ShortsVideos.Add(video);
            }
        }

        private async Task LoadSubscriptionsAsync()
        {
            SubsLoading.IsActive = true;
            SubsEmpty.Visibility = Visibility.Collapsed;
            SubscriptionVideos.Clear();
            try
            {
                await App.Library.SyncSubscriptionsFromApiAsync();
                var subs = App.Library.Subscriptions;
                if (subs.Count == 0)
                {
                    SubsEmpty.Visibility = Visibility.Visible;
                    return;
                }

                var height = Math.Max(420, Window.Current.Bounds.Height - 104);
                var maxChannels = 8;
                var loaded = 0;
                foreach (var entry in subs)
                {
                    if (loaded >= maxChannels)
                    {
                        break;
                    }

                    loaded++;
                    var channel = App.Library.ToChannelItem(entry);
                    var videos = await App.Api.GetChannelVideosAsync(channel.AuthorId);
                    foreach (var video in videos)
                    {
                        if (video.IsShort)
                        {
                            AddShort(video, height);
                        }
                        else
                        {
                            SubscriptionVideos.Add(video);
                        }
                    }
                }

                SubsEmpty.Visibility = SubscriptionVideos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                SubsEmpty.Visibility = SubscriptionVideos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                SubsLoading.IsActive = false;
            }
        }

        private void AddShort(VideoItem video, double height)
        {
            foreach (var existing in ShortsVideos)
            {
                if (existing.VideoId == video.VideoId)
                {
                    return;
                }
            }

            video.IsShort = true;
            video.CardHeight = height;
            ShortsVideos.Insert(0, video);
            ShortsEmpty.Visibility = Visibility.Collapsed;
        }

        private async Task EnsureHistoryLoadedAsync()
        {
            if (_historyLoaded)
            {
                return;
            }

            await RefreshHistoryAsync();
            _historyLoaded = true;
        }

        private async Task RefreshHistoryAsync()
        {
            HistoryVideos.Clear();
            if (App.Api.GoogleAuth.IsSignedIn)
            {
                try
                {
                    var remote = await App.Api.DataApi.GetHistoryVideosAsync(50);
                    foreach (var video in remote)
                    {
                        if (!video.IsShort && !string.IsNullOrEmpty(video.VideoId))
                        {
                            HistoryVideos.Add(video);
                        }
                    }
                }
                catch
                {
                }
            }

            if (HistoryVideos.Count == 0)
            {
                foreach (var entry in App.Library.History)
                {
                    var item = App.Library.ToVideoItem(entry);
                    if (!item.IsShort)
                    {
                        HistoryVideos.Add(item);
                    }
                }
            }

            HistoryEmpty.Visibility = HistoryVideos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            InstanceLabel.Text = App.Api.GoogleAuth.IsSignedIn
                ? "Signed in with Google · " + App.Api.StatusLabel
                : "Source: " + App.Api.StatusLabel;

            await RefreshPlaylistsAsync();
        }

        private async Task RefreshPlaylistsAsync()
        {
            PlaylistsList.ItemsSource = null;
            if (!App.Api.GoogleAuth.IsSignedIn)
            {
                PlaylistsEmpty.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                var playlists = await App.Api.DataApi.GetPlaylistsAsync(25);
                PlaylistsList.ItemsSource = playlists;
                PlaylistsEmpty.Visibility = playlists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                PlaylistsEmpty.Visibility = Visibility.Visible;
            }
        }

        private async void Feed_ItemClick(object sender, ItemClickEventArgs e)
        {
            var feed = e.ClickedItem as FeedItem;
            if (feed == null || feed.Kind != FeedItemKind.Video || feed.Video == null)
            {
                return;
            }

            OpenVideoItem(feed.Video);
        }

        private void ShortShelfItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var border = sender as Border;
            var video = border != null ? border.DataContext as VideoItem : null;
            if (video == null || string.IsNullOrEmpty(video.VideoId))
            {
                return;
            }

            NavigationContext.SetMain(MainPivot.SelectedIndex);
            CloseMiniPlayer();
            App.Api.PrefetchVideoPlayback(video.VideoId);
            AppNavigation.OpenVideo(this, video.VideoId);
        }

        private void Playlist_ItemClick(object sender, ItemClickEventArgs e)
        {
            var playlist = e.ClickedItem as Services.PlaylistSummary;
            if (playlist == null || string.IsNullOrEmpty(playlist.PlaylistId))
            {
                return;
            }

            AppNavigation.OpenPlaylist(this, playlist);
        }

        private void OpenVideoItem(VideoItem video)
        {
            if (video == null)
            {
                return;
            }

            if (video.IsAd)
            {
                if (!string.IsNullOrEmpty(video.ClickUrl))
                {
                    var url = video.ClickUrl;
                    if (url.StartsWith("/"))
                    {
                        url = "https://www.youtube.com" + url;
                    }

                    Launcher.LaunchUriAsync(new Uri(url));
                }
                else if (!string.IsNullOrEmpty(video.VideoId))
                {
                    NavigationContext.SetMain(MainPivot.SelectedIndex);
                    App.Api.PrefetchVideoPlayback(video.VideoId);
                    AppNavigation.OpenVideo(this, video.VideoId);
                }

                return;
            }

            if (string.IsNullOrEmpty(video.VideoId))
            {
                return;
            }

            NavigationContext.SetMain(MainPivot.SelectedIndex);
            CloseMiniPlayer();
            App.Api.PrefetchVideoPlayback(video.VideoId);
            AppNavigation.OpenVideo(this, video.VideoId);
        }

        private async Task OpenVideoItemAsync(VideoItem video)
        {
            OpenVideoItem(video);
            await Task.FromResult(0);
        }

        private async void Video_ItemClick(object sender, ItemClickEventArgs e)
        {
            await OpenVideoItemAsync(e.ClickedItem as VideoItem);
        }

        private void SearchToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_searchOpen && SearchBar.Visibility == Visibility.Visible)
            {
                CloseSearch();
                return;
            }

            SearchBar.Visibility = Visibility.Visible;
            SearchBox.Focus(FocusState.Programmatic);
        }

        private async void SearchGo_Click(object sender, RoutedEventArgs e)
        {
            await RunSearchAsync();
        }

        private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                await RunSearchAsync();
            }
        }

        private async Task RunSearchAsync()
        {
            var query = SearchBox.Text;
            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            _searchOpen = true;
            SearchOverlay.Visibility = Visibility.Visible;
            SearchLoading.IsActive = true;
            SearchEmpty.Visibility = Visibility.Collapsed;
            SearchVideos.Clear();
            try
            {
                var results = await App.Api.SearchVideosAsync(query.Trim(), false);
                var height = Math.Max(420, Window.Current.Bounds.Height - 104);
                foreach (var video in results)
                {
                    if (video.IsShort)
                    {
                        AddShort(video, height);
                    }
                    else
                    {
                        SearchVideos.Add(video);
                    }
                }

                SearchEmpty.Visibility = SearchVideos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                SearchEmpty.Text = "Search failed. " + ex.Message;
                SearchEmpty.Visibility = Visibility.Visible;
            }
            finally
            {
                SearchLoading.IsActive = false;
            }
        }

        private void CloseSearch()
        {
            _searchOpen = false;
            SearchOverlay.Visibility = Visibility.Collapsed;
            SearchBar.Visibility = Visibility.Collapsed;
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            AppNavigation.GoSettings(this);
        }

        private void Settings_Tapped(object sender, TappedRoutedEventArgs e)
        {
            AppNavigation.GoSettings(this);
        }

        private void NavHome_Tapped(object sender, TappedRoutedEventArgs e)
        {
            SelectTab(0, true);
        }

        private void NavShorts_Tapped(object sender, TappedRoutedEventArgs e)
        {
            SelectTab(1, true);
        }

        private void NavSubs_Tapped(object sender, TappedRoutedEventArgs e)
        {
            SelectTab(2, true);
        }

        private void NavYou_Tapped(object sender, TappedRoutedEventArgs e)
        {
            SelectTab(3, true);
        }

        private void SelectTab(int index, bool fromNav)
        {
            CloseSearch();
            if (fromNav && MainPivot.SelectedIndex == index)
            {
                return;
            }

            _updatingNav = true;
            MainPivot.SelectedIndex = index;
            _updatingNav = false;
            UpdateNav(index);
            if (index == 1)
            {
                PlayCurrentShort();
            }
            else
            {
                StopShortsPlayer();
            }
            if (index == 3)
            {
                EnsureHistoryLoadedAsync();
            }
        }

        private void MainPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingNav)
            {
                return;
            }

            UpdateNav(MainPivot.SelectedIndex);
            if (MainPivot.SelectedIndex == 1)
            {
                EnsureShortsLoadedAsync();
                PlayCurrentShort();
            }
            else
            {
                StopShortsPlayer();
            }

            if (MainPivot.SelectedIndex == 2)
            {
                EnsureSubsLoadedAsync();
            }

            if (MainPivot.SelectedIndex == 3)
            {
                EnsureHistoryLoadedAsync();
            }
        }

        private void UpdateNav(int index)
        {
            SetNav(NavHomeIcon, NavHomeLabel, "home", index == 0);
            SetNav(NavShortsIcon, NavShortsLabel, "shorts", index == 1);
            SetNav(NavSubsIcon, NavSubsLabel, "subscriptions", index == 2);
            SetNav(NavYouIcon, NavYouLabel, "library", index == 3);
            HeaderRow.Height = index == 1 ? new GridLength(0) : new GridLength(52);
            HeaderBar.Visibility = index == 1 ? Visibility.Collapsed : Visibility.Visible;
            if (index != 1)
            {
                ShortsCommentsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private VideoItem CurrentShort()
        {
            if (_shortIndex < 0 || _shortIndex >= ShortsVideos.Count)
            {
                return null;
            }

            return ShortsVideos[_shortIndex];
        }

        private void PlayCurrentShort()
        {
            var item = CurrentShort();
            if (MainPivot.SelectedIndex != 1 || item == null)
            {
                StopShortsPlayer();
                return;
            }

            var gen = ++_shortPlayGeneration;
            var videoId = item.VideoId;

            ShortsCommentsPanel.Visibility = Visibility.Collapsed;
            ShortsEmpty.Visibility = Visibility.Collapsed;
            ShortsPlayGlyph.Visibility = Visibility.Collapsed;
            _shortLiked = false;
            _shortDisliked = false;
            _shortStreamAttempt = 0;
            _shortsStreamStarted = false;
            _loadingShortId = videoId;
            _shortDetails = null;
            BindShortsChrome(item, null);

            var thumbUrl = item.Thumbnail;
            UiDispatcher.RunLow(() => ShortsThumb.Source = ImageUrl.ToBitmap(thumbUrl, 240));
            ShortsThumb.Visibility = Visibility.Visible;

            ShortsPlayer.Stop();
            ShortsPlayer.Source = null;
            ShortsPlayer.IsMuted = _shortsMuted;
            UpdateMuteChrome();

            var cached = GetShortPreload(videoId);
            if (cached != null)
            {
                ApplyShortPlayback(item, cached, gen);
                return;
            }

            SetShortsShimmer(true);
            Task.Run(async () => await LoadShortPlaybackAsync(item, videoId, gen));
        }

        private async Task LoadShortPlaybackAsync(VideoItem item, string videoId, int gen)
        {
            try
            {
                var details = await App.Api.GetVideoPlaybackAsync(videoId);
                if (gen != _shortPlayGeneration || _loadingShortId != videoId)
                {
                    return;
                }

                StoreShortPreload(videoId, details);
                await UiDispatcher.RunNormal(() =>
                {
                    if (gen != _shortPlayGeneration || _loadingShortId != videoId)
                    {
                        return;
                    }

                    ApplyShortPlayback(item, details, gen);
                });
            }
            catch
            {
                await UiDispatcher.RunNormal(() =>
                {
                    if (gen != _shortPlayGeneration || _loadingShortId != videoId)
                    {
                        return;
                    }

                    SetShortsShimmer(false);
                    ShortsEmpty.Text = "This Short couldn't be loaded.";
                    ShortsEmpty.Visibility = Visibility.Visible;
                });
            }
        }

        private void ApplyShortPlayback(VideoItem item, VideoDetails details, int gen)
        {
            if (gen != _shortPlayGeneration || item == null || details == null)
            {
                return;
            }

            if (_loadingShortId != item.VideoId)
            {
                return;
            }

            _shortDetails = details;
            BindShortsChrome(item, details);
            QueueShortHistory(details);
            StartShortsStream();
            ScheduleShortsPreloadAfterPlay(_shortIndex);
            var enrichId = item.VideoId;
            Task.Run(async () => await EnrichShortDetailsWorker(enrichId, gen));
        }

        private async Task EnrichShortDetailsWorker(string videoId, int gen)
        {
            if (string.IsNullOrEmpty(videoId))
            {
                return;
            }

            try
            {
                var full = await App.Api.GetVideoAsync(videoId);
                if (gen != _shortPlayGeneration || full == null || _loadingShortId != videoId)
                {
                    return;
                }

                await UiDispatcher.RunLow(() =>
                {
                    if (gen != _shortPlayGeneration || _loadingShortId != videoId)
                    {
                        return;
                    }

                    _shortDetails = full;
                    var item = CurrentShort();
                    if (item != null && item.VideoId == videoId)
                    {
                        BindShortsChrome(item, full);
                    }
                });
            }
            catch
            {
            }
        }

        private void QueueShortHistory(VideoDetails details)
        {
            if (details == null)
            {
                return;
            }

            try
            {
                App.Library.AddHistoryAsync(details);
            }
            catch
            {
            }
        }

        private VideoDetails GetShortPreload(string videoId)
        {
            if (string.IsNullOrEmpty(videoId) || !_shortPreload.ContainsKey(videoId))
            {
                return null;
            }

            var entry = _shortPreload[videoId];
            if (entry == null || entry.IsLoading || entry.Details == null)
            {
                return null;
            }

            return entry.Details;
        }

        private void StoreShortPreload(string videoId, VideoDetails details)
        {
            if (string.IsNullOrEmpty(videoId) || details == null)
            {
                return;
            }

            _shortPreload[videoId] = new ShortPreloadEntry { Details = details, IsLoading = false };
            TrimShortPreload();
        }

        private void TrimShortPreload()
        {
            while (_shortPreload.Count > ShortPreloadMaxEntries)
            {
                string oldestKey = null;
                foreach (var key in _shortPreload.Keys)
                {
                    oldestKey = key;
                    break;
                }

                if (oldestKey != null)
                {
                    _shortPreload.Remove(oldestKey);
                }
                else
                {
                    break;
                }
            }
        }

        private void ClearShortPreload()
        {
            _shortPreload.Clear();
        }

        private void ScheduleShortsPreloadAfterPlay(int currentIndex)
        {
            for (int offset = 1; offset <= ShortsPreloadAhead; offset++)
            {
                var idx = currentIndex + offset;
                if (idx >= ShortsVideos.Count)
                {
                    break;
                }

                var id = ShortsVideos[idx].VideoId;
                if (string.IsNullOrEmpty(id) || _shortPreload.ContainsKey(id))
                {
                    continue;
                }

                PreloadShortAsync(id);
            }
        }

        private void PreloadShortAsync(string videoId)
        {
            if (string.IsNullOrEmpty(videoId) || _shortPreload.ContainsKey(videoId))
            {
                return;
            }

            _shortPreload[videoId] = new ShortPreloadEntry { IsLoading = true };
            Task.Run(async () => await PreloadShortWorker(videoId));
        }

        private async Task PreloadShortWorker(string videoId)
        {
            try
            {
                var details = await App.Api.GetVideoPlaybackAsync(videoId);
                if (details != null)
                {
                    _shortPreload[videoId] = new ShortPreloadEntry { Details = details, IsLoading = false };
                    TrimShortPreload();
                }
                else
                {
                    _shortPreload.Remove(videoId);
                }
            }
            catch
            {
                _shortPreload.Remove(videoId);
            }
        }

        private void BindShortsChrome(VideoItem item, VideoDetails details)
        {
            var title = FirstNonEmpty(details != null ? details.Title : null, item.Title);
            var author = FirstNonEmpty(details != null ? details.Author : null, item.Author);
            if (string.IsNullOrEmpty(title))
            {
                title = author;
            }

            SetOverlayText(ShortsTitleText, ShortsTitleShadow, title ?? "");
            SetOverlayText(ShortsAuthorText, ShortsAuthorShadow, author ?? "");
            var views = FirstNonEmpty(details != null ? details.ViewCountText : null, item.ViewCountText);
            SetOverlayText(ShortsMetaText, ShortsMetaShadow, views ?? "");
            SetOverlayText(ShortsLikeLabel, ShortsLikeLabelShadow,
                details != null && !string.IsNullOrEmpty(details.LikeText) ? details.LikeText : "Like");
            SetOverlayText(ShortsDislikeLabel, ShortsDislikeLabelShadow,
                details != null ? details.DislikeText : "Dislike");
            var commentLabel = "Chat";
            if (details != null && details.CommentCount > 0)
            {
                commentLabel = FormatUtil.Compact(details.CommentCount);
            }

            SetOverlayText(ShortsCommentLabel, ShortsCommentLabelShadow, commentLabel);
            UpdateShortsSubscribeChrome(FirstNonEmpty(details != null ? details.AuthorId : null, item.AuthorId));
            YtIcons.SetToggle(ShortsLikeIcon, "like", _shortLiked);
            YtIcons.SetToggle(ShortsDislikeIcon, "dislike", _shortDisliked);
            var avatar = FirstNonEmpty(details != null ? details.AuthorThumbnail : null, item.AuthorThumbnail);
            var avatarUrl = avatar;
            UiDispatcher.RunLow(() => ShortsAuthorBrush.ImageSource = ImageUrl.ToBitmap(avatarUrl, 72));
        }

        private static string FirstNonEmpty(string a, string b)
        {
            if (!string.IsNullOrEmpty(a))
            {
                return a;
            }

            return b ?? "";
        }

        private void SetOverlayText(TextBlock main, TextBlock shadow, string text)
        {
            var value = text ?? "";
            if (main.Text != value)
            {
                main.Text = value;
            }

            if (shadow != null && shadow.Text != value)
            {
                shadow.Text = value;
            }
        }

        private void UpdateShortsSubscribeChrome(string authorId)
        {
            var subscribed = App.Library.IsSubscribed(authorId);
            ShortsSubscribeButton.Content = subscribed ? "Subscribed" : "Subscribe";
            if (subscribed)
            {
                ShortsSubscribeButton.Background = new SolidColorBrush(Color.FromArgb(255, 58, 58, 58));
                ShortsSubscribeButton.Foreground = new SolidColorBrush(Colors.White);
            }
            else
            {
                ShortsSubscribeButton.Background = new SolidColorBrush(Colors.White);
                ShortsSubscribeButton.Foreground = new SolidColorBrush(Colors.Black);
            }
        }

        private void StartShortsStream()
        {
            var urls = PlaybackUrls();
            if (urls == null || _shortStreamAttempt >= urls.Count)
            {
                SetShortsShimmer(false);
                ShortsEmpty.Text = "This Short couldn't be played.";
                ShortsEmpty.Visibility = Visibility.Visible;
                return;
            }

            ShortsPlayer.IsMuted = _shortsMuted;
            ShortsPlayer.Source = new Uri(urls[_shortStreamAttempt]);
            ShortsPlayer.Play();
        }

        private System.Collections.Generic.List<string> PlaybackUrls()
        {
            if (_shortDetails != null && _shortDetails.PlaybackUrls != null && _shortDetails.PlaybackUrls.Count > 0)
            {
                return _shortDetails.PlaybackUrls;
            }

            return new System.Collections.Generic.List<string>();
        }

        private void StopShortsPlayer()
        {
            _shortPlayGeneration++;
            _loadingShortId = null;
            _shortsStreamStarted = false;
            try
            {
                ShortsPlayer.Stop();
                ShortsPlayer.Source = null;
                DisplayAwakeHelper.Release();
            }
            catch
            {
            }

            SetShortsShimmer(false);
            ShortsPlayGlyph.Visibility = Visibility.Collapsed;
        }

        private void ToggleShortsPlay()
        {
            if (ShortsPlayer.CurrentState == MediaElementState.Playing)
            {
                ShortsPlayer.Pause();
            }
            else
            {
                ShortsPlayer.Play();
            }
        }

        private void ShowNextShort()
        {
            if (_shortIndex < ShortsVideos.Count - 1)
            {
                _shortIndex++;
                PlayCurrentShort();
            }
        }

        private void ShowPreviousShort()
        {
            if (_shortIndex > 0)
            {
                _shortIndex--;
                PlayCurrentShort();
            }
        }

        private void ShortsGesture_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (_shortSuppressTap)
            {
                _shortSuppressTap = false;
                return;
            }

            if (_shortsDragging || ShortsCommentsPanel.Visibility == Visibility.Visible)
            {
                return;
            }

            ToggleShortsPlay();
        }

        private void ShortsGesture_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (_shortsDragging || ShortsCommentsPanel.Visibility == Visibility.Visible)
            {
                return;
            }

            _shortSuppressTap = true;
            var item = CurrentShort();
            if (item == null)
            {
                return;
            }

            _shortLiked = true;
            _shortDisliked = false;
            BindShortsChrome(item, _shortDetails);
        }

        private void ShortsGesture_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            _shortsDragging = false;
            _shortsDragY = 0;
        }

        private void ShortsGesture_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            if (ShortsCommentsPanel.Visibility == Visibility.Visible)
            {
                return;
            }

            if (Math.Abs(e.Cumulative.Translation.Y) > 16)
            {
                _shortsDragging = true;
            }

            _shortsDragY = e.Cumulative.Translation.Y;
            if (_shortsDragging)
            {
                var height = ShortsRoot.ActualHeight > 0 ? ShortsRoot.ActualHeight : 640;
                var damped = _shortsDragY * 0.45;
                var max = height * 0.5;
                if (damped > max)
                {
                    damped = max;
                }
                else if (damped < -max)
                {
                    damped = -max;
                }

                ShortsSlideTransform.Y = damped;
            }
        }

        private void ShortsGesture_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (ShortsCommentsPanel.Visibility == Visibility.Visible)
            {
                return;
            }

            var y = _shortsDragY;
            _shortsDragY = 0;
            if (y < -72 && _shortIndex < ShortsVideos.Count - 1)
            {
                TransitionShort(-1);
            }
            else if (y > 72 && _shortIndex > 0)
            {
                TransitionShort(1);
            }
            else if (_shortsDragging)
            {
                AnimateShortsSlide(ShortsSlideTransform.Y, 0, null);
            }
        }

        private void TransitionShort(int direction)
        {
            StopShortsSlideAnimation();
            var changed = false;
            if (direction < 0 && _shortIndex < ShortsVideos.Count - 1)
            {
                _shortIndex++;
                changed = true;
            }
            else if (direction > 0 && _shortIndex > 0)
            {
                _shortIndex--;
                changed = true;
            }

            if (!changed)
            {
                if (_shortsDragging)
                {
                    AnimateShortsSlide(ShortsSlideTransform.Y, 0, null);
                }

                return;
            }

            _shortsDragging = false;
            _shortSuppressTap = true;
            ShortsSlideTransform.Y = direction < 0 ? -48 : 48;
            PlayCurrentShort();
            AnimateShortsSlide(ShortsSlideTransform.Y, 0, () => { _shortSuppressTap = false; });
        }

        private void AnimateShortsSlide(double from, double to, Action onComplete)
        {
            StopShortsSlideAnimation();
            ShortsSlideTransform.Y = from;
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            _shortsSlideStoryboard = new Storyboard();
            _shortsSlideStoryboard.Children.Add(animation);
            Storyboard.SetTarget(animation, ShortsSlideTransform);
            Storyboard.SetTargetProperty(animation, "Y");
            if (onComplete != null)
            {
                _shortsSlideStoryboard.Completed += (s, e) =>
                {
                    _shortsSlideStoryboard = null;
                    onComplete();
                };
            }

            _shortsSlideStoryboard.Begin();
        }

        private void StopShortsSlideAnimation()
        {
            if (_shortsSlideStoryboard != null)
            {
                _shortsSlideStoryboard.Stop();
                _shortsSlideStoryboard = null;
            }
        }

        private void ShortsPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            SetShortsShimmer(false);
            ShortsThumb.Visibility = Visibility.Collapsed;
            ShortsEmpty.Visibility = Visibility.Collapsed;
            ShortsPlayGlyph.Visibility = Visibility.Collapsed;
            _shortsStreamStarted = true;
        }

        private void ShortsPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            _shortStreamAttempt++;
            if (_shortStreamAttempt < PlaybackUrls().Count)
            {
                StartShortsStream();
                return;
            }

            SetShortsShimmer(false);
            ShortsEmpty.Text = "This Short couldn't be played.";
            ShortsEmpty.Visibility = Visibility.Visible;
        }

        private void ShortsPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            ShortsPlayer.Position = TimeSpan.Zero;
            ShortsPlayer.Play();
        }

        private void ShortsPlayer_CurrentStateChanged(object sender, RoutedEventArgs e)
        {
            if (ShortsPlayer.CurrentState == MediaElementState.Paused)
            {
                DisplayAwakeHelper.Release();
                ShortsPlayGlyph.Visibility = Visibility.Visible;
            }
            else if (ShortsPlayer.CurrentState == MediaElementState.Playing)
            {
                DisplayAwakeHelper.Acquire();
                ShortsPlayGlyph.Visibility = Visibility.Collapsed;
                SetShortsShimmer(false);
                _shortsStreamStarted = true;
            }
            else if (ShortsPlayer.CurrentState == MediaElementState.Stopped)
            {
                DisplayAwakeHelper.Release();
            }
            else if (ShortsPlayer.CurrentState == MediaElementState.Buffering
                || ShortsPlayer.CurrentState == MediaElementState.Opening)
            {
                if (!_shortsStreamStarted)
                {
                    SetShortsShimmer(true);
                }
            }
        }

        private void ShortsLike_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var item = CurrentShort();
            if (item == null)
            {
                return;
            }

            _shortLiked = !_shortLiked;
            if (_shortLiked)
            {
                _shortDisliked = false;
            }

            BindShortsChrome(item, _shortDetails);
        }

        private void ShortsDislike_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var item = CurrentShort();
            if (item == null)
            {
                return;
            }

            _shortDisliked = !_shortDisliked;
            if (_shortDisliked)
            {
                _shortLiked = false;
            }

            BindShortsChrome(item, _shortDetails);
        }

        private void ShortsComments_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var item = CurrentShort();
            if (item == null || string.IsNullOrEmpty(item.VideoId))
            {
                return;
            }

            ShortsCommentsPanel.Visibility = Visibility.Visible;
            ShortsCommentsHeader.Text = "Comments";
            ShortsCommentsEmpty.Visibility = Visibility.Collapsed;
            ShortsCommentsLoading.IsActive = true;
            ShortsCommentsList.ItemsSource = null;

            var videoId = item.VideoId;
            Task.Run(async () =>
            {
                try
                {
                    if (_shortDetails == null || _shortDetails.CommentCount <= 0)
                    {
                        await EnrichShortDetailsWorker(videoId, _shortPlayGeneration);
                    }

                    var comments = await App.Api.GetCommentsAsync(videoId);
                    await UiDispatcher.RunLow(() =>
                    {
                        if (videoId != _loadingShortId && CurrentShort()?.VideoId != videoId)
                        {
                            return;
                        }

                        ShortsCommentsList.ItemsSource = comments;
                        ShortsCommentsHeader.Text = comments.Count > 0
                            ? "Comments · " + comments.Count
                            : "Comments";
                        ShortsCommentsEmpty.Visibility = comments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                        ShortsCommentsLoading.IsActive = false;
                    });
                }
                catch
                {
                    await UiDispatcher.RunLow(() =>
                    {
                        if (videoId == _loadingShortId || CurrentShort()?.VideoId == videoId)
                        {
                            ShortsCommentsEmpty.Visibility = Visibility.Visible;
                            ShortsCommentsLoading.IsActive = false;
                        }
                    });
                }
            });
        }

        private void ShortsCommentsClose_Click(object sender, RoutedEventArgs e)
        {
            ShortsCommentsPanel.Visibility = Visibility.Collapsed;
        }

        private void ShortsMute_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _shortsMuted = !_shortsMuted;
            ShortsPlayer.IsMuted = _shortsMuted;
            UpdateMuteChrome();
        }

        private void UpdateMuteChrome()
        {
            YtIcons.Set(ShortsMuteIcon, _shortsMuted ? "closedcaptionsfilled.png" : "closedcaptionshollow.png");
            SetOverlayText(ShortsMuteLabel, ShortsMuteLabelShadow, _shortsMuted ? "Muted" : "Sound");
        }

        private static void SetNav(Image icon, TextBlock label, string name, bool selected)
        {
            YtIcons.SetToggle(icon, name, selected);
            var color = new SolidColorBrush(selected ? Colors.White : Color.FromArgb(255, 144, 144, 144));
            label.Foreground = color;
        }

        private void ShortsShare_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var item = CurrentShort();
            if (item == null)
            {
                return;
            }

            if (!_shareHooked)
            {
                DataTransferManager.GetForCurrentView().DataRequested += ShortsShare_DataRequested;
                _shareHooked = true;
            }

            DataTransferManager.ShowShareUI();
        }

        private void ShortsShare_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            var item = CurrentShort();
            if (item == null)
            {
                args.Request.FailWithDisplayText("Nothing to share");
                return;
            }

            args.Request.Data.Properties.Title = item.Title ?? "YouTube Short";
            args.Request.Data.SetWebLink(new Uri("https://www.youtube.com/shorts/" + item.VideoId));
        }

        private void ShortsChannel_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var item = CurrentShort();
            if (item == null || string.IsNullOrEmpty(item.AuthorId))
            {
                return;
            }

            AppNavigation.GoChannel(this, item.AuthorId);
        }

        private async void ShortsSubscribe_Click(object sender, RoutedEventArgs e)
        {
            var item = CurrentShort();
            if (item == null || string.IsNullOrEmpty(item.AuthorId))
            {
                return;
            }

            var channel = new ChannelItem();
            channel.Author = item.Author;
            channel.AuthorId = item.AuthorId;
            channel.AuthorThumbnail = item.AuthorThumbnail;
            var subscribed = await App.Library.ToggleSubscriptionAsync(channel);
            UpdateShortsSubscribeChrome(item.AuthorId);
        }

        private void SyncMiniPlayerFromSession()
        {
            if (!MiniPlayerSession.PendingShow && !MiniPlayerSession.IsActive)
            {
                HideMiniPlayerBar();
                return;
            }

            if (!MiniPlayerSession.IsActive || MiniPlayerSession.Source == null)
            {
                MiniPlayerSession.Clear();
                HideMiniPlayerBar();
                return;
            }

            MiniPlayerTitle.Text = MiniPlayerSession.Title;
            MiniPlayerAuthor.Text = MiniPlayerSession.Author;
            MiniPlayerThumb.Source = ImageUrl.ToBitmap(MiniPlayerSession.Thumbnail, 128);
            MiniPlayerThumb.Visibility = Visibility.Visible;
            MiniPlayerBar.Visibility = Visibility.Visible;
            MiniPlayerRow.Height = new GridLength(56);
            MiniPlayerSession.MarkShown();
            StopShortsPlayer();

            var source = MiniPlayerSession.Source;
            if (source != null)
            {
                if (MiniPlayer.Source == null || !MiniPlayer.Source.Equals(source))
                {
                    MiniPlayer.Source = source;
                }
            }

            UpdateMiniPlayerPlayIcon();
        }

        private void MiniPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (MiniPlayerSession.PositionSeconds > 0)
            {
                MiniPlayer.Position = TimeSpan.FromSeconds(MiniPlayerSession.PositionSeconds);
            }

            MiniPlayerThumb.Visibility = Visibility.Collapsed;
            if (MiniPlayerSession.ShouldPlay)
            {
                MiniPlayer.Play();
                DisplayAwakeHelper.Acquire();
            }

            UpdateMiniPlayerPlayIcon();
        }

        private void HideMiniPlayerBar()
        {
            MiniPlayerBar.Visibility = Visibility.Collapsed;
            MiniPlayerRow.Height = new GridLength(0);
        }

        private void StopMiniPlayerMedia()
        {
            try
            {
                MiniPlayer.Stop();
                MiniPlayer.Source = null;
                DisplayAwakeHelper.Release();
            }
            catch
            {
            }
        }

        private void CloseMiniPlayer()
        {
            StopMiniPlayerMedia();
            MiniPlayerSession.Clear();
            HideMiniPlayerBar();
        }

        private void UpdateMiniPlayerPlayIcon()
        {
            var playing = MiniPlayer.CurrentState == MediaElementState.Playing;
            YtIcons.Set(MiniPlayerPlayIcon, playing ? "pause.png" : "play.png");
        }

        private void MiniPlayerExpand_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!MiniPlayerSession.IsActive)
            {
                return;
            }

            NavigationContext.SetMain(MainPivot.SelectedIndex);
            MiniPlayerSession.UpdatePlayback(
                MiniPlayer.Source,
                MiniPlayer.Position.TotalSeconds,
                MiniPlayer.CurrentState == MediaElementState.Playing);
            StopMiniPlayerMedia();
            HideMiniPlayerBar();
            Frame.Navigate(typeof(VideoPage), MiniPlayerSession.VideoId);
        }

        private void MiniPlayerPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (MiniPlayer.CurrentState == MediaElementState.Playing)
            {
                MiniPlayer.Pause();
                DisplayAwakeHelper.Release();
            }
            else
            {
                MiniPlayer.Play();
                DisplayAwakeHelper.Acquire();
            }

            UpdateMiniPlayerPlayIcon();
        }

        private void MiniPlayerClose_Click(object sender, RoutedEventArgs e)
        {
            CloseMiniPlayer();
        }
    }
}
