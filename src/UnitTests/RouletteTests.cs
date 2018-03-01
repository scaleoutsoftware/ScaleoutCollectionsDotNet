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
        public void UpdateItem()
        {
            var rd = new RouletteDictionary<string, int>();

            for (int i = 0; i < 10; i++)
                rd[i.ToString()] = i;

            rd["5"] = 42;
            Assert.Equal(42, rd["5"]);
            Assert.Equal(3, rd["3"]);
        }

        [Fact]
        public void BadAdd()
        {
            var rd = new RouletteDictionary<int, int>();
            rd.Add(42, 42);
            // Can't add when key already exists:
            Assert.Throws<ArgumentException>(() => rd.Add(42, 123));
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

        

        [Fact]
        public void Remove()
        {
            var rd = new RouletteDictionary<string, int>();

            for (int i = 0; i < 10; i++)
            {
                rd[i.ToString()] = i;
            }
            Assert.Equal(10, rd.Count);

            // remove evens.
            for (int i = 0; i < 10; i += 2)
            {
                rd.Remove(i.ToString());
            }
            Assert.Equal(5, rd.Count);

            for (int i = 0; i < 10; i++)
            {
                bool found = rd.TryGetValue(i.ToString(), out int ret);
                if (i % 2 == 0)
                {
                    Assert.False(found);
                }
                else
                {
                    Assert.True(found);
                    Assert.Equal(i, ret);
                }
            }
        }

        [Fact]
        public void RemoveAddAgain()
        {
            var rd = new RouletteDictionary<string, int>();

            for (int i = 0; i < 10; i++)
            {
                rd[i.ToString()] = i;
            }
            
            rd.Remove("7");

            Assert.Equal(9, rd.Count);
            Assert.False(rd.ContainsKey("7"));
            Assert.True(rd.ContainsKey("8"));

            // put it back in, make sure everything's still ok:
            rd["7"] = 7;

            Assert.Equal(10, rd.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(i, rd[i.ToString()]);
            }
        }

        [Fact]
        public void RemoveAndResize()
        {
            var rd = new RouletteDictionary<string, int>();

            // Add first 500.
            for (int i = 0; i < 500; i++)
                rd.Add(i.ToString(), i);

            // remove everything divisible by 7
            for (int i = 0; i < 500; i += 7)
            {
                bool removed = rd.Remove(i.ToString());
                Assert.True(removed);
            }

            // Add 2000 more elements to force resizing.
            for (int i = 500; i < 2500; i++)
                rd.Add(i.ToString(), i);

            for (int i = 0; i < 2500; i++)
            {
                bool found = rd.TryGetValue(i.ToString(), out int ret);
                if ((i < 500) && (i % 7 == 0))
                {
                    Assert.False(found);
                }
                else
                {
                    Assert.True(found);
                    Assert.Equal(i, ret);
                }
            }
        }

        [Fact]
        public void RemoveMissing()
        {
            var rd = new RouletteDictionary<string, int>();

            for (int i = 0; i < 10; i++)
                rd.Add(i.ToString(), i);

            Assert.False(rd.Remove("12345"));
        }

        [Fact]
        public void CheckEnumerator()
        {
            var rd = new RouletteDictionary<string, int>();

            for (int i = 0; i < 10; i++)
                rd.Add(i.ToString(), i);

            bool removed = rd.Remove("9");
            Assert.True(removed);

            int counter = 0;
            foreach (var kvp in rd)
            {
                Assert.True(kvp.Value < 9);
                counter++;
            }

            Assert.Equal(9, counter);

        }

        [Fact]
        public void EnumerateEmpty()
        {
            var rd = new RouletteDictionary<string, int>();

            for (int i = 0; i < 10; i++)
                rd.Add(i.ToString(), i);

            rd.Clear();
            Assert.Empty(rd);

            foreach (var kvp in rd)
            {
                Assert.True(false, "shouldn't ever get here");
            }
        }

        [Fact]
        public void EnumerateKeys()
        {
            var rd = new RouletteDictionary<int, int>();
            for (int i = 0; i < 100; i++)
                rd.Add(i, i);

            // remove the last item to exercise tombstones.
            rd.Remove(99);

            bool[] returnedKeys = new bool[99];
            foreach (var key in rd.Keys)
            {
                Assert.False(returnedKeys[key], "key was returned twice from enumerator");
                returnedKeys[key] = true;
            }

            foreach (bool found in returnedKeys)
            {
                Assert.True(found, "a key was not returned");
            }
        }

        [Fact]
        public void EnumerateValues()
        {
            var rd = new RouletteDictionary<int, int>();
            for (int i = 0; i < 100; i++)
                rd.Add(i, i);

            // remove the last item to exercise tombstones.
            rd.Remove(99);

            bool[] returnedVals = new bool[99];
            foreach (var val in rd.Values)
            {
                Assert.False(returnedVals[val], "value was returned twice from enumerator");
                returnedVals[val] = true;
            }

            foreach (bool found in returnedVals)
            {
                Assert.True(found, "a value was not returned");
            }
        }


    } // class
}
