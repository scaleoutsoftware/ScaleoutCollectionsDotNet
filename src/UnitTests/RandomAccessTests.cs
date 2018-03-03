using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Scaleout.Collections;

namespace UnitTests
{
    public class RandomAccessTests
    {
        [Fact]
        public void RemoveRandom()
        {
            var rd = new RouletteDictionary<string, int>();

            for (int i = 0; i < 10; i++)
                rd[i.ToString()] = i;

            bool removed = rd.RemoveRandom();
            Assert.True(removed);
            Assert.Equal(9, rd.Count);
        }

        [Fact]
        public void RemoveRandomFromMostlyEmpty()
        {
            var rd = new RouletteDictionary<string, int>(10_000);

            rd["Hello world"] = 42;

            bool removed = rd.RemoveRandom();
            Assert.True(removed);
            Assert.Empty(rd);
        }

        [Fact]
        public void RemoveAndGet()
        {
            var rd = new RouletteDictionary<string, string>(100);

            rd["foo"] = "bar";

            var removedKVP = rd.RemoveRandomAndGet();
            Assert.Equal("foo", removedKVP.Key);
            Assert.Equal("bar", removedKVP.Value);
            Assert.Empty(rd);
        }

        [Fact]
        public void GetRandom()
        {
            var rd = new RouletteDictionary<string, string>(42);

            rd["hello"] = "world";

            var key = rd.GetRandomKey();
            Assert.Equal("hello", key);

            var val = rd.GetRandomValue();
            Assert.Equal("world", val);

            var kvp = rd.GetRandomKeyAndValue();
            Assert.Equal("hello", kvp.Key);
            Assert.Equal("world", kvp.Value);

        }

    }
}
