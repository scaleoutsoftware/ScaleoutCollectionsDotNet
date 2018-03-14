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
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;


namespace Scaleout.Collections
{
    /// <summary>
    /// Enumeration specifying which entry to evict when calling
    /// <see cref="RecentDictionary{TKey, TValue}.SetAndMaintainCount(TKey, TValue)"/>.
    /// Used when constructing a RecentDictionary instance.
    /// </summary>
    public enum RecentDictionaryEvictionMode {
        /// <summary>
        /// Remove the least recently accessed dictionary item.
        /// </summary>
        LRU,
        /// <summary>
        /// Remove the most recently accessed dictionary item.
        /// </summary>
        MRU
    };

    /// <summary>
    /// A collection of keys and values that tracks the order in which entries
    /// are accessed.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of the values in the dictionary.</typeparam>
    /// <remarks>
    /// <para>
    /// This dictionary is intended to be used as a building block for caches that perform
    /// LRU eviction. The <see cref="SetAndMaintainCount(TKey, TValue)"/> method is 
    /// the primary method for this use case. It allows entries to be added/updated in the 
    /// dictionary and removes either the least-recently-used or most-recently-used item
    /// if the operation results in an add to prevent unbounded growth.
    /// </para>
    /// </remarks>
    [DebuggerDisplay("Count = {Count}")]
    public sealed class RecentDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        // A hybrid LinkedList/hashtable collection for fast lookups by key
        // that maintains the order in which element are accessed.
        //  - Collision resolution: chaining.
        //  - Bucket count: always a power of two.
        //  - Possible TODO: The SetAndMaintainCount() signature is identical to
        //     RouletteDictionary... consider a common "EvictionDictionary"
        //     abstract base class.

        Node[] _buckets;
        Node _lruHead;
        Node _lruTail;
        RecentDictionaryEvictionMode _evictMode = RecentDictionaryEvictionMode.LRU;
        private int _count = 0;
        private int _bucketMask; // applied to hashes to select buckets (faster than modulo, but requires a good hash function).

        IEqualityComparer<TKey> _comparer;

        // Cached references to collections returned by Keys and Values properties.
        private KeyCollection _keys = null;
        private ValueCollection _values = null;

        // This dictionary is intended for use as a building block
        // for a fixed-size cache. We maintain a small list of free 
        // bucket nodes to reduce allocation/GC overhead when 
        // such a cache is operating at/near capacity.
        private const int MaxFreeNodes = 10;
        Node _freeList;
        int _freeCount;

        
        // Node containing an item in the collection.
        private class Node
        {
            public int HashCode;
            
            // Next node in hash table bucket.
            public Node Next;

            // Previous node in hash table bucket.
            public Node Previous;

            // Next (older, less recently accessed) node in LRU list.
            public Node LruNext;

            // Previous (newer, more recently accessed) node in LRU list.
            public Node LruPrevious;

            public TKey Key;
            public TValue Value;

            public Node(int hash, Node prev, Node next, Node lruPrev, Node lruNext, TKey key, TValue val)
            {
                HashCode = hash;
                Next = next;
                Previous = prev;
                LruNext = lruNext;
                LruPrevious = lruPrev;
                Key = key;
                Value = val;
            }
        }

        private void ReleaseNode(Node node)
        {
            node.Previous = null;
            node.LruPrevious = null;
            node.LruNext = null;
            node.Key = default(TKey);
            node.Value = default(TValue);

            if (_freeCount <= MaxFreeNodes)
            {
                // add to the front of the list of free nodes.
                node.Next = _freeList;
                _freeList = node;
                _freeCount++;
            }
            else
            {
                node.Next = null;
            }
        }

        private Node AcquireNewNode(int hash, Node prev, Node next, Node lruPrev, Node lruNext, TKey key, TValue val)
        {
            if (_freeCount > 0)
            {
                Node ret = _freeList;
                _freeList = ret.Next;
                _freeCount--;

                ret.HashCode = hash;
                ret.Next = next;
                ret.Previous = prev;
                ret.LruNext = lruNext;
                ret.LruPrevious = lruPrev;
                ret.Key = key;
                ret.Value = val;
                return ret;
            }
            else
            {
                return new Node(hash, prev, next, lruPrev, lruNext, key, val);
            }
        }

        /// <summary>
        /// Gets the number of key/value pairs contained in the dictionary.
        /// </summary>
        public int Count => _count;

