using System;
using System.Threading.Tasks;

namespace App1.Helpers
{
    internal static class BackgroundWork
    {
        public static Task Run(Action action)
        {
            return Task.Run(action);
        }

        public static Task<T> Run<T>(Func<T> func)
        {
            return Task.Run(func);
        }
    }
}
