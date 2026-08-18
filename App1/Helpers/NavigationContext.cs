namespace App1.Helpers
{
    internal enum BackDestination
    {
        Main,
        Channel
    }

    internal static class NavigationContext
    {
        public static BackDestination Destination { get; private set; }
        public static int MainTabIndex { get; private set; }
        public static string ChannelId { get; private set; }

        public static void SetMain(int tabIndex)
        {
            Destination = BackDestination.Main;
            MainTabIndex = tabIndex;
            ChannelId = null;
        }

        public static void SetChannel(string channelId)
        {
            Destination = BackDestination.Channel;
            ChannelId = channelId ?? "";
            MainTabIndex = 0;
        }
    }

}
