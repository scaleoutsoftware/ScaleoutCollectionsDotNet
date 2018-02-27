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
