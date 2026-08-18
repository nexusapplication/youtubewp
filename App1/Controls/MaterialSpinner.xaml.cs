using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;

namespace App1.Controls
{
    public sealed partial class MaterialSpinner : UserControl
    {
        private Storyboard _spinStoryboard;

        public MaterialSpinner()
        {
            InitializeComponent();
            Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170));
            Loaded += MaterialSpinner_Loaded;
            Unloaded += MaterialSpinner_Unloaded;
        }

        public bool IsActive
        {
            get { return (bool)GetValue(IsActiveProperty); }
            set { SetValue(IsActiveProperty, value); }
        }

        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(
                "IsActive",
                typeof(bool),
                typeof(MaterialSpinner),
                new PropertyMetadata(true, OnIsActiveChanged));

        private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var spinner = d as MaterialSpinner;
            if (spinner != null)
            {
                spinner.UpdateAnimation();
            }
        }

        private void MaterialSpinner_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateAnimation();
        }

        private void MaterialSpinner_Unloaded(object sender, RoutedEventArgs e)
        {
            StopAnimation();
        }

        private void UpdateAnimation()
        {
            if (IsActive && SpinnerRotate != null)
            {
                StartAnimation();
            }
            else
            {
                StopAnimation();
            }
        }

        private void StartAnimation()
        {
            if (_spinStoryboard != null)
            {
                return;
            }

            var animation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = new Duration(System.TimeSpan.FromMilliseconds(900)),
                RepeatBehavior = RepeatBehavior.Forever
            };

            _spinStoryboard = new Storyboard();
            _spinStoryboard.Children.Add(animation);
            Storyboard.SetTarget(animation, SpinnerRotate);
            Storyboard.SetTargetProperty(animation, "Angle");
            _spinStoryboard.Begin();
        }

        private void StopAnimation()
        {
            if (_spinStoryboard != null)
            {
                _spinStoryboard.Stop();
                _spinStoryboard = null;
            }

            if (SpinnerRotate != null)
            {
                SpinnerRotate.Angle = 0;
            }
        }
    }
}
