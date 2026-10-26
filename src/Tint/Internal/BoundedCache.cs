using System.Collections.Concurrent;

namespace Tint.Internal;

// A thread-safe cache keyed by scope name that stops growing at a fixed size. Scope names can be derived from
// captured text (`storage.type.$0.cpp`), so the set of keys is unbounded; past the cap, values are computed each time.
internal sealed class BoundedCache<TValue>
{
    private readonly ConcurrentDictionary<string, TValue> _entries = new(StringComparer.Ordinal);
    private readonly int _capacity;
    private int _count;

    public BoundedCache(int capacity)
    {
        _capacity = capacity;
    }

    public int Count => Volatile.Read(ref _count);

    public TValue GetOrAdd<TState>(string key, Func<string, TState, TValue> factory, TState state)
    {
        if (_entries.TryGetValue(key, out var value))
        {
            return value;
        }

        value = factory(key, state);
        if (Interlocked.Increment(ref _count) <= _capacity)
        {
            if (_entries.TryAdd(key, value))
            {
                return value;
            }

            // Another thread cached it first.
            Interlocked.Decrement(ref _count);
            return _entries.TryGetValue(key, out var existing) ? existing : value;
        }

        Interlocked.Decrement(ref _count);
        return value;
    }
}
