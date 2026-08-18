using App1.Helpers;
using System;
using System.Collections.ObjectModel;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace App1
{
    public sealed partial class SettingsPage : Page
    {
        private readonly ObservableCollection<string> _instances = new ObservableCollection<string>();
        private bool _suppressAuto;
        private bool _deviceCodeHooked;
        private MiniPlayerUiHelper _miniPlayerUi;

        public SettingsPage()
        {
            this.InitializeComponent();
            InstanceList.ItemsSource = _instances;
            _miniPlayerUi = new MiniPlayerUiHelper(
                MiniPlayer,
                MiniPlayerBar,
                MiniPlayerRow,
                MiniPlayerTitle,
                MiniPlayerAuthor,
                MiniPlayerThumb,
                MiniPlayerPlayIcon);
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            _miniPlayerUi.SyncFromSession();
            UpdateBottomNav(-1);

            _suppressAuto = true;
            InnerTubeRadio.IsChecked = App.Api.UseInnerTube;
            InvidiousRadio.IsChecked = !App.Api.UseInnerTube;
            AutoBox.IsChecked = !App.Api.UseCustomInstance;
            InstanceBox.Text = App.Api.Invidious.CurrentInstance ?? "";
            GoogleClientIdBox.Text = App.Api.GoogleAuth.ClientId ?? "";
            GoogleClientSecretBox.Password = App.Api.GoogleAuth.ClientSecret ?? "";
            GoogleDeviceCodePanel.Visibility = Visibility.Collapsed;
            UpdateGoogleUi();
            UpdateBackendUi();
            _suppressAuto = false;
            if (!App.Api.UseInnerTube)
            {
                await LoadInstancesAsync(false);
            }
        }

        public void HandleHardwareBack()
        {
            if (Frame != null && Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private void UpdateBackendUi()
        {
            InvidiousPanel.Visibility = App.Api.UseInnerTube ? Visibility.Collapsed : Visibility.Visible;
            StatusText.Text = "Using " + App.Api.StatusLabel;
        }

        private void UpdateGoogleUi()
        {
            var signedIn = App.Api.GoogleAuth.IsSignedIn;
            GoogleSignOutButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
            GoogleSignInButton.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
            GoogleStatusText.Text = signedIn
                ? "Signed in. Playlists and history are available on the You tab."
                : "Not signed in. Use a TVs and Limited Input devices OAuth client ID from Google Cloud Console.";
        }

        private void OnDeviceCodeReceived(string verificationUrl, string userCode)
        {
            GoogleDeviceCodePanel.Visibility = Visibility.Visible;
            GoogleVerificationUrlText.Text = verificationUrl;
            GoogleUserCodeText.Text = userCode;
            GoogleStatusText.Text = "Waiting for you to approve sign-in on Google...";
        }

        private async void GoogleSignIn_Click(object sender, RoutedEventArgs e)
        {
            App.Api.GoogleAuth.ClientId = GoogleClientIdBox.Text;
            App.Api.GoogleAuth.ClientSecret = GoogleClientSecretBox.Password;
            GoogleDeviceCodePanel.Visibility = Visibility.Collapsed;
            GoogleStatusText.Text = "Requesting sign-in code from Google...";
            GoogleSignInButton.IsEnabled = false;
            GoogleSignInProgress.IsActive = true;
            if (!_deviceCodeHooked)
            {
                App.Api.GoogleAuth.DeviceCodeReceived += OnDeviceCodeReceived;
                _deviceCodeHooked = true;
            }
            try
            {
                var ok = await App.Api.GoogleAuth.SignInAsync();
                UpdateGoogleUi();
                if (ok)
                {
                    GoogleDeviceCodePanel.Visibility = Visibility.Collapsed;
                    GoogleStatusText.Text = "Signed in successfully.";
                    App.Api.ResetHomeFeed();
                }
                else
                {
                    var err = App.Api.GoogleAuth.LastError;
                    GoogleStatusText.Text = string.IsNullOrEmpty(err)
                        ? "Sign-in was cancelled."
                        : err;
                }
            }
            catch (Exception ex)
            {
                GoogleStatusText.Text = ex.Message;
            }
            finally
            {
                GoogleSignInProgress.IsActive = false;
                GoogleSignInButton.IsEnabled = true;
            }
        }

        private void GoogleSignOut_Click(object sender, RoutedEventArgs e)
        {
            App.Api.GoogleAuth.SignOut();
            App.Api.ResetHomeFeed();
            GoogleDeviceCodePanel.Visibility = Visibility.Collapsed;
            UpdateGoogleUi();
            GoogleStatusText.Text = "Signed out.";
        }

        private async System.Threading.Tasks.Task LoadInstancesAsync(bool force)
        {
            _instances.Clear();
            try
            {
                var list = await App.Api.GetInstancesAsync(force);
                foreach (var item in list)
                {
                    _instances.Add(item);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Couldn't refresh instances. " + ex.Message;
            }
        }

        private async void Backend_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressAuto)
            {
                return;
            }

            App.Api.UseInnerTube = InnerTubeRadio.IsChecked == true;
            UpdateBackendUi();
            if (!App.Api.UseInnerTube && _instances.Count == 0)
            {
                await LoadInstancesAsync(false);
            }
        }

        private void AutoBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressAuto)
            {
                return;
            }

            if (AutoBox.IsChecked == true)
            {
                App.Api.UseAutomaticInstance();
                StatusText.Text = "Automatic instance selection enabled.";
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var url = InstanceBox.Text;
            if (string.IsNullOrWhiteSpace(url))
            {
                StatusText.Text = "Enter an instance URL first.";
                return;
            }

            App.Api.SetCustomInstance(url);
            _suppressAuto = true;
            AutoBox.IsChecked = false;
            _suppressAuto = false;
            StatusText.Text = "Saved " + App.Api.Invidious.CurrentInstance;
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Refreshing instance list...";
            await LoadInstancesAsync(true);
            StatusText.Text = "Loaded " + _instances.Count + " instances.";
        }

        private void InstanceList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var url = e.ClickedItem as string;
            if (string.IsNullOrEmpty(url))
            {
                return;
            }

            InstanceBox.Text = url;
            App.Api.SetCustomInstance(url);
            _suppressAuto = true;
            AutoBox.IsChecked = false;
            _suppressAuto = false;
            StatusText.Text = "Using " + url;
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
