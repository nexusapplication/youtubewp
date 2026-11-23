using App1.Helpers;
using App1.Services;
using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Phone.UI.Input;
using Windows.Security.Authentication.Web;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace App1
{
    public sealed partial class App : Application
    {
        public static YouTubeApi Api { get; private set; }
        public static LocalLibrary Library { get; private set; }

        public App()
        {
            this.InitializeComponent();
            Api = new YouTubeApi();
            Library = new LocalLibrary();
            this.Suspending += this.OnSuspending;
            HardwareButtons.BackPressed += HardwareButtons_BackPressed;
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            var statusBar = StatusBar.GetForCurrentView();
            statusBar.BackgroundColor = Colors.Black;
            statusBar.ForegroundColor = Colors.White;
            statusBar.BackgroundOpacity = 1;
            await statusBar.ShowAsync();
            await Library.InitializeAsync();
            UiDispatcher.Bind(Window.Current.Dispatcher);

            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.CacheSize = 8;
                rootFrame.Language = Windows.Globalization.ApplicationLanguages.Languages[0];
                rootFrame.ContentTransitions = null;
                Window.Current.Content = rootFrame;
            }
            else
            {
                rootFrame.ContentTransitions = null;
            }

            if (rootFrame.Content == null)
            {
                if (!rootFrame.Navigate(typeof(MainPage), e.Arguments))
                {
                    throw new Exception("Failed to create initial page");
                }
            }

            Window.Current.Activate();
        }

        protected override void OnActivated(IActivatedEventArgs args)
        {
            if (args.Kind == ActivationKind.WebAuthenticationBrokerContinuation)
            {
                var continuation = args as IWebAuthenticationBrokerContinuationEventArgs;
                if (continuation != null)
                {
                    Api.GoogleAuth.CompleteSignIn(continuation.WebAuthenticationResult);
                }
            }

            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.CacheSize = 8;
                rootFrame.ContentTransitions = null;
                Window.Current.Content = rootFrame;
            }

            if (rootFrame.Content == null)
            {
                // Cold start through activation (the app was terminated while a
                // web sign-in was open): there is no page yet, so bring up the
                // main one instead of activating an empty window.
                rootFrame.Navigate(typeof(MainPage), null);
            }

            Window.Current.Activate();
            base.OnActivated(args);
        }

        private void HardwareButtons_BackPressed(object sender, BackPressedEventArgs e)
        {
            var frame = Window.Current.Content as Frame;
            if (frame == null)
            {
                return;
            }

            var videoPage = frame.Content as VideoPage;
            if (videoPage != null)
            {
                e.Handled = true;
                videoPage.HandleHardwareBack();
                return;
            }

            var channelPage = frame.Content as ChannelPage;
            if (channelPage != null)
            {
                e.Handled = true;
                channelPage.HandleHardwareBack();
                return;
            }

            var settingsPage = frame.Content as SettingsPage;
            if (settingsPage != null)
            {
                e.Handled = true;
                settingsPage.HandleHardwareBack();
                return;
            }

            var main = frame.Content as MainPage;
            if (main != null && main.HandleBack())
            {
                e.Handled = true;
                return;
            }

            if (frame.CanGoBack)
            {
                e.Handled = true;
                frame.GoBack();
            }
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            deferral.Complete();
        }
    }
}