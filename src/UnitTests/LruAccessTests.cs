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
    public class LruAccessTests
    {
        [Fact]
        public void RemoveLru()
        {
            var ld = new RecentDictionary<string, int>();
            for (int i = 0; i < 42; i++)
                ld.Add(i.ToString(), i);

            ld.RemoveLeastRecent();
            Assert.False(ld.ContainsKey("0"));
            Assert.True(ld.ContainsKey("1"));
            Assert.Equal(41, ld.Count);

            // Access the least-recently used element, make
            // sure it doesn't get removed.

            Assert.Equal(1, ld["1"]);
            ld.RemoveLeastRecent();
            Assert.True(ld.ContainsKey("1"));
            Assert.False(ld.ContainsKey("2"));

        }

        [Fact]
        public void SetAndMaintainCount()
        {
            var ld = new RecentDictionary<string, int>(1000, StringComparer.Ordinal);

            for (int i = 0; i < 10; i++)
                ld[i.ToString()] = i;

            ld.SetAndMaintainCount("foo", 42);
            Assert.Equal(10, ld.Count);
            Assert.True(ld.ContainsKey("foo"));
        }

        [Fact]
        public void SetAndMaintainCountKnownBucketCollision()
        {
            var ld = new RecentDictionary<int, int>(RecentDictionaryEvictionMode.LRU);
            Assert.Equal(8, ld.Capacity);

            for (int i = 0; i < 3; i++)
                ld[i] = i;

            // assuming initial bucket capacity of 8, this will
            // collide in bucket 0, which also contains the LRU item:
            ld.SetAndMaintainCount(8, 42);
            Assert.Equal(3, ld.Count);
            Assert.True(ld.ContainsKey(1));
            Assert.True(ld.ContainsKey(2));
            Assert.True(ld.ContainsKey(8));
        }

        [Fact]
        public void SetAndMaintainCountKnownBucketCollision2()
        {
            var ld = new RecentDictionary<int, int>(RecentDictionaryEvictionMode.LRU);
            Assert.Equal(8, ld.Capacity);

            // assuming initial bucket capacity of 8, these will
            // pile into bucket 0:
            ld.Add(0, 0);
            ld.Add(8, 8);
            ld.Add(16, 16);
            ld.Add(24, 24);

            // reverse the order in the LRU list
            ld.TryGetValue(16, out int a);
            ld.TryGetValue(8, out int b);
            ld.TryGetValue(0, out int c);
            Assert.Equal(24, ld.LeastRecent.Key);
            Assert.Equal(0, ld.MostRecent.Key);

            // Set/evict:
            ld.SetAndMaintainCount(32, 32);

            Assert.Equal(4, ld.Count);
            Assert.True(ld.ContainsKey(0));
            Assert.True(ld.ContainsKey(8));
            Assert.True(ld.ContainsKey(16));
            Assert.False(ld.ContainsKey(24)); // should have been evicted
            Assert.True(ld.ContainsKey(32));
            Assert.Equal(16, ld.LeastRecent.Key);
            Assert.Equal(32, ld.MostRecent.Key);
        }

        [Fact]
        public void SetAndMaintainCountKnownBucketCollision3()
        {
            var ld = new RecentDictionary<int, int>(RecentDictionaryEvictionMode.LRU);
            Assert.Equal(8, ld.Capacity);

            // assuming initial bucket capacity of 8, these will
            // pile into bucket 0:
            ld.Add(0, 0);
            ld.Add(8, 8);
            ld.Add(16, 16);
            ld.Add(24, 24);

            // Change the order in the LRU list
            ld.TryGetValue(0, out int c);
            Assert.Equal(8, ld.LeastRecent.Key);
            Assert.Equal(0, ld.MostRecent.Key);

            // Set/evict:
            ld.SetAndMaintainCount(32, 32);

            Assert.Equal(4, ld.Count);
            Assert.True(ld.ContainsKey(0));
            Assert.False(ld.ContainsKey(8)); // should have been evicted
            Assert.True(ld.ContainsKey(16));
            Assert.True(ld.ContainsKey(24)); 
            Assert.True(ld.ContainsKey(32));
            Assert.Equal(16, ld.LeastRecent.Key);
            Assert.Equal(32, ld.MostRecent.Key);
        }

        [Fact]
        public void EnumerateMruToLru()
        {
            var ld = new RecentDictionary<string, int>(1000, StringComparer.Ordinal);

            for (int i = 0; i < 10; i++)
                ld[i.ToString()] = i;

            Assert.Equal("9", ld.Keys.First());
            Assert.Equal(0, ld.Values.Last());
        }
    }
}
