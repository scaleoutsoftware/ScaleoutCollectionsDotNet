using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Scaleout.Collections
{
    static class TlsRandom
    {
        // People are used to doing unsynchronized reads on a dictionary, but that
        // would break System.Random, which is stateful and quietly breaks if accessed
        // by multiple simultaneous callers. This class holds System.Random instances in
        // thread-local storage to protect it.
        private static ThreadLocal<Random> _tlsRand = new ThreadLocal<Random>(() => new Random(Seed: Thread.CurrentThread.ManagedThreadId));

        public static int Next()
        {
            return _tlsRand.Value.Next();
        }

        public static int Next(int maxValue)
        {
            return _tlsRand.Value.Next(maxValue);
        }

        public static int Next(int minValue, int maxValue)
        {
            return _tlsRand.Value.Next(minValue, maxValue);
        }

    }
}
