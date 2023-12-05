The answer to part 2:

In the implementation, a size-limited cache is achieved using a combination of a Dictionary (_cache) to store key-value pairs and a Queue (_cacheOrder) to maintain the order of access. The Dictionary stores the mappings, and the Queue is used to keep track of the order in which items are accessed.

The Advantages:

1. It's simplicity, it's pretty straightforward and easy to implement.

2. It's efficient, the size limitation ensures that the cache does not grow indefinitely, helping to manage memory 
efficiently.

3. Using the Queue allows the application to approximate LRU("Least Recently Used") eviction by dequeuing and re-enqueuing items based on their access order.


The disadvantages:

1. This approach provides only an approximation of LRU eviction but it may not reflect the exact order of access in a multithreaded environment.