        internal int Capacity => _buckets.Length;

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        /// <summary>
        /// Initializes a new instance of the dictionary that is empty, 
        /// performs LRU eviction, has the default initial capacity, 
        /// and uses the default equality comparer for the key type.
        /// </summary>
        public RecentDictionary() : this(0, RecentDictionaryEvictionMode.LRU, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the dictionary that is empty, 
        /// performs LRU eviction, has the specified initial capacity, 
        /// and uses the default equality comparer for the key type.
        /// </summary>
        /// <param name="capacity">
        /// The initial number of elements that the dictionary can contain before resizing internally.
        /// </param>
        public RecentDictionary(int capacity) : this(capacity, RecentDictionaryEvictionMode.LRU, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the dictionary that is empty, 
        /// uses the specified eviction mode,
        /// has the default initial capacity, 
        /// and uses the default equality comparer for the key type.
        /// </summary>
        /// <param name="evictionMode">
        /// Specifies whether the most recent or least recent entry is evicted when
        /// <see cref="SetAndMaintainCount(TKey, TValue)"/> is called.
        /// </param>
        public RecentDictionary(RecentDictionaryEvictionMode evictionMode) : this(0, evictionMode, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the dictionary that is empty, 
        /// performs LRU eviction, has the default initial capacity, 
        /// and uses the specified equality comparer for the key type.
        /// </summary>
        /// <param name="comparer">
        /// The <see cref="IEqualityComparer{T}"/> implementation to use when comparing keys, 
        /// or null to use the default comparer for the type of the key.
        /// </param>
        public RecentDictionary(IEqualityComparer<TKey> comparer) : this(0, RecentDictionaryEvictionMode.LRU, comparer)
        {
        }

        /// <summary>
        /// Initializes a new instance of the dictionary with the specified  
        /// initial capacity, performs LRU eviction,
        /// and uses the provided equality comparer for the key type.
        /// </summary>
        /// <param name="capacity">
        /// The initial number of elements that the dictionary can contain before resizing internally.
        /// </param>
        /// <param name="comparer">
        /// The <see cref="IEqualityComparer{T}"/> implementation to use when comparing keys, 
        /// or null to use the default comparer for the type of the key.
        /// </param>
        public RecentDictionary(int capacity, IEqualityComparer<TKey> comparer) : this(capacity, RecentDictionaryEvictionMode.LRU, comparer)
        {
        }

        /// <summary>
        /// Initializes a new instance of the dictionary with the specified  
        /// initial capacity, the specified eviction, and uses the 
        /// provide equality comparer for the key type.
        /// </summary>
        /// <param name="capacity">
        /// The initial number of elements that the dictionary can contain before resizing internally.
        /// </param>
        /// <param name="evictionMode">
        /// Specifies whether the most recent or least recent entry is evicted when
        /// <see cref="SetAndMaintainCount(TKey, TValue)"/> is called.
        /// </param>
        /// <param name="comparer">
        /// The <see cref="IEqualityComparer{T}"/> implementation to use when comparing keys, 
        /// or null to use the default comparer for the type of the key.
        /// </param>
        public RecentDictionary(int capacity, RecentDictionaryEvictionMode evictionMode, IEqualityComparer<TKey> comparer)
        {
            if (capacity < 0 || capacity > (1 << 30))
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be between 0 and 1,073,741,824 (inclusive)");

            _comparer = comparer ?? EqualityComparer<TKey>.Default;
            _evictMode = evictionMode;

            int initialBucketCount;
            if (capacity <= 4)
            {
                initialBucketCount = 8;
            }
            else
            {
                initialBucketCount = NextPowerOfTwo(capacity);
            }
            _buckets = new Node[initialBucketCount];
            _bucketMask = initialBucketCount - 1;
        }

        private void MoveToFrontOfLru(Node node)
        {
            if (node.LruPrevious == null)
            {
                // we're already the most recently used. Nothing to do.
                return;
            }
            else
            {
                // unhook from current position
                node.LruPrevious.LruNext = node.LruNext;
                if (node.LruNext == null)
                {
                    // we're at the tail.
                    _lruTail = node.LruPrevious;
                }
                else
                {
                    node.LruNext.LruPrevious = node.LruPrevious;
                }

                _lruHead.LruPrevious = node;
                node.LruNext = _lruHead;
                _lruHead = node;
            }
        }

        private void RemoveFromLru(Node node)
        {
            if (node.LruPrevious == null)
            {
                // we're the most recently used.
                _lruHead = node.LruNext;
            }
            else
            {
                // unhook from current position
                node.LruPrevious.LruNext = node.LruNext;
                if (node.LruNext == null)
                {
                    // we're at the tail.
                    _lruTail = node.LruPrevious;
                }
                else
                {
                    node.LruNext.LruPrevious = node.LruPrevious;
                }
            }
        }


        /// <summary>
        /// Adds/updates the dictionary with a new value.
        /// </summary>
        /// <param name="key">Key associated with the value.</param>
        /// <param name="value">New value.</param>
        /// <param name="updateAllowed">
        /// Whether an existing element with the same key may be updated.
        /// If false, an ArgumentException is thrown if an item with the same
        /// key already exists.
        /// </param>
        /// <param name="maintainCount">
        /// If true, the lease recently accessed element will be removed to make room when a new
        /// entry is added.
        /// </param>
        private void Set(TKey key, TValue value, bool updateAllowed, bool maintainCount)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (_count >= _buckets.Length)
                Resize(_buckets.Length * 2);

            int hashcode = _comparer.GetHashCode(key) & 0x7FFFFFFF;
            int bucketIndex = hashcode & _bucketMask;

            Node node = _buckets[bucketIndex];
            while (node != null)
            {
                if (node.HashCode == hashcode && _comparer.Equals(key, node.Key))
                {
                    // found the entry, set new value
                    if (updateAllowed)
                    {
                        node.Value = value;
                        MoveToFrontOfLru(node);
                    }
                    else
                        throw new ArgumentException("An element with the same key already exists in the dictionary.", nameof(key));

                    return;
                }

                if (node.Next != null)
                    node = node.Next;
                else
                    break;
            }

            if (maintainCount && _count > 0)
            {
                Node nodeToRemove;
                if (_evictMode == RecentDictionaryEvictionMode.LRU)
                {
                    // remove the least recently used
                    nodeToRemove = _lruTail;
                    if (nodeToRemove.LruPrevious != null)
                    {
                        nodeToRemove.LruPrevious.LruNext = null;
                        _lruTail = nodeToRemove.LruPrevious;
                    }
                }
                else
                {
                    // remove the most recently used
                    nodeToRemove = _lruHead;
                    if (nodeToRemove.LruNext != null)
                    {
                        nodeToRemove.LruNext.LruPrevious = null;
                        _lruHead = nodeToRemove.LruNext;
                    }
                }
                RemoveNode(nodeToRemove, nodeToRemove.HashCode & _bucketMask);
            }


            // Add a new node.
            var newNode = AcquireNewNode(hashcode, prev: node, next: null, lruPrev: null, lruNext: _lruHead, key: key, val: value);

            // LRU list maintenance:
            if (_lruHead != null)
                _lruHead.LruPrevious = newNode;

            newNode.LruNext = _lruHead;
            _lruHead = newNode;
            if (_lruTail == null)
                _lruTail = newNode;

            // bucket list maintenance:
            if (node == null)
                _buckets[bucketIndex] = newNode;
            else
                node.Next = newNode;

            _count++;
            return;

        } // end Set()


        /// <summary>
        /// Gets the node containing the entry with the specified key, or null if not found.
        /// </summary>
        /// <param name="key">Key to search for.</param>
        /// <param name="updateLru">Whether to update the internal LRU list.</param>
        /// <returns>Node or null if not found.</returns>
        private Node Find(TKey key, bool updateLru)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            int hashcode = _comparer.GetHashCode(key) & 0x7FFFFFFF;
            int bucketIndex = hashcode & _bucketMask;

            Node node = _buckets[bucketIndex];

            while (node != null)
            {
                if (node.HashCode == hashcode && _comparer.Equals(key, node.Key))
                {
                    if (updateLru)
                        MoveToFrontOfLru(node);
                    return node;
                }
                else
                    node = node.Next;
            }

            return null;
        }

        /// <summary>
        /// Gets or sets the value associated with the specified key, marking the entry
        /// as the most recently used.
        /// </summary>
        /// <param name="key">The key of the value to get or set.</param>
        /// <returns>The value associated with the key.</returns>
        /// <exception cref="KeyNotFoundException">The key does not exist in the collection when performing a get.</exception>
        /// <exception cref="ArgumentNullException">The provided key is null.</exception>
        public TValue this[TKey key]
        {
            get
            {
                var node = Find(key, updateLru: true);
                if (node == null)
                    throw new KeyNotFoundException("Key does not exist in the collection");
                else
                    return node.Value;
            }

            set
            {
                Set(key, value, updateAllowed: true, maintainCount: false);
            }
        }

        /// <summary>
        /// Adds/updates the dictionary with a new value. If the operation performs an
        /// add, either the least-recently or most-recently accessed element will be 
        /// removed to make room for the new one (depending on the 
        /// <see cref="RecentDictionaryEvictionMode"/> passed into the constructor).
        /// </summary>
        /// <param name="key">Key associated with the value.</param>
        /// <param name="value">New value.</param>
        public void SetAndMaintainCount(TKey key, TValue value)
        {
            Set(key, value, updateAllowed: true, maintainCount: true);
        }


        /// <summary>
        /// Rounds up to the next power of 2.
        /// </summary>
        private static int NextPowerOfTwo(int n)
        {
            // see https://graphics.stanford.edu/~seander/bithacks.html#RoundUpPowerOf2
            n--;
            n |= n >> 1;
            n |= n >> 2;
            n |= n >> 4;
            n |= n >> 8;
            n |= n >> 16;
            n++;
            return n;
        }

        /// <summary>
        /// Resizes the collection. Requested size must be a power of two or
        /// behavior is undefined.
        /// </summary>
        /// <param name="newSize"></param>
        private void Resize(int newSize)
        {
            if (newSize > (1 << 30))
                throw new ArgumentOutOfRangeException(nameof(newSize), newSize, "Capacity cannot exceed 1,073,741,824 items.");

            var newBuckets = new Node[newSize];
            _bucketMask = newSize - 1;

            for (int i = 0; i < _buckets.Length; i++)
            {
                Node node = _buckets[i];

                while (true)
                {
                    if (_buckets[i] == null)
                        break;
                    else
                        node = _buckets[i];

                    _buckets[i] = node.Next;
                    if (_buckets[i] != null)
                        _buckets[i].Previous = null;

                    int newIndex = node.HashCode & _bucketMask;
                    if (newBuckets[newIndex] == null)
                    {
                        newBuckets[newIndex] = node;
                        node.Previous = null;
                        node.Next = null;
                    }
                    else
                    {
                        Node newBucket = newBuckets[newIndex];
                        while (newBucket.Next != null)
                            newBucket = newBucket.Next;

                        newBucket.Next = node;
                        node.Previous = newBucket;
                        node.Next = null;
                    }

                }
            }

            _buckets = newBuckets;
        }

        /// <summary>
        /// Trims excess capacity from the dictionary.
        /// </summary>
        public void Trim()
        {
            int newCapacity;
            if (_count <= 4)
                newCapacity = 8;
            else
                newCapacity = NextPowerOfTwo(_count);

            Resize(newCapacity);
            _freeList = null;
            _freeCount = 0;
        }

        /// <summary>
        /// Gets a collection containing the keys in the dictionary.
        /// </summary>
        public ICollection<TKey> Keys
        {
            get
            {
                if (_keys == null)
                    _keys = new KeyCollection(this);

                return _keys;
            }
        }

        /// <summary>
        /// Gets a collection containing the values in the dictionary.
        /// </summary>
        public ICollection<TValue> Values
        {
            get
            {
                if (_values == null)
                    _values = new ValueCollection(this);

                return _values;
            }
        }

        /// <summary>
        /// Adds the specified key and value to the dictionary.
        /// </summary>
        /// <param name="key">The key of the element to add.</param>
        /// <param name="value">The value of the element to add. The value can be null for reference types.</param>
        /// <exception cref="ArgumentException">An element with the same key already exists in the dictionary.</exception>
        public void Add(TKey key, TValue value)
        {
            Set(key, value, updateAllowed: false, maintainCount: false);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            Set(item.Key, item.Value, updateAllowed: false, maintainCount: false);
        }

        /// <summary>
        /// Removes all items from the dictionary without changing the dictionary's capacity.
        /// </summary>
        /// <remarks>
        /// Use <see cref="Trim"/> to reduce the collection's capacity.
        /// </remarks>
        public void Clear()
        {
            if (_count > 0)
            {
                _lruHead = null;
                _lruTail = null;
                Array.Clear(_buckets, 0, _buckets.Length);
                _count = 0;
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            var node = Find(item.Key, updateLru: false);

            if (node != null && EqualityComparer<TValue>.Default.Equals(item.Value, node.Value))
                return true;
            else
                return false;
        }

        /// <summary>
        /// Determines whether the dictionary contains the specified key. The order of
        /// items in the dictionary is not modified.
        /// </summary>
        /// <param name="key">The key to locate</param>
        /// <returns>true if the dictionary contains an element with the specified key; otherwise, false.</returns>
        public bool ContainsKey(TKey key)
        {
            return (Find(key, updateLru: false) != null);
        }

        /// <summary>
        /// Determines whether the dictionary contains the specified value. The order of
        /// items in the dictionary is not modified.
        /// </summary>
        /// <param name="value">The value to locate</param>
        /// <returns>true if the dictionary contains an element with the specified value; otherwise, false.</returns>
        /// <remarks>
        /// This in an O(n) operation.
        /// </remarks>
        public bool ContainsValue(TValue value)
        {
            if (value == null)
            {
                var node = _lruHead;
                while (node != null)
                {
                    if (node.Value == null)
                        return true;
                    node = node.LruNext;
                }
            }
            else
            {
                var comparer = EqualityComparer<TValue>.Default;
                var node = _lruHead;
                while (node != null)
                {
                    if (comparer.Equals(node.Value, value))
                        return true;
                    node = node.LruNext;
                }
            }
            return false;
        }

        /// <summary>
        /// Copies the elements of the dictionary to an array of KeyValuePair elements, starting at the specified array index.
        /// </summary>
        /// <param name="array">Destination array.</param>
        /// <param name="arrayIndex">Index in destination array at which copying begins.</param>
        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            if (arrayIndex < 0 || arrayIndex >= array.Length)
                throw new ArgumentOutOfRangeException(nameof(arrayIndex), arrayIndex, "arrayIndex must reside within the bounds of the destination array");

            if ((array.Length - arrayIndex) < _count)
                throw new ArgumentOutOfRangeException(nameof(array), "The number of elements in the source dictionary is greater than the available space from arrayIndex to the end of the destination array.");

            var node = _lruHead;
            while (node != null)
            {
                array[arrayIndex] = new KeyValuePair<TKey, TValue>(node.Key, node.Value);
                node = node.LruNext;
                arrayIndex++;
            }
        }


        /// <summary>
        /// Returns an enumerator that iterates through the dictionary.
        /// </summary>
        /// <returns>An enumerator to iterate through the dictionary's key/value pairs.</returns>
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            if (_count == 0)
                yield break;
            else
            {
                var node = _lruHead;
                while (node != null)
                {
                    yield return new KeyValuePair<TKey, TValue>(node.Key, node.Value);
                    node = node.LruNext;
                }
            }

        }

