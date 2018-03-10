﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Text;

namespace Scaleout.Collections
{
    // TODO: Serialization
    // TODO: Condition for random read/remove

    [DebuggerDisplay("Count = {Count}")]
    public class RouletteDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        private int _count = 0;
        private int _countMask; // applied to hashes to select buckets

        IEqualityComparer<TKey> _comparer;

        // Cached references to collections returned by Keys and Values properties.
        private KeyCollection _keys = null;
        private ValueCollection _values = null;

        Node[] _buckets;

        // This dictionary is intended for use as a building block
        // for a fixed-size cache.
        // We maintain a small list of free bucket nodes to reduce
        // allocation/GC overhead when operating at/near capacity.
        private const int MaxFreeNodes = 10;
        Node _freeList;
        int _freeCount;

        private class Node
        {
            public int HashCode;
            public Node Next;
            public Node Previous;
            public TKey Key;
            public TValue Value;

            public Node(int hash, Node prev, Node next, TKey key, TValue val)
            {
                HashCode = hash;
                Next = next;
                Previous = prev;
                Key = key;
                Value = val;
            }
        }

        private void ReleaseNode(Node node)
        {
            node.Previous = null;
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

        private Node AcquireNewNode(int hash, Node prev, Node next, TKey key, TValue val)
        {
            if (_freeCount > 0)
            {
                Node ret = _freeList;
                _freeList = ret.Next;
                _freeCount--;

                ret.HashCode = hash;
                ret.Next = next;
                ret.Previous = prev;
                ret.Key = key;
                ret.Value = val;
                return ret;
            }
            else
            {
                return new Node(hash, prev, next, key, val);
            }
        }

        public int Count => _count;

        internal int Capacity => _buckets.Length;

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        public RouletteDictionary(int capacity = 0, IEqualityComparer<TKey> comparer = null)
        {
            _comparer = comparer ?? EqualityComparer<TKey>.Default;
            
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
            _countMask = initialBucketCount - 1;
        }


        /// <summary>
        /// Adds/updates the dictionary with a new value.
        /// </summary>
        /// <param name="key">Key associated with the value.</param>
        /// <param name="value">New value.</param>
        /// <param name="updateAllowed">
        /// Whether an existing element with the same key may be updated.
        /// If false, an ArgumentException is thrown if an item with the same
        /// key alread exists.
        /// </param>
        /// <param name="maintainCount">
        /// If true, a random element will be removed to make room when a new
        /// entry is added.
        /// </param>
        private void Set(TKey key, TValue value, bool updateAllowed, bool maintainCount)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (_count >= _buckets.Length)
                Resize(_buckets.Length * 2);

            int hashcode = _comparer.GetHashCode(key) & 0x7FFFFFFF;
            int bucketIndex = hashcode & _countMask;

            Node node = _buckets[bucketIndex];
            while (node != null)
            {
                if (node.HashCode == hashcode && _comparer.Equals(key, node.Key))
                {
                    // found the entry, set new value
                    if (updateAllowed)
                        node.Value = value;
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
                int bucketToRemoveFrom = GetRandomOccupiedBucket();
                if (bucketToRemoveFrom == bucketIndex)
                {
                    // remove the node we just looked at:
                    if (node.Previous == null)
                        _buckets[bucketIndex] = null;
                    else
                        node.Previous.Next = null;
                    ReleaseNode(node);
                    _count--;
                    node = null;
                }
                else
                {
                    RemoveFrontNode(bucketToRemoveFrom);
                }
            }

            // Add a new node.
            var newNode = AcquireNewNode(hashcode, prev: node, next: null, key: key, val: value);
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
        /// <returns>Node or null if not found.</returns>
        private Node Find(TKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            int hashcode = _comparer.GetHashCode(key) & 0x7FFFFFFF;
            int bucketIndex = hashcode & _countMask;

            Node node = _buckets[bucketIndex];

            while (node != null)
            {
                if (node.HashCode == hashcode && _comparer.Equals(key, node.Key))
                    return node;
                else
                    node = node.Next;
            }

            return null;
        }

        public TValue this[TKey key]
        {
            get
            {
                var node = Find(key);
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
        /// add, a random element will be removed to make room for the new one.
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
        private int NextPowerOfTwo(int n)
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
            var newBuckets = new Node[newSize];
            _countMask = newSize - 1;

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

                    int newIndex = node.HashCode & _countMask;
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

        public ICollection<TKey> Keys
        {
            get
            {
                if (_keys == null)
                    _keys = new KeyCollection(this);

                return _keys;
            }
        }

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
                Array.Clear(_buckets, 0, _buckets.Length);
                _count = 0;
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            var node = Find(item.Key);

            if (node != null && EqualityComparer<TValue>.Default.Equals(item.Value, node.Value))
                return true;
            else
                return false;
        }

        /// <summary>
        /// Determines whether the dictionary contains the specified key.
        /// </summary>
        /// <param name="key">The key to locate</param>
        /// <returns>true if the dictionary contains an element with the specified key; otherwise, false.</returns>
        public bool ContainsKey(TKey key)
        {
            return (Find(key) != null);
        }

        /// <summary>
        /// Determines whether the dictionary contains the specified value.
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
                for (int i = 0; i < _buckets.Length; i++)
                {
                    var node = _buckets[i];
                    while (node != null)
                    {
                        if (node.Value == null)
                            return true;
                        node = node.Next;
                    }
                }
            }
            else
            {
                var comparer = EqualityComparer<TValue>.Default;
                for (int i = 0; i < _buckets.Length; i++)
                {
                    var node = _buckets[i];
                    while (node != null)
                    {
                        if (comparer.Equals(node.Value, value))
                            return true;
                        node = node.Next;
                    }
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
                throw new ArgumentOutOfRangeException("The number of elements in the source dictionary is greater than the available space from arrayIndex to the end of the destination array.");

            for (int i = 0; i < _buckets.Length; i++)
            {
                var node = _buckets[i];
                while (node != null)
                {
                    array[arrayIndex] = new KeyValuePair<TKey, TValue>(node.Key, node.Value);
                    node = node.Next;
                    arrayIndex++;
                }
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
                for (int i = 0; i < _buckets.Length; i++)
                {
                    var node = _buckets[i];
                    while (node != null)
                    {
                        yield return new KeyValuePair<TKey, TValue>(node.Key, node.Value);
                        node = node.Next;
                    }
                }
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
            int bucketIndex = hashcode & _countMask;

            Node node = _buckets[bucketIndex];

            while (node != null)
            {
                if (node.HashCode == hashcode && _comparer.Equals(key, node.Key))
                {
                    RemoveNode(node, bucketIndex);
                    return true;
                }
                else
                    node = node.Next;
            }

            return false;

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

        /// <summary>
        /// Removes a random entry from the dictionary that satisfies a condition.
        /// </summary>
        /// <returns>true if an element; otherwise, false. This method returns false if the dictionary is empty or if no element satisfies the condition in predicate.</returns>
        public bool RemoveRandom(Func<TValue, bool> predicate)
        {
            if (Count == 0)
                return false;

            int bucketIndex = GetRandomNode(predicate, out Node node);
            if (bucketIndex < 0)
                return false;

            RemoveNode(node, bucketIndex);
            return true;
        }

        /// <summary>
        /// Removes a random entry from the dictionary that satisfies a condition, returning the removed
        /// entry as a KeyValuePair.
        /// </summary>
        /// <returns>A KeyValuePair containing the removed entry.</returns>
        public KeyValuePair<TKey, TValue> RemoveRandomAndGet(Func<TValue, bool> predicate)
        {
            if (Count == 0)
                throw new InvalidOperationException("Dictionary is empty.");

            int bucketIndex = GetRandomNode(predicate, out Node node);
            if (bucketIndex < 0)
                throw new InvalidOperationException("No entries satisfy the predicate.");

            var ret = new KeyValuePair<TKey, TValue>(node.Key, node.Value);
            RemoveNode(node, bucketIndex);
            return ret;
        }

        /// <summary>
        /// Removes a random entry from the dictionary.
        /// </summary>
        /// <returns>true if an element; otherwise, false. This method returns false if the dictionary is empty.</returns>
        public bool RemoveRandom()
        {
            int bucketIndex = GetRandomOccupiedBucket();
            if (bucketIndex < 0)
                return false;

            return RemoveFrontNode(bucketIndex);
        }

        private int GetRandomOccupiedBucket()
        {
            if (_count == 0)
                return -1;

            int bucketIndex = TlsRandom.Next(_buckets.Length);
            while (_buckets[bucketIndex] == null)
            {
                bucketIndex++;
                if (bucketIndex == _buckets.Length)
                    bucketIndex = 0;
            }

            return bucketIndex;
        }

        private int GetRandomNode(Func<TValue, bool> predicate, out Node node)
        {
            int checkedObjCount = 0;
            if (_count == 0)
            {
                node = null;
                return -1;
            }

            int bucketIndex = TlsRandom.Next(_buckets.Length);
            while (checkedObjCount <= _count)
            {
                node = _buckets[bucketIndex];
                while (node != null)
                {
                    if (predicate(node.Value))
                    {
                        return bucketIndex;
                    }
                    node = node.Next;
                }

                bucketIndex++;
                if (bucketIndex == _buckets.Length)
                    bucketIndex = 0;
            }

            // Didn't find any objects that satisfy the predicate.
            node = null;
            return -1;
        }

        /// <summary>
        /// Removes an entry from a bucket.
        /// </summary>
        /// <returns>true if an element; otherwise, false. This method returns false if the dictionary is empty.</returns>
        private bool RemoveFrontNode(int bucketIndex)
        {
            // remove first item in bucket
            var node = _buckets[bucketIndex];
            if (node == null)
                return false;

            // removing the first node in the bucket.
            _buckets[bucketIndex] = node.Next;
            if (node.Next != null)
                node.Next.Previous = null;

            ReleaseNode(node);
            _count--;
            return true;
        }

        /// <summary>
        /// Removes a random entry from the dictionary, returning the removed
        /// entry as a KeyValuePair.
        /// </summary>
        /// <returns>A KeyValuePair containing the removed entry.</returns>
        public KeyValuePair<TKey, TValue> RemoveRandomAndGet()
        {
            return RemoveRandomAndGet(v => true);
        }

        public TValue GetRandomValue(Func<TValue, bool> predicate)
        {
            if (Count == 0)
                throw new InvalidOperationException("Dictionary is empty.");

            int bucketIndex = GetRandomNode(predicate, out Node node);
            if (bucketIndex < 0)
                throw new InvalidOperationException("No entries satisfy the predicate.");

            return node.Value;
        }

        public TKey GetRandomKey(Func<TValue, bool> predicate)
        {
            if (Count == 0)
                throw new InvalidOperationException("Dictionary is empty.");

            int bucketIndex = GetRandomNode(predicate, out Node node);
            if (bucketIndex < 0)
                throw new InvalidOperationException("No entries satisfy the predicate.");

            return node.Key;
        }

        public KeyValuePair<TKey, TValue> GetRandomKeyAndValue(Func<TValue, bool> predicate)
        {
            if (Count == 0)
                throw new InvalidOperationException("Dictionary is empty.");

            int bucketIndex = GetRandomNode(predicate, out Node node);
            if (bucketIndex < 0)
                throw new InvalidOperationException("No entries satisfy the predicate.");

            var ret = new KeyValuePair<TKey, TValue>(node.Key, node.Value);
            return ret;
        }

        public TValue GetRandomValue()
        {
            return GetRandomValue(v => true);
        }

        public TKey GetRandomKey()
        {
            return GetRandomKey(v => true);
        }

        public KeyValuePair<TKey,TValue> GetRandomKeyAndValue()
        {
            return GetRandomKeyAndValue(v => true);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            var node = Find(key);
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

        public class KeyCollection : ICollection<TKey>, IEnumerable<TKey>, IReadOnlyCollection<TKey>
        {
            private RouletteDictionary<TKey, TValue> _dict;

            public KeyCollection(RouletteDictionary<TKey, TValue> dictionary)
            {
                _dict = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
            }
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

            public void CopyTo(TKey[] array, int arrayIndex)
            {
                if (array == null) throw new ArgumentNullException(nameof(array));

                if (arrayIndex < 0 || arrayIndex > array.Length)
                    throw new ArgumentOutOfRangeException(nameof(arrayIndex), arrayIndex, "index is outside the bounds of the array");

                if ((array.Length - arrayIndex) < _dict.Count)
                    throw new ArgumentException("The number of elements in the source collection is greater than the available space from arrayIndex to the end of the destination array.");

                var buckets = _dict._buckets;
                for (int i = 0; i < buckets.Length; i++)
                {
                    var node = buckets[i];
                    while (node != null)
                    {
                        array[arrayIndex] = node.Key;
                        node = node.Next;
                        arrayIndex++;
                    }
                }
            }

            public IEnumerator<TKey> GetEnumerator()
            {
                if (_dict._count == 0)
                    yield break;
                else
                {
                    var buckets = _dict._buckets;
                    for (int i = 0; i < buckets.Length; i++)
                    {
                        var node = buckets[i];
                        while (node != null)
                        {
                            yield return node.Key;
                            node = node.Next;
                        }
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


        public class ValueCollection : ICollection<TValue>, IEnumerable<TValue>, IReadOnlyCollection<TValue>
        {
            private RouletteDictionary<TKey, TValue> _dict;

            public ValueCollection(RouletteDictionary<TKey, TValue> dictionary)
            {
                _dict = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
            }
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

                var buckets = _dict._buckets;
                for (int i = 0; i < buckets.Length; i++)
                {
                    var node = buckets[i];
                    while (node != null)
                    {
                        array[arrayIndex] = node.Value;
                        node = node.Next;
                        arrayIndex++;
                    }
                }
            }

            public IEnumerator<TValue> GetEnumerator()
            {
                if (_dict._count == 0)
                    yield break;
                else
                {
                    var buckets = _dict._buckets;
                    for (int i = 0; i < buckets.Length; i++)
                    {
                        var node = buckets[i];
                        while (node != null)
                        {
                            yield return node.Value;
                            node = node.Next;
                        }
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
