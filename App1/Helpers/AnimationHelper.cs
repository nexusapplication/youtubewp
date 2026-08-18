using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media.Animation;

namespace App1.Helpers
{
    internal static class AnimationHelper
    {
        public static void FadeTo(UIElement target, double to, int durationMs, Action onComplete)
        {
            if (target == null)
            {
                if (onComplete != null)
                {
                    onComplete();
                }

                return;
            }

            var animation = new DoubleAnimation
            {
                To = to,
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var storyboard = new Storyboard();
            storyboard.Children.Add(animation);
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, "Opacity");
            if (onComplete != null)
            {
                storyboard.Completed += (s, e) => onComplete();
            }

            storyboard.Begin();
        }

        public static void Show(UIElement target, int durationMs)
        {
            if (target == null)
            {
                return;
            }

            target.Visibility = Visibility.Visible;
            target.Opacity = 0;
            FadeTo(target, 1, durationMs, null);
        }

        public static void Hide(UIElement target, int durationMs)
        {
            if (target == null)
            {
                return;
            }

            FadeTo(target, 0, durationMs, () => target.Visibility = Visibility.Collapsed);
        }
    }
}
