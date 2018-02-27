using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Scaleout.Collections
{
    public class RouletteDictionary<TKey, TValue> : IDictionary<TKey, TValue>
    {
        private int _count = 0;
        IEqualityComparer<TKey> _comparer;

        private struct Bucket
        {
            public int HashCode;
            public TKey Key;
            public TValue Value;
            public bool isOccupied;
            public bool isTombstone;
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

        public TValue this[TKey key]
        {
            get
            {
                int hashcode = _comparer.GetHashCode(key) & 0x7FFFFFFF;
                int bucketIndex = hashcode % _buckets.Length;
                int probeIndex = bucketIndex;
                int probeCount = 0;

                while (probeCount <= _buckets.Length)
                {
                    if (_buckets[probeIndex].isTombstone)
                    {
                        // need to probe again.
                        probeCount++;
                        probeIndex = bucketIndex + (probeCount * probeCount);
                        if (probeIndex >= _buckets.Length)
                            probeIndex = probeIndex % _buckets.Length;
                        continue;
                    }

                    if (!_buckets[probeIndex].isOccupied)
                    {
                        // didn't find the entry
                        throw new KeyNotFoundException("key does not exist in the collection");
                    }

                    if (hashcode == _buckets[probeIndex].HashCode && _comparer.Equals(key, _buckets[probeIndex].Key))
                    {
                        // found the entry, return the value
                        return _buckets[probeIndex].Value;
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

            set
            {
                int hashcode = _comparer.GetHashCode(key) & 0x7FFFFFFF;
                int bucketIndex = hashcode % _buckets.Length;
                int probeIndex = bucketIndex;

                int indexOfFirstTombstone = -1;
                int probeCount = 0;

                while (probeCount <= _buckets.Length)
                {
                    if (_buckets[probeIndex].isTombstone)
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

                    if (!_buckets[probeIndex].isOccupied)
                    {
                        // didn't find the entry, adding new value.
                        _count++;

                        // fill in a tombstone, if possible:
                        if (indexOfFirstTombstone != -1)
                            probeIndex = indexOfFirstTombstone;

                        _buckets[probeIndex].HashCode = hashcode;
                        _buckets[probeIndex].Key = key;
                        _buckets[probeIndex].Value = value;
                        _buckets[probeIndex].isTombstone = false;
                        _buckets[probeIndex].isOccupied = true;

                        if (LoadFactor() > 0.7)
                            Resize();
                        return;
                    }

                    if (hashcode == _buckets[probeIndex].HashCode && _comparer.Equals(key, _buckets[probeIndex].Key))
                    {
                        // found the entry, set new value
                        _buckets[probeIndex].Value = value;
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
        }

        private void Resize()
        {
            var newBuckets = new Bucket[Primes.Next((_buckets.Length * 2) + 1)];

            for (int i = 0; i < _buckets.Length; i++)
            {
                if (!_buckets[i].isOccupied || _buckets[i].isTombstone)
                    continue;

                int bucketIndex = _buckets[i].HashCode % newBuckets.Length;
                int probeIndex = bucketIndex;
                int probeCount = 0;
                while (true)
                {
                    if (probeCount == newBuckets.Length)
                        throw new ApplicationException("too many probes");

                    if (!newBuckets[probeIndex].isOccupied)
                    {
                        newBuckets[probeIndex].HashCode = _buckets[i].HashCode;
                        newBuckets[probeIndex].Key = _buckets[i].Key;
                        newBuckets[probeIndex].Value = _buckets[i].Value;
                        newBuckets[probeIndex].isTombstone = false;
                        newBuckets[probeIndex].isOccupied = true;
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
            throw new NotImplementedException();
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        public bool ContainsKey(TKey key)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        public bool Remove(TKey key)
        {
            throw new NotImplementedException();
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }
}