        /// <summary>
        /// Gets the most recently accessed item in the dictionary. The order of
        /// items in the dictionary is not modified.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The dictionary is empty.
        /// </exception>
        public KeyValuePair<TKey, TValue> MostRecent
        {
            get
            {
                if (_count > 0)
                    return new KeyValuePair<TKey, TValue>(_lruHead.Key, _lruHead.Value);
                else
                    throw new InvalidOperationException("Dictionary is empty.");
            }
        }

        /// <summary>
        /// Gets the least recently accessed item in the dictionary. The order of
        /// items in the dictionary is not modified.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The dictionary is empty.
        /// </exception>
        public KeyValuePair<TKey, TValue> LeastRecent
        {
            get
            {
                if (_count > 0)
                    return new KeyValuePair<TKey, TValue>(_lruTail.Key, _lruTail.Value);
                else
                    throw new InvalidOperationException("Dictionary is empty.");
            }
        }

        /// <summary>
        /// Removes the value with the specified key from the dictionary.
        /// </summary>
        /// <param name="key">key of the element to remove</param>
        /// <returns>true if the element is found and removed; otherwise, false. This method returns false if key is not found in the dictionary</returns>
        public bool Remove(TKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            int hashcode = _comparer.GetHashCode(key) & 0x7FFFFFFF;
            int bucketIndex = hashcode & _bucketMask;

            Node node = _buckets[bucketIndex];

            while (node != null)
            {
                if (node.HashCode == hashcode && _comparer.Equals(key, node.Key))
                {
                    RemoveFromLru(node);
                    RemoveNode(node, bucketIndex);
                    return true;
                }
                else
                    node = node.Next;
            }

            return false;

        }

