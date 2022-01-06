API Documentation: [https://scaleoutsoftware.github.io/ScaleoutCollectionsDotNet/](https://scaleoutsoftware.github.io/ScaleoutCollectionsDotNet/)

This library provides generic dictionaries designed to simplify the creation of an in-process cache. Two classes are available:

* [RecentDictionary](https://scaleoutsoftware.github.io/ScaleoutCollectionsDotNet/html/ea9f01b3-f389-21b7-b763-b1c3f18bc366.htm): A collection of keys and values that tracks the order in which entries are accessed, suitable for creating a cache with an LRU or MRU eviction policy.
* [RouletteDictionary](https://scaleoutsoftware.github.io/ScaleoutCollectionsDotNet/html/835bf4de-10d5-9283-e0d4-95918772829c.htm): A collection of keys and values that allows random entries to be retrieved or removed, suitable for creating a cache with a random eviction policy.