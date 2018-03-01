using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Scaleout.Collections
{
    [DebuggerDisplay("Count = {Count}")]
    [Serializable]
    public class RouletteDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        private int _count = 0;
        IEqualityComparer<TKey> _comparer;

        private struct Bucket
        {
            public int HashCode;
            public TKey Key;
            public TValue Value;
            public bool IsOccupied;
            public bool IsTombstone;
        }

        Bucket[] _buckets;

        public int Count => _count;

        public bool IsReadOnly => false;

        public RouletteDictionary(int capacity = 1, IEqualityComparer<TKey> comparer = null)
        {
            _comparer = comparer ?? EqualityComparer<TKey>.Default;

            // we keep the load factor below .7, so add some wiggle room to the requested
            // capacity to prevent resize operations
            int actualCapacity = Primes.Next((int)(capacity * 1.5));
            _buckets = new Bucket[actualCapacity];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float LoadFactor() { return (float)_count / _buckets.Length; }


        /// <summary>
        /// Adds/updates the dictionary with a new value.
        /// </summary>
        /// <param name="key">Key associated with the value.</param>
        /// <param name="value">New value.</param>
        /// <param name="canUpdate">
        /// Whether an existing element with the same key may be updated.
        /// If false, an ArgumentException is thrown if an item with the same
        /// key alread exists.
        /// </param>
        private void Set(TKey key, TValue value, bool canUpdate)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            int hashcode = _comparer.GetHashCode(key) & 0x7FFFFFFF;
            int bucketIndex = hashcode % _buckets.Length;
            int probeIndex = bucketIndex;

            int indexOfFirstTombstone = -1;
            int probeCount = 0;

            while (probeCount <= _buckets.Length)
            {
                if (_buckets[probeIndex].IsTombstone)
                {
                    if (indexOfFirstTombstone == -1)
                        indexOfFirstTombstone = probeIndex;

                    // need to probe again.
                    probeCount++;
                    probeIndex = bucketIndex + (probeCount * probeCount);
                    if (probeIndex >= _buckets.Length)
                        probeIndex = probeIndex % _buckets.Length;
                    continue;
                }

                if (!_buckets[probeIndex].IsOccupied)
                {
                    // didn't find the entry, adding new value.
                    _count++;

                    // fill in a tombstone, if possible:
                    if (indexOfFirstTombstone != -1)
                        probeIndex = indexOfFirstTombstone;

                    _buckets[probeIndex].HashCode = hashcode;
                    _buckets[probeIndex].Key = key;
                    _buckets[probeIndex].Value = value;
                    _buckets[probeIndex].IsTombstone = false;
                    _buckets[probeIndex].IsOccupied = true;

                    if (LoadFactor() > 0.7)
                        Resize();
                    return;
                }

                if (hashcode == _buckets[probeIndex].HashCode && _comparer.Equals(key, _buckets[probeIndex].Key))
                {
                    // found the entry, set new value
                    if (canUpdate)
                        _buckets[probeIndex].Value = value;
                    else
                        throw new ArgumentException("An element with the same key already exists in the dictionary.", nameof(key));

                    return;
                }
                else
                {
                    // collision, probe again.
                    probeCount++;
                    probeIndex = bucketIndex + (probeCount * probeCount);
                    if (probeIndex >= _buckets.Length)
                        probeIndex = probeIndex % _buckets.Length;
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

            while (probeCount <= _buckets.Length)
            {
                if (_buckets[probeIndex].IsTombstone)
                {
                    // need to probe again.
                    probeCount++;
                    probeIndex = bucketIndex + (probeCount * probeCount);
                    if (probeIndex >= _buckets.Length)
                        probeIndex = probeIndex % _buckets.Length;
                    continue;
                }

                if (!_buckets[probeIndex].IsOccupied)
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
                    probeIndex = bucketIndex + (probeCount * probeCount);
                    if (probeIndex >= _buckets.Length)
                        probeIndex = probeIndex % _buckets.Length;
                    continue;
                }
            }
            throw new ApplicationException("too many probes");
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
                Set(key, value, canUpdate: true);
            }
        }

        private void Resize()
        {
            var newBuckets = new Bucket[Primes.Next((_buckets.Length * 2) + 1)];

            for (int i = 0; i < _buckets.Length; i++)
            {
                if (!_buckets[i].IsOccupied || _buckets[i].IsTombstone)
                    continue;

                int bucketIndex = _buckets[i].HashCode % newBuckets.Length;
                int probeIndex = bucketIndex;
                int probeCount = 0;
                while (true)
                {
                    if (probeCount == newBuckets.Length)
                        throw new ApplicationException("too many probes");

                    if (!newBuckets[probeIndex].IsOccupied)
                    {
                        newBuckets[probeIndex].HashCode = _buckets[i].HashCode;
                        newBuckets[probeIndex].Key = _buckets[i].Key;
                        newBuckets[probeIndex].Value = _buckets[i].Value;
                        newBuckets[probeIndex].IsTombstone = false;
                        newBuckets[probeIndex].IsOccupied = true;
                        break;
                    }
                    else
                    {
                        // collision. probe again.
                        probeCount++;
                        probeIndex = bucketIndex + (probeCount * probeCount);
                        if (probeIndex >= newBuckets.Length)
                            probeIndex = probeIndex % newBuckets.Length;
                    }

                }
            }

            _buckets = newBuckets;
        }

        public ICollection<TKey> Keys => throw new NotImplementedException();

        public ICollection<TValue> Values => throw new NotImplementedException();

        public void Add(TKey key, TValue value)
        {
            Set(key, value, canUpdate: false);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            Set(item.Key, item.Value, canUpdate: false);
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

            int destIndex = 0;
            for (int i = 0; i < _buckets.Length; i++)
            {
                if (_buckets[i].IsOccupied && !_buckets[i].IsTombstone)
                {
                    array[destIndex] = new KeyValuePair<TKey, TValue>(_buckets[i].Key, _buckets[i].Value);
                    destIndex++;
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
                    if (_buckets[i].IsOccupied && !_buckets[i].IsTombstone)
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
                _buckets[bucketIndex].IsOccupied = false;
                _buckets[bucketIndex].IsTombstone = true;
                _buckets[bucketIndex].HashCode = 0;
                _buckets[bucketIndex].Key = default(TKey);
                _buckets[bucketIndex].Value = default(TValue);
                _count--;
                return true;
            }
            else
                return false;
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            int bucketIndex = FindKey(item.Key);

            if (bucketIndex >= 0 && EqualityComparer<TValue>.Default.Equals(item.Value, _buckets[bucketIndex].Value))
            {
                _buckets[bucketIndex].IsOccupied = false;
                _buckets[bucketIndex].IsTombstone = true;
                _buckets[bucketIndex].HashCode = 0;
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
    }
}