        /// <summary>
        /// Removes the least recently accessed item from the dictionary.
        /// </summary>
        /// <returns>true if an item was successfully removed, or false if the dictionary is empty.</returns>
        public bool RemoveLeastRecent()
        {
            if (_count == 0)
                return false;


            var nodeToRemove = _lruTail;
            RemoveFromLru(nodeToRemove);
            RemoveNode(nodeToRemove, nodeToRemove.HashCode & _bucketMask);
            return true;
        }

        /// <summary>
        /// Gets and removes the least recently accessed item from the dictionary.
        /// </summary>
        /// <returns>The dictionary entry that was removed.</returns>
        public KeyValuePair<TKey, TValue> RemoveAndGetLeastRecent()
        {
            if (_count == 0)
                throw new InvalidOperationException("Dictionary is empty.");

            var nodeToRemove = _lruTail;
            var ret = new KeyValuePair<TKey, TValue>(nodeToRemove.Key, nodeToRemove.Value);
            RemoveFromLru(nodeToRemove);
            RemoveNode(nodeToRemove, nodeToRemove.HashCode & _bucketMask);
            return ret;
        }

        /// <summary>
        /// Removes the most recently accessed item from the dictionary.
        /// </summary>
        /// <returns>true if an item was successfully removed, or false if the dictionary is empty.</returns>
        public bool RemoveMostRecent()
        {
            if (_count == 0)
                return false;


            var nodeToRemove = _lruHead;
            RemoveFromLru(nodeToRemove);
            RemoveNode(nodeToRemove, nodeToRemove.HashCode & _bucketMask);
            return true;
        }

