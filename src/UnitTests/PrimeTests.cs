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
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Scaleout.Collections;

namespace UnitTests
{
    public class PrimeTests
    {
        [Fact]
        public void NegativeThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Primes.Next(-42));
        }

        [Fact]
        public void ZeroOneTwo()
        {
            Assert.Equal(2, Primes.Next(0));
            Assert.Equal(2, Primes.Next(1));
            Assert.Equal(2, Primes.Next(2));
        }

        [Fact]
        public void VerifyPrimes()
        {
            Assert.Equal(3, Primes.Next(3));
            Assert.Equal(37, Primes.Next(32));
            Assert.Equal(1_299_827, Primes.Next(1_299_822));
        }
    }
}
