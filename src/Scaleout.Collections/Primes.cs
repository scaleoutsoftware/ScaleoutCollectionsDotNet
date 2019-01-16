/* Copyright 2019 ScaleOut Software, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

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
