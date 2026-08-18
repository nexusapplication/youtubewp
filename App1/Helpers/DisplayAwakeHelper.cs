using Windows.System.Display;

namespace App1.Helpers
{
    internal static class DisplayAwakeHelper
    {
        private static DisplayRequest _request;
        private static int _holdCount;

        public static void Acquire()
        {
            if (_holdCount == 0)
            {
                _request = new DisplayRequest();
                _request.RequestActive();
            }

            _holdCount++;
        }

        public static void Release()
        {
            if (_holdCount <= 0)
            {
                return;
            }

            _holdCount--;
            if (_holdCount == 0 && _request != null)
            {
                _request.RequestRelease();
                _request = null;
            }
        }
    }
}
