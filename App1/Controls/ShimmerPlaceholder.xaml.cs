using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;

namespace App1.Controls
{
    public sealed partial class ShimmerPlaceholder : UserControl
    {
        private Storyboard _shineStoryboard;

        public ShimmerPlaceholder()
        {
            InitializeComponent();
            Loaded += ShimmerPlaceholder_Loaded;
            Unloaded += ShimmerPlaceholder_Unloaded;
        }

        public bool ShowSpinner
        {
            get { return (bool)GetValue(ShowSpinnerProperty); }
            set { SetValue(ShowSpinnerProperty, value); }
        }

        public static readonly DependencyProperty ShowSpinnerProperty =
            DependencyProperty.Register(
                "ShowSpinner",
                typeof(bool),
                typeof(ShimmerPlaceholder),
                new PropertyMetadata(false, OnShowSpinnerChanged));

        private static void OnShowSpinnerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var placeholder = d as ShimmerPlaceholder;
            if (placeholder != null)
            {
                placeholder.UpdateSpinner();
            }
        }

        private void ShimmerPlaceholder_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateSpinner();
            StartShine();
        }

        private void ShimmerPlaceholder_Unloaded(object sender, RoutedEventArgs e)
        {
            StopShine();
        }

        private void UpdateSpinner()
        {
            if (Spinner == null)
            {
                return;
            }

            Spinner.Visibility = ShowSpinner ? Visibility.Visible : Visibility.Collapsed;
            Spinner.IsActive = ShowSpinner;
        }

        private void StartShine()
        {
            if (_shineStoryboard != null || ShineTransform == null)
            {
                return;
            }

            var width = ActualWidth > 0 ? ActualWidth + 360 : 520;
            var animation = new DoubleAnimation
            {
                From = -220,
                To = width,
                Duration = new Duration(System.TimeSpan.FromMilliseconds(1200)),
                RepeatBehavior = RepeatBehavior.Forever
            };

            _shineStoryboard = new Storyboard();
            _shineStoryboard.Children.Add(animation);
            Storyboard.SetTarget(animation, ShineTransform);
            Storyboard.SetTargetProperty(animation, "X");
            _shineStoryboard.Begin();
        }

        private void StopShine()
        {
            if (_shineStoryboard != null)
            {
                _shineStoryboard.Stop();
                _shineStoryboard = null;
            }
        }
    }
}
