#if NET35

using System.Collections.Generic;
using System.Threading;

namespace NetFX.Polyfill2
{
    public class BlockingCollection<T>
    {
        private readonly object _queueLock = new();
        private readonly Queue<T> _queue = new();
        private readonly int _boundedCapacity;

        public BlockingCollection() : this(int.MaxValue)
        {
        }

        public BlockingCollection(int boundedCapacity)
        {
            if (boundedCapacity <= 0) throw new System.ArgumentOutOfRangeException(nameof(boundedCapacity));
            _boundedCapacity = boundedCapacity;
        }

        public T Take()
        {
            lock (_queueLock)
            {
                while (_queue.Count == 0)
                {
                    Monitor.Wait(_queueLock);
                }
                var value = _queue.Dequeue();
                Monitor.PulseAll(_queueLock);
                return value;
            }
        }

        public void Add(T obj)
        {
            lock (_queueLock)
            {
                while (_queue.Count >= _boundedCapacity)
                {
                    Monitor.Wait(_queueLock);
                }
                _queue.Enqueue(obj);
                Monitor.PulseAll(_queueLock);
            }
        }
    }
}

#endif
