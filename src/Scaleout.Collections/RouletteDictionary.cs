using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Runtime.CompilerServices;
using System.Text;

namespace Scaleout.Collections
{
    [DebuggerDisplay("Count = {Count}")]
    [Serializable]
    public class RouletteDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        private const float MaxLoadFactor = 0.75f;
        private const int Unoccupied = 0;
        private const int Tombstone = -1;

        // People are used to doing unsynchronized reads on a dictionary, but that
        // would break System.Random. We hold it in thread local storage to protect it.
        private static ThreadLocal<Random> _tlsRand = new ThreadLocal<Random>(() => new Random(Seed: Thread.CurrentThread.ManagedThreadId));


        private int _count = 0;
        // Max count, inclusive, before resize.
        private int _maxCountBeforeResize;

        IEqualityComparer<TKey> _comparer;

        // Cached references to collections returned by Keys and Values properties.
        private KeyCollection _keys = null;
        private ValueCollection _values = null;

        private struct Bucket
        {
            public int HashCode;
            public TKey Key;
            public TValue Value;

            public override string ToString()
            {
                if (HashCode < 1)
                    return "{empty}";
                else
                    return $"[{Key}]: {Value}";
            }
        }

        Bucket[] _buckets;
        
        public int Count => _count;

        internal int Capacity => _buckets.Length;

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        public RouletteDictionary(int capacity = 1, IEqualityComparer<TKey> comparer = null)
        {
            _comparer = comparer ?? EqualityComparer<TKey>.Default;
            
            int initialBucketCount;
            if (capacity <= 3)
            {
                initialBucketCount = 5;
                _maxCountBeforeResize = 3;
            }
            else
            {
                // we keep the load factor below .75, so add some wiggle room to the requested
                // capacity to prevent resize operations
                initialBucketCount = Primes.Next((int)(capacity * 1.5));
                _maxCountBeforeResize = (int)(initialBucketCount * MaxLoadFactor);
            }
            _buckets = new Bucket[initialBucketCount];
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
        private void Set(TKey key, TValue value, bool updateAllowed)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            if (_count >= _maxCountBeforeResize)
                Resize(Primes.Next((_buckets.Length * 2) + 1));

            int hashcode = _comparer.GetHashCode(key) & 0x7FFFFFFF;
            if (hashcode == 0) hashcode = 1;

            int bucketIndex = hashcode % _buckets.Length;
            int probeIndex = bucketIndex;

            int indexOfFirstTombstone = -1;
            int probeCount = 0;

            while (true)
            {
                if (_buckets[probeIndex].HashCode == Tombstone)
                {
                    if (indexOfFirstTombstone == -1)
                        indexOfFirstTombstone = probeIndex;

                    // need to probe again.
                    probeCount++;
                    probeIndex = (bucketIndex + (probeCount * probeCount)) % _buckets.Length;
                    continue;
                }

                if (_buckets[probeIndex].HashCode == Unoccupied)
                {
                    // didn't find the entry, adding new value.
                    _count++;

                    // fill in a tombstone, if possible:
                    if (indexOfFirstTombstone != -1)
                        probeIndex = indexOfFirstTombstone;

                    _buckets[probeIndex].HashCode = hashcode;
                    _buckets[probeIndex].Key = key;
                    _buckets[probeIndex].Value = value;

                    return;
                }

                if (hashcode == _buckets[probeIndex].HashCode && _comparer.Equals(key, _buckets[probeIndex].Key))
                {
                    // found the entry, set new value
                    if (updateAllowed)
                        _buckets[probeIndex].Value = value;
                    else
                        throw new ArgumentException("An element with the same key already exists in the dictionary.", nameof(key));

                    return;
                }
                else
                {
                    // collision, probe again.
                    probeCount++;
                    probeIndex = (bucketIndex + (probeCount * probeCount)) % _buckets.Length;
                    continue;
                }
            }
        }

        /// <summary>
        /// Gets the bucket index containing the entry with the specified key, or -1 if not found.
        /// </summary>
        /// <param name="key">Key to search for.</param>
        /// <returns>Bucket index or -1.</returns>
        private int FindKey(TKey key)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            int hashcode = _comparer.GetHashCode(key) & 0x7FFFFFFF;
            int bucketIndex = hashcode % _buckets.Length;
            int probeIndex = bucketIndex;
            int probeCount = 0;

