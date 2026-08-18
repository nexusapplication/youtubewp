using System;
using System.Collections.Generic;
using Windows.UI.Xaml.Media.Imaging;

namespace App1.Helpers
{
    internal static class ImageCache
    {
        private const int MaxEntries = 150;
        private static readonly Dictionary<string, BitmapImage> Cache = new Dictionary<string, BitmapImage>();
        private static readonly Queue<string> Order = new Queue<string>();

        public static BitmapImage Get(string url, int decodeWidth)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            var key = decodeWidth + "|" + url;
            BitmapImage cached;
            if (Cache.TryGetValue(key, out cached))
            {
                return cached;
            }

            try
            {
                var image = new BitmapImage();
                if (decodeWidth > 0)
                {
                    image.DecodePixelWidth = decodeWidth;
                }

                var uri = new Uri(url, UriKind.Absolute);
                Put(key, image);
                UiDispatcher.RunLow(() =>
                {
                    try
                    {
                        if (image.UriSource == null)
                        {
                            image.UriSource = uri;
                        }
                    }
                    catch
                    {
                    }
                });

                return image;
            }
            catch
            {
                return null;
            }
        }

        private static void Put(string key, BitmapImage image)
        {
            if (Cache.ContainsKey(key))
            {
                return;
            }

            while (Order.Count >= MaxEntries)
            {
                var old = Order.Dequeue();
                Cache.Remove(old);
            }

            Cache[key] = image;
            Order.Enqueue(key);
        }
    }
}
