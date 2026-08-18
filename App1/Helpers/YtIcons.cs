using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace App1.Helpers
{
    internal static class YtIcons
    {
        public const string Base = "ms-appx:///youtubeicons/";

        public static BitmapImage Image(string file)
        {
            return new BitmapImage(new System.Uri(Base + file));
        }

        public static void Set(Image image, string file)
        {
            image.Source = Image(file);
        }

        public static void SetToggle(Image image, string name, bool filled)
        {
            Set(image, filled ? name + "filled.png" : name + "hollow.png");
        }
    }
}
