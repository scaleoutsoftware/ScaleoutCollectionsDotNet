﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Scaleout.Collections;

namespace UnitTests
{
    public class LruTests
    {
        [Fact]
        public void EmptyDict()
        {
            var ld = new LruDictionary<int, int>();
            Assert.Throws<KeyNotFoundException>(() => ld[42]);
        }

        [Fact]
        public void OneItem()
        {
            var ld = new LruDictionary<string, string>(1);
            ld["hello"] = "world";
            Assert.Equal("world", ld["hello"]);
            Assert.Throws<KeyNotFoundException>(() => ld["foo"]);
        }

        [Fact]
        public void UpdateItem()
        {
            var ld = new LruDictionary<string, int>();

            for (int i = 0; i < 10; i++)
                ld[i.ToString()] = i;

            ld["5"] = 42;
            Assert.Equal(42, ld["5"]);
            Assert.Equal(3, ld["3"]);
        }

        [Fact]
        public void BadAdd()
        {
            var ld = new LruDictionary<int, int>();
            ld.Add(42, 42);
            // Can't add when key already exists:
            Assert.Throws<ArgumentException>(() => ld.Add(42, 123));
        }

        [Fact]
        public void OneItemBigCapacity()
        {
            var ld = new LruDictionary<string, string>(100_000);
            ld["hello"] = "world";
            Assert.Equal("world", ld["hello"]);
            Assert.Throws<KeyNotFoundException>(() => ld["foo"]);
        }

        [Fact]
        public void CustomComparer()
        {
            var ld = new LruDictionary<string, string>(comparer: StringComparer.OrdinalIgnoreCase);
            ld["hello"] = "world";
            Assert.Equal("world", ld["HELLO"]);
            Assert.Throws<KeyNotFoundException>(() => ld["foo"]);
        }

        [Fact]
        public void TenThousand()
        {
            // start with low capacity to exercise resizing.
            var ld = new LruDictionary<string, int>(1);

            for (int i = 0; i < 10_000; i++)
            {
                ld[i.ToString()] = i;
            }

            Assert.Equal(10_000, ld.Count);

            for (int i = 0; i < 10_000; i++)
            {
                Assert.Equal(i, ld[i.ToString()]);
            }
        }



        [Fact]
        public void Remove()
        {
            var ld = new LruDictionary<string, int>();

            for (int i = 0; i < 10; i++)
            {
                ld[i.ToString()] = i;
            }
            Assert.Equal(10, ld.Count);

            // remove evens.
            for (int i = 0; i < 10; i += 2)
            {
                ld.Remove(i.ToString());
            }
            Assert.Equal(5, ld.Count);

            for (int i = 0; i < 10; i++)
            {
                bool found = ld.TryGetValue(i.ToString(), out int ret);
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
            var ld = new LruDictionary<string, int>();

            for (int i = 0; i < 10; i++)
            {
                ld[i.ToString()] = i;
            }

            ld.Remove("7");

            Assert.Equal(9, ld.Count);
            Assert.False(ld.ContainsKey("7"));
            Assert.True(ld.ContainsKey("8"));

            // put it back in, make sure everything's still ok:
            ld["7"] = 7;

            Assert.Equal(10, ld.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(i, ld[i.ToString()]);
            }
        }

        [Fact]
        public void RemoveAndResize()
        {
            var ld = new LruDictionary<string, int>();

            // Add first 500.
            for (int i = 0; i < 500; i++)
                ld.Add(i.ToString(), i);

            // remove everything divisible by 7
            for (int i = 0; i < 500; i += 7)
            {
                bool removed = ld.Remove(i.ToString());
                Assert.True(removed);
            }

            // Add 2000 more elements to force resizing.
            for (int i = 500; i < 2500; i++)
                ld.Add(i.ToString(), i);

            for (int i = 0; i < 2500; i++)
            {
                bool found = ld.TryGetValue(i.ToString(), out int ret);
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
            var ld = new LruDictionary<string, int>();

            for (int i = 0; i < 10; i++)
                ld.Add(i.ToString(), i);

            Assert.False(ld.Remove("12345"));
        }

        [Fact]
        public void CheckEnumerator()
        {
            var ld = new LruDictionary<string, int>();

            for (int i = 0; i < 10; i++)
                ld.Add(i.ToString(), i);

            bool removed = ld.Remove("9");
            Assert.True(removed);

            int counter = 0;
            foreach (var kvp in ld)
            {
                Assert.True(kvp.Value < 9);
                counter++;
            }

            Assert.Equal(9, counter);

        }

        [Fact]
        public void EnumerateEmpty()
        {
            var ld = new LruDictionary<string, int>();

            for (int i = 0; i < 10; i++)
                ld.Add(i.ToString(), i);

            ld.Clear();
            Assert.Empty(ld);

            foreach (var kvp in ld)
            {
                Assert.True(false, "shouldn't ever get here");
            }
        }

        [Fact]
        public void EnumerateKeys()
        {
            var ld = new LruDictionary<int, int>();
            for (int i = 0; i < 100; i++)
                ld.Add(i, i);

            // remove the last item to exercise tombstones.
            ld.Remove(99);

            bool[] returnedKeys = new bool[99];
            foreach (var key in ld.Keys)
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
            var ld = new LruDictionary<int, int>();
            for (int i = 0; i < 100; i++)
                ld.Add(i, i);

            // remove the last item to exercise tombstones.
            ld.Remove(99);

            bool[] returnedVals = new bool[99];
            foreach (var val in ld.Values)
            {
                Assert.False(returnedVals[val], "value was returned twice from enumerator");
                returnedVals[val] = true;
            }

            foreach (bool found in returnedVals)
            {
                Assert.True(found, "a value was not returned");
            }
        }

        [Fact]
        public void ContainsValue()
        {
            var ld = new LruDictionary<string, int>();
            for (int i = 0; i < 100; i++)
                ld.Add(i.ToString(), i);

            // remove something to exercise tombstones.
            ld.Remove("42");

            Assert.True(ld.ContainsValue(66));
            Assert.True(ld.Values.Contains(88));
            Assert.False(ld.ContainsValue(42));
            Assert.False(ld.ContainsValue(543434));
        }

        [Fact]
        public void Trim()
        {
            var ld = new LruDictionary<string, int>();
            for (int i = 0; i < 1234; i++)
                ld[i.ToString()] = i;

            int initialCap = ld.Capacity;
            Assert.True(initialCap > 1234);

            ld.Trim();
            Assert.Equal(500, ld["500"]);


            ld.Clear();
            ld.Trim();
            Assert.Equal(8, ld.Capacity);

        }

        public class BadHasher : IEqualityComparer<string>
        {
            public bool Equals(string x, string y)
            {
                return string.Equals(x, y, StringComparison.Ordinal);
            }

            public int GetHashCode(string obj)
            {
                return 0;
            }
        }

        [Fact]
        public void ZeroHash()
        {
            // Check hashcode of 0, which is a special case.
            var ld = new LruDictionary<string, string>(100, new BadHasher());
            ld.Add("foo", "bar");
            Assert.Equal("bar", ld["foo"]);
            Assert.True(ld.Remove("foo"));
        }

        [Fact]
        public void LotsOfCollisions()
        {
            // Make sure the dictionary still works, even when there are lots of collisions
            var ld = new LruDictionary<string, int>(comparer: new BadHasher());

            for (int i = 0; i < 1_000; i++)
                ld[i.ToString()] = i;

            Assert.Equal(1_000, ld.Count);

            for (int i = 0; i < 1_000; i++)
            {
                Assert.Equal(i, ld[i.ToString()]);
            }
        }

    }
}