using App1.Helpers;
using App1.Models;
using App1.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

namespace App1
{
    public sealed partial class VideoPage : Page
    {
        private string _videoId;
        private VideoDetails _details;
        private readonly DispatcherTimer _timer;
        private bool _updatingSlider;
        private int _streamAttempt;
        private bool _playbackStarted;
        private double _minimizeDragY;
        private bool _relatedLoadStarted;
        private string _relatedLoadToken;
        private readonly ObservableCollection<FeedItem> _relatedFeed = new ObservableCollection<FeedItem>();
        private DispatcherTimer _relatedFallbackTimer;
        private const int MaxRelatedItems = 12;
        private const int RelatedBatchSize = 2;
        private const int RelatedBatchDelayMs = 300;
        private const double RelatedScrollTriggerPx = 160;
        private readonly string[] _itags = new string[] { "18", "22", "36" };
        private List<VideoItem> _relatedVideos;
        private int _relatedIndex;
        private bool _liked;
        private bool _disliked;
        private bool _descriptionExpanded;
        private bool _commentsExpanded;
        private bool _shareHooked;
        private bool _suppressTap;
        private double _resumeSeconds;
        private readonly DispatcherTimer _seekHintTimer;
        private bool _minimizeDragging;
        private bool _captionsOn;
        private int _videoLoadGeneration;
        private readonly DispatcherTimer _overlayHideTimer;
        private bool _landscapeLayout;
        private bool _layoutFullscreen;

        public VideoPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(400);
            _timer.Tick += Timer_Tick;
            _seekHintTimer = new DispatcherTimer();
            _seekHintTimer.Interval = TimeSpan.FromMilliseconds(700);
            _seekHintTimer.Tick += SeekHintTimer_Tick;
            _overlayHideTimer = new DispatcherTimer();
            _overlayHideTimer.Interval = TimeSpan.FromSeconds(3);
            _overlayHideTimer.Tick += OverlayHideTimer_Tick;
            RelatedList.ItemsSource = _relatedFeed;
            ContentScroll.ViewChanged += ContentScroll_ViewChanged;
            this.SizeChanged += VideoPage_SizeChanged;
            UpdateFullscreenChrome();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            var videoId = e.Parameter as string;
            if (string.IsNullOrEmpty(videoId))
            {
                ErrorText.Text = "Missing video id.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            BeginLoadVideo(videoId);
        }

        public void ReloadVideo(string videoId)
        {
            if (string.IsNullOrEmpty(videoId))
            {
                return;
            }

            if (_videoId == videoId && _details != null)
            {
                return;
            }

            BeginLoadVideo(videoId);
        }

        private void BeginLoadVideo(string videoId)
        {
            if (string.IsNullOrEmpty(videoId))
            {
                ErrorText.Text = "Missing video id.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            var restoringFromMini = MiniPlayerSession.IsActive && MiniPlayerSession.VideoId == videoId;
            if (!restoringFromMini && _details != null && _details.VideoId == videoId)
            {
                return;
            }

            _videoLoadGeneration++;
            var gen = _videoLoadGeneration;
            _videoId = videoId;

            var snapshot = AppContentCache.GetVideo(videoId);
            if (restoringFromMini || (snapshot != null && snapshot.Details != null))
            {
                ApplyWarmState(snapshot, restoringFromMini, gen);
                return;
            }

            PrepareColdVideoUi();
            Task.Run(async () => await LoadPlaybackWorker(videoId, gen));
        }

        private void PrepareColdVideoUi()
        {
            _liked = false;
            _disliked = false;
            _descriptionExpanded = false;
            _commentsExpanded = false;
            _resumeSeconds = 0;
            _playbackStarted = false;
            _relatedLoadStarted = false;
            _relatedLoadToken = null;
            StopRelatedFallbackTimer();

            ErrorText.Visibility = Visibility.Collapsed;
            DescriptionText.Visibility = Visibility.Collapsed;
            CommentsExpanded.Visibility = Visibility.Collapsed;
            CommentsPreview.Visibility = Visibility.Collapsed;
            MoreLink.Visibility = Visibility.Collapsed;
            PlayerHostTransform.Y = 0;
            PlayerHostScale.ScaleX = 1;
            PlayerHostScale.ScaleY = 1;
            ContentScroll.Opacity = 1;
            ShowRelatedPlaceholders();
            SetPlayerShimmer(true);
            BindCommentsBar();
        }

        private async Task LoadPlaybackWorker(string videoId, int gen)
        {
            try
            {
                var details = await App.Api.GetVideoPlaybackAsync(videoId);
                if (gen != _videoLoadGeneration || videoId != _videoId)
                {
                    return;
                }

                await UiDispatcher.RunNormal(() =>
                {
                    if (gen != _videoLoadGeneration || videoId != _videoId)
                    {
                        return;
                    }

                    _details = details;
                    QueueVideoHistory(details);
                    BindDetails();
                    BindCommentsBar();
                    StartPlayback();
                });
            }
            catch (Exception ex)
            {
                await UiDispatcher.RunNormal(() =>
                {
                    if (gen != _videoLoadGeneration)
                    {
                        return;
                    }

                    ErrorText.Text = "Couldn't load this video. " + ex.Message;
                    ErrorText.Visibility = Visibility.Visible;
                    SetPlayerShimmer(false);
                });
            }
        }

        private void ApplyWarmState(VideoSnapshot snapshot, bool fromMini, int gen)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            DescriptionText.Visibility = Visibility.Collapsed;
            CommentsExpanded.Visibility = Visibility.Collapsed;
            CommentsPreview.Visibility = Visibility.Collapsed;
            MoreLink.Visibility = Visibility.Collapsed;
            PlayerHostTransform.Y = 0;
            PlayerHostScale.ScaleX = 1;
            PlayerHostScale.ScaleY = 1;
            ContentScroll.Opacity = 1;

            _details = fromMini && MiniPlayerSession.Details != null
                ? MiniPlayerSession.Details
                : snapshot != null ? snapshot.Details : null;

            if (_details == null)
            {
                PrepareColdVideoUi();
                Task.Run(async () => await LoadPlaybackWorker(_videoId, gen));
                return;
            }

            _liked = false;
            _disliked = false;
            _descriptionExpanded = false;
            _commentsExpanded = false;

            if (snapshot != null)
            {
                _relatedVideos = snapshot.RelatedVideos;
                _relatedIndex = snapshot.RelatedIndex;
            }

            BindDetails();
            BindCommentsBar();

            if (snapshot != null && snapshot.MixedRelated != null && snapshot.MixedRelated.Count > 0)
            {
                PopulateRelatedFeed(snapshot.MixedRelated);
                _relatedLoadStarted = true;
            }
            else if (_relatedVideos == null || _relatedVideos.Count == 0)
            {
                ShowRelatedPlaceholders();
                ScheduleRelatedLoadFallback();
            }
            else
            {
                PopulateRelatedFromVideos(_relatedVideos);
                _relatedLoadStarted = true;
            }

            if (fromMini && MiniPlayerSession.Source != null)
            {
                _resumeSeconds = MiniPlayerSession.PositionSeconds;
                _streamAttempt = 0;
                Player.Source = MiniPlayerSession.Source;
                _timer.Start();
                SetPlayerShimmer(false);
                if (MiniPlayerSession.ShouldPlay)
                {
                    Player.Play();
                }

                MiniPlayerSession.Clear();
                SaveSnapshot();
                return;
            }

            _resumeSeconds = 0;
            StartPlayback();
            SaveSnapshot();
        }