            while (true)
            {
                if (_buckets[probeIndex].HashCode == Tombstone)
                {
                    // need to probe again.
                    probeCount++;
                    probeIndex = (bucketIndex + (probeCount * probeCount)) % _buckets.Length;
                    continue;
                }

                if (_buckets[probeIndex].HashCode == Unoccupied)
                {
                    // didn't find the entry
                    return -1;
                }

                if (hashcode == _buckets[probeIndex].HashCode && _comparer.Equals(key, _buckets[probeIndex].Key))
                {
                    // found the entry, return the bucket index.
                    return probeIndex;
                }
                else
                {
                    // collision, probe again.
                    probeCount++;
                    probeIndex = (bucketIndex + (probeCount * probeCount)) % _buckets.Length;
                    continue;
                }
            }

        }

        /// <summary>
        /// Picks a random, occupied bucket.
        /// </summary>
        /// <returns>Index of a bucket, or -1 if the dictionary is empty.</returns>
        private int FindRandomOccupied()
        {
            if (_count == 0)
                return -1;

            int probeIndex = _tlsRand.Value.Next(_buckets.Length);
            // probe forward if even, backwards if odd.
            int probeIncrement = (probeIndex & 1) == 0 ? 1 : -1;

            while (true)
            {
                if (_buckets[probeIndex].HashCode > Unoccupied)
                    return probeIndex;
                else
                {
                    probeIndex += probeIncrement;
                    if (probeIndex < 0)
                        probeIndex = _buckets.Length - 1;
                    else if (probeIndex == _buckets.Length)
                        probeIndex = 0;
                }
            }
        }


        public TValue this[TKey key]
        {
            get
            {
                int bucketIndex = FindKey(key);
                if (bucketIndex < 0)
                    throw new KeyNotFoundException("Key does not exist in the collection");
                else
                    return _buckets[bucketIndex].Value;
            }

            set
            {
                Set(key, value, updateAllowed: true);
            }
        }

        /// <summary>
        /// Resizes the collection. Requested size must be prime or
        /// behavior is undefined.
        /// </summary>
        /// <param name="newSize"></param>
        private void Resize(int newSize)
        {
            var newBuckets = new Bucket[newSize];

            for (int i = 0; i < _buckets.Length; i++)
            {
                if (_buckets[i].HashCode < 1)
                    continue;

                int bucketIndex = _buckets[i].HashCode % newBuckets.Length;
                int probeIndex = bucketIndex;
                int probeCount = 0;
                while (true)
                {
                    if (newBuckets[probeIndex].HashCode == Unoccupied)
                    {
                        newBuckets[probeIndex].HashCode = _buckets[i].HashCode;
                        newBuckets[probeIndex].Key = _buckets[i].Key;
                        newBuckets[probeIndex].Value = _buckets[i].Value;
                        break;
                    }
                    else
                    {
                        // collision. probe again.
                        probeCount++;
                        probeIndex = (bucketIndex + (probeCount * probeCount)) % newBuckets.Length;
                    }

                }
            }

            _buckets = newBuckets;
            _maxCountBeforeResize = (int)(newBuckets.Length * MaxLoadFactor);
        }

        /// <summary>
        /// Trims excess capactiy from the dictionary.
        /// </summary>
        public void Trim()
        {
            // we keep the load factor a bit below .75 to keep
            // probing from killing performance.
            int newCapacity;
            if (_count <= 3)
                newCapacity = 5;
            else
                newCapacity = Primes.Next((int)(_count * 1.5));
            Resize(newCapacity);
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

        public void Add(TKey key, TValue value)
        {
            Set(key, value, updateAllowed: false);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            Set(item.Key, item.Value, updateAllowed: false);
        }

        /// <summary>
        /// Removes all items from the dictionary without changing the dictionary's capacity.
        /// </summary>
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
            int bucketIndex = FindKey(item.Key);

            if (bucketIndex >= 0 && EqualityComparer<TValue>.Default.Equals(item.Value, _buckets[bucketIndex].Value))
                return true;
            else
                return false;
        }

        public bool ContainsKey(TKey key)
        {
            return FindKey(key) >= 0;
        }

        public bool ContainsValue(TValue value)
        {
            if (value == null)
            {
                for (int i = 0; i < _buckets.Length; i++)
                {
                    if (_buckets[i].HashCode > Unoccupied && _buckets[i].Value == null)
                        return true;
                }
            }
            else
            {
                var comparer = EqualityComparer<TValue>.Default;
                for (int i = 0; i < _buckets.Length; i++)
                {
                    if (_buckets[i].HashCode > Unoccupied && comparer.Equals(_buckets[i].Value, value))
                        return true;
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
                if (_buckets[i].HashCode > Unoccupied)
                {
                    array[arrayIndex] = new KeyValuePair<TKey, TValue>(_buckets[i].Key, _buckets[i].Value);
                    arrayIndex++;
                }
            }

        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            if (_count == 0)
                yield break;
            else
            {
                for (int i = 0; i < _buckets.Length; i++)
                {
                    if (_buckets[i].HashCode > Unoccupied)
                    {
                        yield return new KeyValuePair<TKey, TValue>(_buckets[i].Key, _buckets[i].Value);
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
            int bucketIndex = FindKey(key);

            if (bucketIndex >= 0)
            {
                _buckets[bucketIndex].HashCode = Tombstone;
                _buckets[bucketIndex].Key = default(TKey);
                _buckets[bucketIndex].Value = default(TValue);
                _count--;
                return true;
            }
            else
                return false;
        }

        /// <summary>
        /// Removes a random entry from the dictionary.
        /// </summary>
        /// <returns>true if an element; otherwise, false. This method returns false if the dictionary is empty.</returns>
        public bool RemoveRandom()
        {
            int bucketIndex = FindRandomOccupied();
            if (bucketIndex >= 0)
            {
                _buckets[bucketIndex].HashCode = Tombstone;
                _buckets[bucketIndex].Key = default(TKey);
                _buckets[bucketIndex].Value = default(TValue);
                _count--;
                return true;
            }
            else
                return false;
        }

        /// <summary>
        /// Removes a random entry from the dictionary, returning the removed
        /// entry as a KeyValuePair.
        /// </summary>
        /// <returns>A KeyValuePair containing the removed entry.</returns>
        public KeyValuePair<TKey, TValue> RemoveRandomAndGet()
        {
            int bucketIndex = FindRandomOccupied();
            if (bucketIndex >= 0)
            {
                var kvp = new KeyValuePair<TKey, TValue>(_buckets[bucketIndex].Key, _buckets[bucketIndex].Value);
                _buckets[bucketIndex].HashCode = Tombstone;
                _buckets[bucketIndex].Key = default(TKey);
                _buckets[bucketIndex].Value = default(TValue);
                _count--;
                return kvp;
            }
            else
                throw new InvalidOperationException("Dictionary is empty.");
        }

        public TValue GetRandomValue()
        {
            int bucketIndex = FindRandomOccupied();
            if (bucketIndex >= 0)
                return _buckets[bucketIndex].Value;
            else
                throw new InvalidOperationException("Dictionary is empty.");
        }

        public TKey GetRandomKey()
        {
            int bucketIndex = FindRandomOccupied();
            if (bucketIndex >= 0)
                return _buckets[bucketIndex].Key;
            else
                throw new InvalidOperationException("Dictionary is empty.");
        }

        public KeyValuePair<TKey,TValue> GetRandomKeyAndValue()
        {
            int bucketIndex = FindRandomOccupied();
            if (bucketIndex >= 0)
                return new KeyValuePair<TKey, TValue>(_buckets[bucketIndex].Key, _buckets[bucketIndex].Value);
            else
                throw new InvalidOperationException("Dictionary is empty.");
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            int bucketIndex = FindKey(item.Key);

            if (bucketIndex >= 0 && EqualityComparer<TValue>.Default.Equals(item.Value, _buckets[bucketIndex].Value))
            {
                _buckets[bucketIndex].HashCode = Tombstone;
                _buckets[bucketIndex].Key = default(TKey);
                _buckets[bucketIndex].Value = default(TValue);
                _count--;
                return true;
            }
            else
                return false;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            int bucketIndex = FindKey(key);
            if (bucketIndex >= 0)
            {
                value = _buckets[bucketIndex].Value;
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
                    if (buckets[i].HashCode > Unoccupied)
                    {
                        array[arrayIndex] = buckets[i].Key;
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
                        if (buckets[i].HashCode > Unoccupied)
                        {
                            yield return buckets[i].Key;
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
                    if (buckets[i].HashCode > Unoccupied)
                    {
                        array[arrayIndex] = buckets[i].Value;
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
                        if (buckets[i].HashCode > Unoccupied)
                        {
                            yield return buckets[i].Value;
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