        /// <summary>
        /// Gets and removes the most recently accessed item from the dictionary.
        /// </summary>
        /// <returns>The dictionary entry that was removed.</returns>
        public KeyValuePair<TKey, TValue> RemoveAndGetMostRecent()
        {
            if (_count == 0)
                throw new InvalidOperationException("Dictionary is empty.");

            var nodeToRemove = _lruHead;
            var ret = new KeyValuePair<TKey, TValue>(nodeToRemove.Key, nodeToRemove.Value);
            RemoveFromLru(nodeToRemove);
            RemoveNode(nodeToRemove, nodeToRemove.HashCode & _bucketMask);
            return ret;
        }

        private void RemoveNode(Node node, int bucketIndex)
        {
            if (bucketIndex < 0 || bucketIndex >= _buckets.Length)
                throw new ArgumentOutOfRangeException(nameof(bucketIndex));
            if (node == null)
                throw new ArgumentNullException(nameof(node));

            if (node.Previous == null)
                _buckets[bucketIndex] = node.Next;
            else
                node.Previous.Next = node.Next;

            if (node.Next != null)
                node.Next.Previous = node.Previous;

            ReleaseNode(node);
            _count--;

        }

        


        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            if (item.Key == null) throw new ArgumentException("Item's Key property is null", nameof(item));
            int hashcode = _comparer.GetHashCode(item.Key) & 0x7FFFFFFF;
            int bucketIndex = hashcode & _bucketMask;