        public void HandleHardwareBack()
        {
            MinimizeToMiniPlayer();
        }

        public void StashMiniPlayer()
        {
            if (string.IsNullOrEmpty(_videoId) || Player.Source == null)
            {
                return;
            }

            SaveSnapshot();
            MiniPlayerSession.Stash(
                _videoId,
                _details != null ? _details.Title : TitleText.Text,
                _details != null ? _details.Author : "",
                _details != null ? _details.AuthorThumbnail : "",
                Player.Source,
                Player.Position.TotalSeconds,
                Player.CurrentState == MediaElementState.Playing,
                _details);

            _timer.Stop();
            Player.Stop();
            Player.Source = null;
        }

        private async System.Threading.Tasks.Task LoadVideoAsync(string videoId)
        {
            BeginLoadVideo(videoId);
        }

        private void SetPlayerShimmer(bool visible)
        {
            PlayerShimmer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowRelatedPlaceholders()
        {
            ClearRelatedFeed();
            for (int i = 0; i < 2; i++)
            {
                _relatedFeed.Add(FeedItem.Placeholder("related" + i));
            }
        }

        private void ClearRelatedFeed()
        {
            _relatedFeed.Clear();
        }

        private void ClearRelatedPlaceholders()
        {
            for (int i = _relatedFeed.Count - 1; i >= 0; i--)
            {
                if (_relatedFeed[i].Kind == FeedItemKind.Placeholder)
                {
                    _relatedFeed.RemoveAt(i);
                }
            }
        }

        private void PopulateRelatedFeed(List<FeedItem> items)
        {
            ClearRelatedFeed();
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count && i < MaxRelatedItems; i++)
            {
                if (items[i].Kind == FeedItemKind.Video)
                {
                    _relatedFeed.Add(items[i]);
                }
            }
        }

        private void PopulateRelatedFromVideos(List<VideoItem> videos)
        {
            ClearRelatedFeed();
            if (videos == null)
            {
                return;
            }

            for (int i = 0; i < videos.Count && i < MaxRelatedItems; i++)
            {
                _relatedFeed.Add(FeedItem.FromVideo(videos[i]));
            }
        }

        private void ContentScroll_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (ContentScroll.VerticalOffset >= RelatedScrollTriggerPx)
            {
                TryStartRelatedLoad();
            }
        }

        private void ScheduleRelatedLoadFallback()
        {
            if (_relatedLoadStarted)
            {
                return;
            }

            StopRelatedFallbackTimer();
            _relatedFallbackTimer = new DispatcherTimer();
            _relatedFallbackTimer.Interval = TimeSpan.FromSeconds(4);
            _relatedFallbackTimer.Tick += RelatedFallbackTimer_Tick;
            _relatedFallbackTimer.Start();
        }

        private void RelatedFallbackTimer_Tick(object sender, object e)
        {
            StopRelatedFallbackTimer();
            TryStartRelatedLoad();
        }

        private void StopRelatedFallbackTimer()
        {
            if (_relatedFallbackTimer != null)
            {
                _relatedFallbackTimer.Stop();
                _relatedFallbackTimer.Tick -= RelatedFallbackTimer_Tick;
                _relatedFallbackTimer = null;
            }
        }

        private void TryStartRelatedLoad()
        {
            if (_relatedLoadStarted || string.IsNullOrEmpty(_videoId))
            {
                return;
            }

            _relatedLoadStarted = true;
            StopRelatedFallbackTimer();
            LoadRelatedAsyncFire();
        }

