using System;
using Windows.UI.Xaml.Media.Imaging;

namespace App1.Helpers
{
    internal static class ImageUrl
    {
        public static string VideoThumb(string videoId)
        {
            if (string.IsNullOrEmpty(videoId))
            {
                return "";
            }

            return "https://i.ytimg.com/vi/" + videoId + "/mqdefault.jpg";
        }

        public static BitmapImage ToBitmap(string url, int size)
        {
            url = Normalize(url, size);
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            try
            {
                return ImageCache.Get(url, size);
            }
            catch
            {
                return null;
            }
        }

        public static string Normalize(string url, int size)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return "";
            }

            url = url.Trim();
            if (url.StartsWith("//", StringComparison.Ordinal))
            {
                url = "https:" + url;
            }
            else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url.Substring(7);
            }

            var videoId = ExtractVideoId(url);
            if (!string.IsNullOrEmpty(videoId))
            {
                return VideoThumb(videoId);
            }

            url = url.Replace("yt3.googleusercontent.com", "yt3.ggpht.com");
            url = url.Replace("lh3.googleusercontent.com", "yt3.ggpht.com");
            url = url.Replace("yt4.ggpht.com", "yt3.ggpht.com");
            url = url.Replace(".webp", ".jpg");
            url = url.Replace("-rw", "-rj");
            url = url.Replace("-rp", "-rj");

            if (url.IndexOf("ggpht.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                url.IndexOf("googleusercontent.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ForceJpeg(url, size > 0 ? size : 88);
            }

            var query = url.IndexOf('?');
            if (query > 0)
            {
                url = url.Substring(0, query);
            }

            return url;
        }

        private static string ForceJpeg(string url, int size)
        {
            var cut = url.IndexOf("=s");
            if (cut < 0)
            {
                cut = url.IndexOf('?');
            }

            var root = cut > 0 ? url.Substring(0, cut) : url;
            return root + "=s" + size.ToString() + "-c-k-c0x00ffffff-no-rj";
        }

        private static string ExtractVideoId(string url)
        {
            var markers = new string[] { "/vi/", "/vi_webp/", "/an_webp/" };
            for (int i = 0; i < markers.Length; i++)
            {
                var index = url.IndexOf(markers[i], StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    continue;
                }

                var start = index + markers[i].Length;
                var end = url.IndexOf('/', start);
                if (end < 0)
                {
                    end = url.Length;
                }

                var id = url.Substring(start, end - start);
                if (id.Length == 11)
                {
                    return id;
                }
            }

            return "";
        }
    }
}
