using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Scaleout.Collections;

namespace UnitTests
{
    public class RouletteTests
    {
        [Fact]
        public void EmptyDict()
        {
            var rd = new RouletteDictionary<int, int>();
            Assert.Throws<KeyNotFoundException>(() => rd[42]);
        }

        [Fact]
        public void OneItem()
        {
            var rd = new RouletteDictionary<string, string>(1);
            rd["hello"] = "world";
            Assert.Equal("world", rd["hello"]);
            Assert.Throws<KeyNotFoundException>(() => rd["foo"]);
        }

        [Fact]
        public void OneItemBigCapacity()
        {
            var rd = new RouletteDictionary<string, string>(100_000);
            rd["hello"] = "world";
            Assert.Equal("world", rd["hello"]);
            Assert.Throws<KeyNotFoundException>(() => rd["foo"]);
        }

        [Fact]
        public void CustomComparer()
        {
            var rd = new RouletteDictionary<string, string>(comparer: StringComparer.OrdinalIgnoreCase);
            rd["hello"] = "world";
            Assert.Equal("world", rd["HELLO"]);
            Assert.Throws<KeyNotFoundException>(() => rd["foo"]);
        }

        [Fact]
        public void TenThousand()
        {
            // start with low capacity to exercise resizing.
            var rd = new RouletteDictionary<string, int>(1);

            for (int i = 0; i < 10_000; i++)
            {
                rd[i.ToString()] = i;
            }

            Assert.Equal(10_000, rd.Count);

            for (int i = 0; i < 10_000; i++)
            {
                Assert.Equal(i, rd[i.ToString()]);
            }
        }
    }
}