        private void BindCommentsBar()
        {
            if (_details == null)
            {
                CommentsPreview.Visibility = Visibility.Visible;
                CommentsPreviewHeader.Text = "Comments";
                TopCommentText.Text = "Tap to read comments";
                CommentsHeader.Text = "Comments";
                return;
            }

            CommentsPreview.Visibility = Visibility.Visible;
            if (_details.CommentCount > 0)
            {
                CommentsPreviewHeader.Text = "Comments · " + FormatUtil.Compact(_details.CommentCount);
            }
            else
            {
                CommentsPreviewHeader.Text = "Comments";
            }

            TopCommentText.Text = "Tap to read comments";
            CommentsHeader.Text = CommentsPreviewHeader.Text;
        }

        private void BeginDeferredLoads()
        {
            EnrichVideoDetailsAsync();
            ScheduleRelatedLoadFallback();
            PrefetchCommentsAsync();
        }

        private void PrefetchCommentsAsync()
        {
            var videoId = _videoId;
            if (string.IsNullOrEmpty(videoId))
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    var comments = await App.Api.GetCommentsAsync(videoId);
                    await UiDispatcher.RunLow(() =>
                    {
                        if (videoId != _videoId || CommentsList.ItemsSource != null)
                        {
                            return;
                        }

                        CommentsList.ItemsSource = comments;
                        if (comments.Count > 0 && TopCommentText != null)
                        {
                            var top = comments[0];
                            TopCommentText.Text = top.Content ?? "Tap to read comments";
                            if (!string.IsNullOrEmpty(top.AuthorThumbnail))
                            {
                                TopCommentBrush.ImageSource = ImageUrl.ToBitmap(top.AuthorThumbnail, 56);
                            }
                        }
                    });
                }
                catch
                {
                }
            });
        }

        private void QueueVideoHistory(VideoDetails details)
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

        private async void EnrichVideoDetailsAsync()
        {
            if (string.IsNullOrEmpty(_videoId))
            {
                return;
            }

            var videoId = _videoId;
            Task.Run(async () =>
            {
                await Task.Delay(1500);
                if (videoId != _videoId)
                {
                    return;
                }

                try
                {
                    var full = await App.Api.GetVideoAsync(videoId);
                    if (full == null || full.VideoId != videoId || videoId != _videoId)
                    {
                        return;
                    }

                    await UiDispatcher.RunLow(() =>
                    {
                        if (videoId != _videoId)
                        {
                            return;
                        }

                        _details = full;
                        BindDetails();
                        BindCommentsBar();
                        SaveSnapshot();
                    });
                }
                catch
                {
                }
            });
        }

        private void LoadRelatedAsyncFire()
        {
            Task.Run(async () => await LoadRelatedAsync());
        }

        private async System.Threading.Tasks.Task RestoreWarmState(VideoSnapshot snapshot, bool fromMini)
        {
            ApplyWarmState(snapshot, fromMini, _videoLoadGeneration);
        }

        private void SaveSnapshot()
        {
            if (string.IsNullOrEmpty(_videoId) || _details == null)
            {
                return;
            }

            var mixed = new List<FeedItem>();
            foreach (var item in _relatedFeed)
            {
                mixed.Add(item);
            }

            AppContentCache.PutVideo(_videoId, new VideoSnapshot
            {
                Details = _details,
                RelatedVideos = _relatedVideos,
                MixedRelated = mixed,
                RelatedIndex = _relatedIndex
            });
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            _videoLoadGeneration++;
            StopRelatedFallbackTimer();
            _overlayHideTimer.Stop();
            DisplayAwakeHelper.Release();
            SaveSnapshot();
            _timer.Stop();
            _seekHintTimer.Stop();
            if (!MiniPlayerSession.PendingShow)
            {
                Player.Stop();
                Player.Source = null;
            }
        }

        private async Task LoadExtras()
        {
            BindCommentsBar();
            LoadRelatedAsyncFire();
        }

        private void BindCommentsPreviewFromDetails()
        {
            BindCommentsBar();
        }

        private async Task LoadRelatedAsync()
        {
            var videoId = _videoId;
            if (string.IsNullOrEmpty(videoId))
            {
                return;
            }

            try
            {
                _relatedLoadToken = videoId;
                var related = await App.Api.GetRecommendedAsync(videoId);
                if (_relatedLoadToken != videoId)
                {
                    return;
                }

                _relatedVideos = new List<VideoItem>();
                _relatedIndex = -1;
                foreach (var video in related)
                {
                    if (video.IsShort || video.IsAd || string.IsNullOrEmpty(video.Title))
                    {
                        continue;
                    }

                    if (_relatedVideos.Count >= MaxRelatedItems)
                    {
                        break;
                    }

                    _relatedVideos.Add(video);
                    if (video.VideoId == videoId)
                    {
                        _relatedIndex = _relatedVideos.Count - 1;
                    }
                }

                if (_details != null && !string.IsNullOrEmpty(_details.AuthorId))
                {
                    foreach (var video in _relatedVideos)
                    {
                        if (string.Equals(video.AuthorId, _details.AuthorId, StringComparison.Ordinal))
                        {
                            VideoClassifier.EnrichFromDetails(video, _details);
                        }
                    }
                }

                var items = new List<FeedItem>();
                for (int i = 0; i < _relatedVideos.Count; i++)
                {
                    items.Add(FeedItem.FromVideo(_relatedVideos[i]));
                }

                for (int i = 0; i < items.Count; i += RelatedBatchSize)
                {
                    if (_relatedLoadToken != videoId)
                    {
                        break;
                    }

                    var batchEnd = Math.Min(i + RelatedBatchSize, items.Count);
                    var batchStart = i;
                    await UiDispatcher.RunLow(() =>
                    {
                        if (_relatedLoadToken != videoId)
                        {
                            return;
                        }

                        ClearRelatedPlaceholders();
                        for (int j = batchStart; j < batchEnd; j++)
                        {
                            _relatedFeed.Add(items[j]);
                        }
                    });

                    if (batchEnd < items.Count)
                    {
                        await Task.Delay(RelatedBatchDelayMs);
                    }
                }

                await UiDispatcher.RunLow(() =>
                {
                    if (_relatedLoadToken == videoId)
                    {
                        SaveSnapshot();
                    }
                });
            }
            catch
            {
                await UiDispatcher.RunLow(() =>
                {
                    if (_relatedLoadToken == videoId)
                    {
                        _relatedVideos = new List<VideoItem>();
                        ClearRelatedFeed();
                    }
                });
            }
        }

        private void BindDetails()
        {
            TitleText.Text = _details.Title ?? "";

            var handle = FormatUtil.FormatHandle(_details.AuthorHandle, _details.Author);

            var likes = string.IsNullOrEmpty(_details.LikeText) ? "" : _details.LikeText;
            if (!string.IsNullOrEmpty(likes) && likes.IndexOf("like", StringComparison.OrdinalIgnoreCase) < 0)
            {
                likes += " likes";
            }

            var published = FormatUtil.Published(_details.PublishedText, _details.Published);
            if (_details.IsLive && string.IsNullOrEmpty(published))
            {
                published = "Streamed live" + (string.IsNullOrEmpty(_details.PublishedText) ? "" : " on " + _details.PublishedText);
            }

            var genre = string.IsNullOrEmpty(_details.Genre) ? "" : "#" + _details.Genre.Replace(" ", "");
            MetaText.Text = FormatUtil.JoinMeta(handle, likes, _details.ViewCountText, published, genre);

            var hasDescription = !string.IsNullOrWhiteSpace(_details.Description);
            MoreLink.Visibility = hasDescription && !_descriptionExpanded ? Visibility.Visible : Visibility.Collapsed;
            DescriptionText.Text = _details.Description ?? "";

            var authorThumb = _details.AuthorThumbnail;
            UiDispatcher.RunLow(() => AuthorBrush.ImageSource = ImageUrl.ToBitmap(authorThumb, 88));
            UpdateSubscribeChrome();
            UpdateActionIcons();

            if (_details.IsShort)
            {
                PlayerHost.Height = Math.Max(360, Window.Current.Bounds.Width * 1.4);
                Player.Stretch = Stretch.UniformToFill;
            }
        }

        private void UpdateSubscribeChrome()
        {
            if (_details == null)
            {
                return;
            }

            var subscribed = App.Library.IsSubscribed(_details.AuthorId);
            SubscribeLabel.Text = subscribed ? "Subscribed" : "Subscribe";
            SubscribePill.Background = subscribed
                ? new SolidColorBrush(Color.FromArgb(255, 58, 58, 58))
                : new SolidColorBrush(Colors.White);
            SubscribeLabel.Foreground = subscribed
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Colors.Black);
            SubscribeBellIcon.Visibility = subscribed ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateActionIcons()
        {
            YtIcons.SetToggle(LikeIcon, "like", _liked);
            YtIcons.SetToggle(DislikeIcon, "dislike", _disliked);
            if (OverlayLikeIcon != null)
            {
                YtIcons.SetToggle(OverlayLikeIcon, "like", _liked);
            }

            if (OverlayDislikeIcon != null)
            {
                YtIcons.SetToggle(OverlayDislikeIcon, "dislike", _disliked);
            }
        }

        private void UpdateFullscreenChrome()
        {
            if (FullscreenActions == null)
            {
                return;
            }

            var fullscreen = IsPlayerFullscreen();
            FullscreenActions.Visibility = fullscreen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void StartPlayback()
        {
            var urls = PlaybackCandidates();
            if (urls.Count == 0 || _streamAttempt >= urls.Count)
            {
                SetPlayerShimmer(false);
                ErrorText.Text = "This video couldn't be played on this device. Try the other data source in Settings.";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            _playbackStarted = false;
            SetPlayerShimmer(true);
            Player.Source = new Uri(urls[_streamAttempt]);
            _timer.Start();
        }

        private List<string> PlaybackCandidates()
        {
            if (_details != null && _details.PlaybackUrls != null && _details.PlaybackUrls.Count > 0)
            {
                return _details.PlaybackUrls;
            }

            var urls = new List<string>();
            for (int i = 0; i < _itags.Length; i++)
            {
                var url = App.Api.GetPlaybackUrl(_videoId, _itags[i]);
                if (!string.IsNullOrEmpty(url) && !urls.Contains(url))
                {
                    urls.Add(url);
                }
            }

            return urls;
        }

        private void Timer_Tick(object sender, object e)
        {
            if (_updatingSlider || !Player.NaturalDuration.HasTimeSpan || Player.NaturalDuration.TimeSpan.TotalSeconds <= 0)
            {
                return;
            }

            _updatingSlider = true;
            PositionSlider.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
            PositionSlider.Value = Player.Position.TotalSeconds;
            TimeText.Text = FormatUtil.Duration((int)Player.Position.TotalSeconds) + " / " +
                            FormatUtil.Duration((int)Player.NaturalDuration.TimeSpan.TotalSeconds);
            _updatingSlider = false;
        }

        private void PositionSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_updatingSlider)
            {
                return;
            }

            Player.Position = TimeSpan.FromSeconds(PositionSlider.Value);
        }

        private void OverlayPlayPause_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _suppressTap = true;
            TogglePlay();
        }

        private void TogglePlay()
        {
            if (Player.CurrentState == MediaElementState.Playing)
            {
                Player.Pause();
            }
            else
            {
                Player.Play();
            }
        }

        private void PlayerHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (_suppressTap)
            {
                _suppressTap = false;
                return;
            }

            if (Player.CurrentState == MediaElementState.Playing)
            {
                UpdatePlayerChrome(PlayerOverlay.Visibility != Visibility.Visible);
            }
            else
            {
                TogglePlay();
            }
        }

        private void PlayerHost_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            _suppressTap = true;
            var pos = e.GetPosition(PlayerHost);
            var rewind = pos.X < (PlayerHost.ActualWidth / 2.0);
            SeekRelative(rewind ? -10 : 10);
            ShowSeekHint(rewind ? "-10s" : "+10s");
        }

        private void SeekRelative(int seconds)
        {
            if (!Player.NaturalDuration.HasTimeSpan)
            {
                return;
            }

            var total = Player.NaturalDuration.TimeSpan.TotalSeconds;
            var next = Player.Position.TotalSeconds + seconds;
            if (next < 0)
            {
                next = 0;
            }

            if (next > total)
            {
                next = total;
            }

            Player.Position = TimeSpan.FromSeconds(next);
        }

        private void ShowSeekHint(string text)
        {
            SeekHint.Text = text;
            SeekHint.Visibility = Visibility.Visible;
            _seekHintTimer.Stop();
            _seekHintTimer.Start();
        }

        private void SeekHintTimer_Tick(object sender, object e)
        {
            _seekHintTimer.Stop();
            SeekHint.Visibility = Visibility.Collapsed;
        }

        private void UpdatePlayerChrome(bool showOverlay)
        {
            _overlayHideTimer.Stop();
            if (showOverlay)
            {
                PlayerOverlay.Visibility = Visibility.Visible;
                PlayerDimmer.Visibility = Visibility.Visible;
                PlayerOverlay.Opacity = 1;
                PlayerDimmer.Opacity = 1;
                _overlayHideTimer.Start();
            }
            else
            {
                PlayerOverlay.Visibility = Visibility.Collapsed;
                PlayerDimmer.Visibility = Visibility.Collapsed;
                PlayerOverlay.Opacity = 0;
                PlayerDimmer.Opacity = 0;
            }

            if (Player.CurrentState == MediaElementState.Paused)
            {
                PlayGlyph.Visibility = showOverlay ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private bool IsPlayerFullscreen()
        {
            return _landscapeLayout || _layoutFullscreen;
        }

        private void ApplyPlayerLayoutFullscreen(bool fullscreen, double viewportHeight)
        {
            Player.IsFullWindow = false;
            if (fullscreen)
            {
                PlayerHost.SetValue(Grid.RowSpanProperty, 2);
                PlayerHost.Height = viewportHeight > 0 ? viewportHeight : Window.Current.Bounds.Height;
                ContentScroll.Visibility = Visibility.Collapsed;
            }
            else
            {
                PlayerHost.SetValue(Grid.RowSpanProperty, 1);
                ContentScroll.Visibility = Visibility.Visible;
                if (_details != null && _details.IsShort)
                {
                    PlayerHost.Height = Math.Max(360, Window.Current.Bounds.Width * 1.4);
                }
                else
                {
                    PlayerHost.Height = 211;
                }

                HideLandscapeSidePanel();
            }

            UpdateFullscreenChrome();
        }

        private void OverlayHideTimer_Tick(object sender, object e)
        {
            _overlayHideTimer.Stop();
            if (Player.CurrentState == MediaElementState.Playing)
            {
                UpdatePlayerChrome(false);
            }
        }

        private void VideoPage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var landscape = e.NewSize.Width > e.NewSize.Height;
            if (landscape && !_landscapeLayout)
            {
                _landscapeLayout = true;
                _layoutFullscreen = false;
                ApplyPlayerLayoutFullscreen(true, e.NewSize.Height);
                YtIcons.Set(FullScreenIcon, "exitfullscreen.png");
            }
            else if (!landscape && _landscapeLayout)
            {
                _landscapeLayout = false;
                ApplyPlayerLayoutFullscreen(_layoutFullscreen, _layoutFullscreen ? e.NewSize.Height : 0);
                YtIcons.Set(FullScreenIcon, _layoutFullscreen ? "exitfullscreen.png" : "fullscreen.png");
            }
        }

        private void FullScreen_Click(object sender, RoutedEventArgs e)
        {
            if (_landscapeLayout)
            {
                return;
            }

            _layoutFullscreen = !_layoutFullscreen;
            ApplyPlayerLayoutFullscreen(_layoutFullscreen, Window.Current.Bounds.Height);
            YtIcons.Set(FullScreenIcon, _layoutFullscreen ? "exitfullscreen.png" : "fullscreen.png");
        }

        private void Player_MediaOpened(object sender, RoutedEventArgs e)
        {
            SetPlayerShimmer(false);
            _playbackStarted = true;
            PlayGlyph.Visibility = Visibility.Collapsed;
            DisplayAwakeHelper.Acquire();
            BeginDeferredLoads();
            if (_resumeSeconds > 0)
            {
                Player.Position = TimeSpan.FromSeconds(_resumeSeconds);
                _resumeSeconds = 0;
            }
        }

        private void Player_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            _streamAttempt++;
            if (_streamAttempt < PlaybackCandidates().Count)
            {
                StartPlayback();
                return;
            }

            SetPlayerShimmer(false);
            ErrorText.Text = "This video couldn't be played on this device. Try switching between InnerTube and Invidious in Settings.";
            ErrorText.Visibility = Visibility.Visible;
        }

        private void Player_MediaEnded(object sender, RoutedEventArgs e)
        {
            YtIcons.Set(OverlayPlayIcon, "play.png");
            UpdatePlayerChrome(true);
            PlayGlyph.Visibility = Visibility.Visible;
        }

        private void Player_CurrentStateChanged(object sender, RoutedEventArgs e)
        {
            if (Player.CurrentState == MediaElementState.Playing)
            {
                DisplayAwakeHelper.Acquire();
                YtIcons.Set(OverlayPlayIcon, "pause.png");
                UpdatePlayerChrome(false);
                SetPlayerShimmer(false);
                _playbackStarted = true;
                PlayGlyph.Visibility = Visibility.Collapsed;
            }
            else if (Player.CurrentState == MediaElementState.Paused)
            {
                YtIcons.Set(OverlayPlayIcon, "play.png");
                UpdatePlayerChrome(true);
                PlayGlyph.Visibility = Visibility.Visible;
            }
            else if (Player.CurrentState == MediaElementState.Stopped)
            {
                DisplayAwakeHelper.Release();
            }
            else if (Player.CurrentState == MediaElementState.Buffering
                || Player.CurrentState == MediaElementState.Opening)
            {
                if (!_playbackStarted)
                {
                    SetPlayerShimmer(true);
                }
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            MinimizeToMiniPlayer();
        }

        private void PlayerHost_ManipulationStarted(object sender, ManipulationStartedRoutedEventArgs e)
        {
            _minimizeDragging = false;
            _minimizeDragY = 0;
        }

        private void PlayerHost_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            var y = e.Cumulative.Translation.Y;
            if (y > 16 && Math.Abs(e.Cumulative.Translation.X) < 100)
            {
                _minimizeDragging = true;
            }

            _minimizeDragY = y;
            if (_minimizeDragging)
            {
                var drag = Math.Max(0, y);
                PlayerHostTransform.Y = drag * 0.55;
                var scale = Math.Max(0.78, 1.0 - drag / 800.0);
                PlayerHostScale.ScaleX = scale;
                PlayerHostScale.ScaleY = scale;
                ContentScroll.Opacity = Math.Max(0.35, 1.0 - drag / 320.0);
                PlayerDimmer.Visibility = Visibility.Visible;
                PlayerDimmer.Opacity = Math.Min(0.35, drag / 280.0);
            }
        }

        private void PlayerHost_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            var y = _minimizeDragY;
            _minimizeDragY = 0;
            PlayerDimmer.Visibility = Visibility.Collapsed;
            if (y > 72 && Math.Abs(e.Cumulative.Translation.X) < 100)
            {
                ResetPlayerHostTransform();
                FinishMinimize();
            }
            else if (_minimizeDragging)
            {
                AnimatePlayerHostReset();
            }
        }

        private void ResetPlayerHostTransform()
        {
            PlayerHostTransform.Y = 0;
            PlayerHostScale.ScaleX = 1;
            PlayerHostScale.ScaleY = 1;
            ContentScroll.Opacity = 1;
        }

        private void AnimatePlayerHostReset()
        {
            var fromY = PlayerHostTransform.Y;
            var fromScale = PlayerHostScale.ScaleX;
            var fromOpacity = ContentScroll.Opacity;
            var animationY = new DoubleAnimation
            {
                From = fromY,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var animationScale = new DoubleAnimation
            {
                From = fromScale,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var animationOpacity = new DoubleAnimation
            {
                From = fromOpacity,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var storyboard = new Storyboard();
            storyboard.Children.Add(animationY);
            storyboard.Children.Add(animationScale);
            storyboard.Children.Add(animationOpacity);
            Storyboard.SetTarget(animationY, PlayerHostTransform);
            Storyboard.SetTargetProperty(animationY, "Y");
            Storyboard.SetTarget(animationScale, PlayerHostScale);
            Storyboard.SetTargetProperty(animationScale, "ScaleX");
            Storyboard.SetTarget(animationScale, PlayerHostScale);
            Storyboard.SetTargetProperty(animationScale, "ScaleY");
            Storyboard.SetTarget(animationOpacity, ContentScroll);
            Storyboard.SetTargetProperty(animationOpacity, "Opacity");
            storyboard.Begin();
        }

        private void AnimatePlayerHostY(double from, double to)
        {
            PlayerHostTransform.Y = from;
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            Storyboard.SetTarget(animation, PlayerHostTransform);
            Storyboard.SetTargetProperty(animation, "Y");
            storyboard.Begin();
        }

        private void MinimizeToMiniPlayer()
        {
            if (string.IsNullOrEmpty(_videoId) || Player.Source == null)
            {
                if (Frame.CanGoBack)
                {
                    Frame.GoBack();
                }

                return;
            }

            ResetPlayerHostTransform();
            FinishMinimize();
        }

        private void FinishMinimize()
        {
            SaveSnapshot();
            MiniPlayerSession.Stash(
                _videoId,
                _details != null ? _details.Title : TitleText.Text,
                _details != null ? _details.Author : "",
                _details != null ? _details.AuthorThumbnail : "",
                Player.Source,
                Player.Position.TotalSeconds,
                Player.CurrentState == MediaElementState.Playing,
                _details);

            _timer.Stop();
            Player.Stop();
            Player.Source = null;

            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            Frame.ContentTransitions = null;
            Frame.Navigate(typeof(SettingsPage));
        }

        private void PrevVideo_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PlayRelated(-1);
        }

        private void NextVideo_Tapped(object sender, TappedRoutedEventArgs e)
        {
            PlayRelated(1);
        }

        private void PlayRelated(int offset)
        {
            if (_relatedVideos == null || _relatedIndex < 0)
            {
                return;
            }

            var newIndex = _relatedIndex + offset;
            if (newIndex < 0 || newIndex >= _relatedVideos.Count)
            {
                return;
            }

            var video = _relatedVideos[newIndex];
            if (!string.IsNullOrEmpty(video.VideoId))
            {
                AppNavigation.OpenVideo(this, video.VideoId);
            }
        }

        private void Like_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _liked = !_liked;
            if (_liked)
            {
                _disliked = false;
            }

            UpdateActionIcons();
        }

        private void Dislike_Tapped(object sender, TappedRoutedEventArgs e)
        {
            _disliked = !_disliked;
            if (_disliked)
            {
                _liked = false;
            }

            UpdateActionIcons();
        }

        private void Share_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (!_shareHooked)
            {
                DataTransferManager.GetForCurrentView().DataRequested += Share_DataRequested;
                _shareHooked = true;
            }

            DataTransferManager.ShowShareUI();
        }

        private void Share_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            var request = args.Request;
            request.Data.Properties.Title = _details != null ? _details.Title : "YouTube video";
            request.Data.Properties.Description = "Shared from YouTube";
            request.Data.SetWebLink(new Uri("https://www.youtube.com/watch?v=" + _videoId));
        }

        private void Save_Tapped(object sender, TappedRoutedEventArgs e)
        {
        }

        private void More_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (_details == null)
            {
                return;
            }

            ShowInfoOverlay("Description", _details.Description ?? "");
        }

        private void PrepareInfoOverlayLayout()
        {
            var pageHeight = Window.Current.Bounds.Height;
            var maxCard = pageHeight - 64;
            if (maxCard < 180)
            {
                maxCard = 180;
            }

            InfoOverlayCard.MaxHeight = maxCard;
            InfoOverlayScroll.MaxHeight = maxCard - 56;
        }

        private ItemsControl BuildCommentsItemsControl(object itemsSource)
        {
            return new ItemsControl
            {
                ItemTemplate = (DataTemplate)Application.Current.Resources["CommentTemplate"],
                ItemsSource = itemsSource
            };
        }

        private void ShowCommentsLoadingOverlay()
        {
            if (IsPlayerFullscreen())
            {
                LandscapePanelTitle.Text = "Comments";
                LandscapePanelContent.Children.Clear();
                LandscapePanelContent.Children.Add(new ProgressRing
                {
                    Width = 40,
                    Height = 40,
                    IsActive = true,
                    Foreground = new SolidColorBrush(Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 24, 0, 24)
                });
                LandscapeSidePanel.Visibility = Visibility.Visible;
                PlayerHost.Width = Window.Current.Bounds.Width - 300;
                PlayerHostScale.ScaleX = 0.88;
                PlayerHostScale.ScaleY = 0.88;
                return;
            }

            InfoOverlayTitle.Text = "Comments";
            InfoOverlayBodyHost.Children.Clear();
            InfoOverlayBodyHost.Children.Add(new ProgressRing
            {
                Width = 40,
                Height = 40,
                IsActive = true,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 24, 0, 24)
            });
            PrepareInfoOverlayLayout();
            InfoOverlay.Visibility = Visibility.Visible;
            InfoOverlay.Opacity = 1;
        }

        private void ShowCommentsEmptyOverlay()
        {
            if (IsPlayerFullscreen())
            {
                ShowLandscapePanel("Comments", false, "No comments on this video.");
                return;
            }

            InfoOverlayTitle.Text = "Comments";
            InfoOverlayBodyHost.Children.Clear();
            InfoOverlayBodyHost.Children.Add(new TextBlock
            {
                Text = "No comments on this video.",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170)),
                FontSize = 14
            });
            PrepareInfoOverlayLayout();
            InfoOverlay.Visibility = Visibility.Visible;
            InfoOverlay.Opacity = 1;
        }

        private void ShowCommentsErrorOverlay(string message)
        {
            if (IsPlayerFullscreen())
            {
                ShowLandscapePanel("Comments", false, message);
                return;
            }

            InfoOverlayTitle.Text = "Comments";
            InfoOverlayBodyHost.Children.Clear();
            InfoOverlayBodyHost.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170)),
                FontSize = 14
            });
            PrepareInfoOverlayLayout();
            InfoOverlay.Visibility = Visibility.Visible;
            InfoOverlay.Opacity = 1;
        }

        private void ShowInfoOverlay(string title, string body)
        {
            if (IsPlayerFullscreen())
            {
                ShowLandscapePanel(title, false, body);
                return;
            }

            InfoOverlayTitle.Text = title ?? "";
            InfoOverlayBodyHost.Children.Clear();
            var text = new TextBlock
            {
                Text = body ?? "",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.White)
            };
            InfoOverlayBodyHost.Children.Add(text);
            PrepareInfoOverlayLayout();
            InfoOverlay.Visibility = Visibility.Visible;
            InfoOverlay.Opacity = 1;
        }

        private void ShowLandscapePanel(string title, bool comments, string bodyText)
        {
            LandscapePanelTitle.Text = title ?? "";
            LandscapePanelContent.Children.Clear();
            if (comments)
            {
                LandscapePanelContent.Children.Add(BuildCommentsItemsControl(CommentsList.ItemsSource));
            }
            else
            {
                LandscapePanelContent.Children.Add(new TextBlock
                {
                    Text = bodyText ?? "",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Colors.White)
                });
            }

            LandscapeSidePanel.Visibility = Visibility.Visible;
            PlayerHost.Width = Window.Current.Bounds.Width - 300;
            PlayerHostScale.ScaleX = 0.88;
            PlayerHostScale.ScaleY = 0.88;
        }

        private void HideLandscapeSidePanel()
        {
            LandscapeSidePanel.Visibility = Visibility.Collapsed;
            PlayerHost.Width = double.NaN;
            PlayerHostScale.ScaleX = 1;
            PlayerHostScale.ScaleY = 1;
        }

        private void LandscapePanelClose_Click(object sender, RoutedEventArgs e)
        {
            HideLandscapeSidePanel();
        }

        private void InfoOverlayClose_Click(object sender, RoutedEventArgs e)
        {
            AnimationHelper.Hide(InfoOverlay, 150);
        }

        private void InfoOverlayBackground_Tapped(object sender, TappedRoutedEventArgs e)
        {
            InfoOverlayClose_Click(sender, null);
        }

        private void CommentsPreview_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (IsPlayerFullscreen())
            {
                if (CommentsList.ItemsSource == null)
                {
                    LoadCommentsForOverlay();
                }
                else
                {
                    ShowLandscapePanel("Comments", true, null);
                }

                return;
            }

            if (CommentsList.ItemsSource == null)
            {
                LoadCommentsForOverlay();
            }
            else
            {
                ShowCommentsInfoOverlay();
            }
        }

        private void LoadCommentsForOverlay()
        {
            var videoId = _videoId;
            ShowCommentsLoadingOverlay();
            Task.Run(async () =>
            {
                try
                {
                    var comments = await App.Api.GetCommentsAsync(videoId);
                    await UiDispatcher.RunLow(() =>
                    {
                        if (videoId != _videoId)
                        {
                            return;
                        }

                        CommentsList.ItemsSource = comments;
                        CommentsEmpty.Visibility = comments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                        if (comments.Count == 0)
                        {
                            ShowCommentsEmptyOverlay();
                            return;
                        }

                        if (IsPlayerFullscreen())
                        {
                            ShowLandscapePanel("Comments", true, null);
                        }
                        else
                        {
                            ShowCommentsInfoOverlay();
                        }
                    });
                }
                catch
                {
                    await UiDispatcher.RunLow(() =>
                    {
                        if (videoId != _videoId)
                        {
                            return;
                        }

                        ShowCommentsErrorOverlay("Comments could not be loaded. Try switching data source in Settings.");
                    });
                }
            });
        }

        private void ShowCommentsInfoOverlay()
        {
            InfoOverlayTitle.Text = CommentsHeader.Text;
            InfoOverlayBodyHost.Children.Clear();
            InfoOverlayBodyHost.Children.Add(BuildCommentsItemsControl(CommentsList.ItemsSource));
            PrepareInfoOverlayLayout();
            InfoOverlay.Visibility = Visibility.Visible;
            InfoOverlay.Opacity = 1;
        }

        private async void Subscribe_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (_details == null)
            {
                return;
            }

            var channel = new ChannelItem();
            channel.Author = _details.Author;
            channel.AuthorId = _details.AuthorId;
            channel.AuthorThumbnail = _details.AuthorThumbnail;
            channel.SubCountText = _details.SubCountText;
            await App.Library.ToggleSubscriptionAsync(channel);
            UpdateSubscribeChrome();
        }

        private void Captions_Click(object sender, RoutedEventArgs e)
        {
            _captionsOn = !_captionsOn;
            YtIcons.Set(CaptionsIcon, _captionsOn ? "closedcaptionsfilled.png" : "closedcaptionshollow.png");
        }

        private void Channel_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (_details != null && !string.IsNullOrEmpty(_details.AuthorId))
            {
                AppNavigation.GoChannel(this, _details.AuthorId);
            }
        }

        private void RelatedList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var feed = e.ClickedItem as FeedItem;
            if (feed == null || feed.Kind != FeedItemKind.Video || feed.Video == null)
            {
                return;
            }

            var video = feed.Video;
            if (!string.IsNullOrEmpty(video.VideoId) && video.VideoId != _videoId)
            {
                AppNavigation.OpenVideo(this, video.VideoId);
            }
        }

        private void ShortShelfItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var border = sender as Border;
            var video = border != null ? border.DataContext as VideoItem : null;
            if (video != null && !string.IsNullOrEmpty(video.VideoId) && video.VideoId != _videoId)
            {
                AppNavigation.OpenVideo(this, video.VideoId);
            }
        }
    }
}
