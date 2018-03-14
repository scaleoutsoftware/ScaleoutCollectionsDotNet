/* Copyright 2018 ScaleOut Software, Inc.
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
            var ld = new LruDictionary<string, int>();
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
            var ld = new LruDictionary<string, int>(1000, StringComparer.Ordinal);

            for (int i = 0; i < 10; i++)
                ld[i.ToString()] = i;

            ld.SetAndMaintainCount("foo", 42);
            Assert.Equal(10, ld.Count);
            Assert.True(ld.ContainsKey("foo"));
        }

        [Fact]
        public void EnumerateMruToLru()
        {
            var ld = new LruDictionary<string, int>(1000, StringComparer.Ordinal);

            for (int i = 0; i < 10; i++)
                ld[i.ToString()] = i;

            Assert.Equal("9", ld.Keys.First());
            Assert.Equal(0, ld.Values.Last());
        }
    }
}
