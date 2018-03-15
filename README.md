# ScaleOut Software Collections for .NET

Useful building blocks for in-process caches.

Nuget: `Install-Package Scaleout.Collections -Pre`

API Documentation: https://scaleoutsoftware.github.io/ScaleoutCollectionsDotNet/

## Overview

This library provides generic dictionaries designed to simplify the
creation of an in-process cache. Two classes are available:

* [RecentDictionary](https://scaleoutsoftware.github.io/ScaleoutCollectionsDotNet/html/ea9f01b3-f389-21b7-b763-b1c3f18bc366.htm):
  A collection of keys and values that tracks the order in which
  entries are accessed, suitable for creating a cache with an LRU or
  MRU eviction policy.
* [RouletteDictionary](https://scaleoutsoftware.github.io/ScaleoutCollectionsDotNet/html/835bf4de-10d5-9283-e0d4-95918772829c.htm):
  A collection of keys and values that allows random entries to be
  retrieved or removed, suitable for creating a cache with a random
  eviction policy.
  
Both classes implement a *SetAndMaintainCount* method that can be used
to set values while keeping the dictionary at a fixed size. If the
operation results in a value being added, another entry will be
removed to make room: The RecentDictionary will evict either the
most-recently or least-recently used entry (depending on the eviction
mode passed into the constructor), and RouletteDictionary will evict a
random entry.

## Motivation

These collections simplfiy the creation of in-memory caches that
require an eviction policy, often with better performance and lower
memory usage than traditional approaches.

### RecentDictionary 

The
[RecentDictionary](https://scaleoutsoftware.github.io/ScaleoutCollectionsDotNet/html/ea9f01b3-f389-21b7-b763-b1c3f18bc366.htm)
is a hybrid linked list and hash table--the LRU previous/next
references are stored directly in hashtable bucket nodes. Performance
for get/set operations are typically 20% faster than a traditional LRU
cache where the dictionary and linked list are maintained as separate
data structures. Memory savings will vary depending on key size.

### RouletteDictionary

Random eviction using a standard .NET Dictionary is not possible
without maintaining a separate collection of
keys. The
[RouletteDictionary](https://scaleoutsoftware.github.io/ScaleoutCollectionsDotNet/html/835bf4de-10d5-9283-e0d4-95918772829c.htm)
addresses this by making it straightforward to retrieve or remove random
elements.

### Example

The following sample illustrates how an LRU cache could be created
using the RecentDictionary class.

```csharp
using Scaleout.Collections;

// A basic LRU cache
class MyLruCache<TKey, TValue>
{
    public int MaxSize { get; }
    private RecentDictionary<TKey, TValue> _entries;
    private object _guard = new object();

    public MyLruCache(int maxSize)
    {
        MaxSize = maxSize;
        _entries = new RecentDictionary<TKey, TValue>(capacity: maxSize,
                                                      evictionMode: RecentDictionaryEvictionMode.LRU,
                                                      comparer: null);
    }

    // Gets an item from the cache.
    public TValue Get(TKey key)
    {
        lock (_guard)
        {
            // TryGetValue() makes the entry the most-recently accessed:
            bool found = _entries.TryGetValue(key, out TValue entry);
            if (found)
                return entry;
            else
                return default;
        }
    }

    // Adds/updates an item in the cache.
    public void Set(TKey key, TValue value)
    {
        lock (_guard)
        {
            if (_entries.Count == MaxSize)
            {
                // We're at our cache's capacity. SetAndMaintainCount will
                // cause the least recently used item to be evicted if a new
                // entry needs to be added.
                _entries.SetAndMaintainCount(key, value);
            }
            else
            {
                // Not at a max capacity yet.
                // The ordinary setter does not perform eviction.
                _entries[key] = value;
            }
        }
    }

    // Removes an item from the cache.
    public void Remove(TKey key)
    {
        lock (_guard)
        {
            _entries.Remove(key);
        }
    }

}
```

## License

Apache 2
