using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.CompilerServices;

namespace Scaleout.Collections
{
    // Basic brute force prime number finder (with a little intelligence to avoid checking even
    // numbers and to stop checking after sqrt(n). Based on "Implementation 4" from
    // https://stackoverflow.com/a/5694432/3634591

    // This class is not currently used--collections in this assembly are now sized to be
    // a power of two instead of prime. Keeping this class for now in case we revisit this
    // decision.

    static class Primes
    {
        /// <summary>
        /// Gets the smallest prime that is greater than or equal to <paramref name="minValue"/>.
        /// </summary>
        /// <param name="minValue">Minimum allowed value for the next prime number</param>
        /// <returns>Next prime.</returns>
        public static int Next(int minValue)
        {
            if (minValue < 0)
                throw new ArgumentOutOfRangeException(nameof(minValue));

            if (minValue <= 2)
                return 2;

            // Is minValue even? If so, increment before we start checking.
            if (!((minValue & 1) == 1)) 
                ++minValue;

            while (true)
            {
                if (IsPrime(minValue))
                    return minValue;
                else
                    minValue += 2; // only check odd numbers
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsPrime(int n)
        {
            for (int i = 3; true; i += 2)
            {
                int quotient = n / i;

                if (quotient < i)
                    return true; // Made it past the square root of n. This is a prime.

                if (n == (quotient * i))  // (allegedly faster than modulo)
                    return false;
            }
        }

    }
}