            var node = Find(item.Key, updateLru: false);

            if (node != null && EqualityComparer<TValue>.Default.Equals(item.Value, node.Value))
            {
                RemoveFromLru(node);
                RemoveNode(node, bucketIndex);
                return true;
            }
            else
                return false;

        }

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        /// <param name="key">The key of the value to get.</param>
        /// <param name="value">
        /// When this method returns, contains the value associated with the specified key, 
        /// if the key is found; otherwise, the default value for the type of the value parameter. 
        /// This parameter is passed uninitialized.
        /// </param>
        /// <returns>true if the dictionary contains an element with the specified key; otherwise, false.</returns>
        public bool TryGetValue(TKey key, out TValue value)
        {
            var node = Find(key, updateLru: true);
            if (node != null)
            {
                value = node.Value;
                return true;
            }
            else
            {
                value = default(TValue);
                return false;
            }


        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Represents the collection of keys in a <see cref="RecentDictionary{TKey, TValue}"/>.
        /// </summary>
        public sealed class KeyCollection : ICollection<TKey>, IEnumerable<TKey>, IReadOnlyCollection<TKey>
        {
            private RecentDictionary<TKey, TValue> _dict;

            internal KeyCollection(RecentDictionary<TKey, TValue> dictionary)
            {
                _dict = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
            }

            /// <summary>
            /// Gets the number of elements in the key collection.
            /// </summary>
            public int Count => _dict.Count;

            bool ICollection<TKey>.IsReadOnly => true;

            void ICollection<TKey>.Add(TKey item)
            {
                throw new NotSupportedException("Key collection is read-only.");
            }

            void ICollection<TKey>.Clear()
            {
                throw new NotSupportedException("Key collection is read-only.");
            }

            bool ICollection<TKey>.Contains(TKey item)
            {
                return _dict.ContainsKey(item);
            }

            /// <summary>
            /// Copies the key elements to an existing array, starting at the specified array index.
            /// </summary>
            /// <param name="array">Destination array.</param>
            /// <param name="arrayIndex">Offset in the destination array at which to begin copying.</param>
            public void CopyTo(TKey[] array, int arrayIndex)
            {
                if (array == null) throw new ArgumentNullException(nameof(array));

                if (arrayIndex < 0 || arrayIndex > array.Length)
                    throw new ArgumentOutOfRangeException(nameof(arrayIndex), arrayIndex, "index is outside the bounds of the array");

                if ((array.Length - arrayIndex) < _dict.Count)
                    throw new ArgumentException("The number of elements in the source collection is greater than the available space from arrayIndex to the end of the destination array.");

                var node = _dict._lruHead;
                while (node != null)
                {
                    array[arrayIndex] = node.Key;
                    node = node.LruNext;
                    arrayIndex++;
                }
            }

            /// <summary>
            /// Gets an enumerator that iterates through a collection.
            /// </summary>
            /// <returns>An enumerator that can be used to iterate through the collection.</returns>
            public IEnumerator<TKey> GetEnumerator()
            {
                if (_dict._count == 0)
                    yield break;
                else
                {
                    var node = _dict._lruHead;
                    while (node != null)
                    {
                        yield return node.Key;
                        node = node.LruNext;
                    }
                }

            }

            bool ICollection<TKey>.Remove(TKey item)
            {
                throw new NotSupportedException("Key collection is read-only.");
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        } // end KeyCollection


        /// <summary>
        /// Represents the collection of values in a <see cref="RecentDictionary{TKey, TValue}"/>.
        /// </summary>
        public sealed class ValueCollection : ICollection<TValue>, IEnumerable<TValue>, IReadOnlyCollection<TValue>
        {
            private RecentDictionary<TKey, TValue> _dict;

            internal ValueCollection(RecentDictionary<TKey, TValue> dictionary)
            {
                _dict = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
            }

            /// <summary>
            /// Gets the number of elements in the value collection.
            /// </summary>
            public int Count => _dict.Count;

            bool ICollection<TValue>.IsReadOnly => true;

            void ICollection<TValue>.Add(TValue item)
            {
                throw new NotSupportedException("Value collection is read-only.");
            }

            void ICollection<TValue>.Clear()
            {
                throw new NotSupportedException("Value collection is read-only.");
            }

            bool ICollection<TValue>.Contains(TValue item)
            {
                return _dict.ContainsValue(item);
            }

            void ICollection<TValue>.CopyTo(TValue[] array, int arrayIndex)
            {
                if (array == null) throw new ArgumentNullException(nameof(array));

                if (arrayIndex < 0 || arrayIndex > array.Length)
                    throw new ArgumentOutOfRangeException(nameof(arrayIndex), arrayIndex, "index is outside the bounds of the array");

                if ((array.Length - arrayIndex) < _dict.Count)
                    throw new ArgumentException("The number of elements in the source collection is greater than the available space from arrayIndex to the end of the destination array.");

                var node = _dict._lruHead;
                while (node != null)
                {
                    array[arrayIndex] = node.Value;
                    node = node.LruNext;
                    arrayIndex++;
                }
            }

            /// <summary>
            /// Gets an enumerator that iterates through a collection.
            /// </summary>
            /// <returns>An enumerator that can be used to iterate through the collection.</returns>
            public IEnumerator<TValue> GetEnumerator()
            {
                if (_dict._count == 0)
                    yield break;
                else
                {
                    var node = _dict._lruHead;
                    while (node != null)
                    {
                        yield return node.Value;
                        node = node.LruNext;
                    }
                }

            }

            bool ICollection<TValue>.Remove(TValue item)
            {
                throw new NotSupportedException("Value collection is read-only.");
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        } // end ValueCollection
    }
}
