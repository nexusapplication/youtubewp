using System;

using System.Runtime.InteropServices.WindowsRuntime;

using System.Threading.Tasks;

using Windows.ApplicationModel.Core;

using Windows.UI.Core;

using Windows.UI.Xaml;



namespace App1.Helpers

{

    internal static class UiDispatcher

    {

        private static CoreDispatcher _cachedDispatcher;

        public static void Bind(CoreDispatcher dispatcher)
        {
            if (dispatcher != null)
            {
                _cachedDispatcher = dispatcher;
            }
        }



        public static Task RunAsync(CoreDispatcherPriority priority, Action action)

        {

            var dispatcher = GetDispatcher();

            if (dispatcher == null || action == null)

            {

                return Task.FromResult(0);

            }



            return dispatcher.RunAsync(priority, () => action()).AsTask();

        }



        public static Task RunLow(Action action)

        {

            return RunAsync(CoreDispatcherPriority.Low, action);

        }



        public static Task RunNormal(Action action)

        {

            return RunAsync(CoreDispatcherPriority.Normal, action);

        }



        private static CoreDispatcher GetDispatcher()

        {

            if (_cachedDispatcher != null)

            {

                return _cachedDispatcher;

            }



            if (Window.Current != null)

            {

                _cachedDispatcher = Window.Current.Dispatcher;

                return _cachedDispatcher;

            }



            try

            {

                var mainView = CoreApplication.MainView;

                if (mainView != null && mainView.CoreWindow != null)

                {

                    _cachedDispatcher = mainView.CoreWindow.Dispatcher;

                    return _cachedDispatcher;

                }

            }

            catch

            {

            }



            return null;

        }

    }

}


