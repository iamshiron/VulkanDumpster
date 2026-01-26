using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Shiron.VulkanDumpster;

/// <summary>
/// A simple thread-safe pool for unmanaged memory buffers to avoid the overhead of frequent Alloc/Free.
/// </summary>
public static unsafe class UnmanagedPool {
    private static readonly ConcurrentDictionary<nuint, ConcurrentQueue<IntPtr>> _pools = new();

    public static void* Rent(nuint size) {
        if (!_pools.TryGetValue(size, out var queue)) {
            queue = _pools.GetOrAdd(size, _ => new ConcurrentQueue<IntPtr>());
        }

        if (queue.TryDequeue(out var ptr)) {
            return (void*)ptr;
        }

        void* newPtr = (void*)NativeMemory.Alloc(size);
        if (newPtr == null) {
            throw new OutOfMemoryException($"Failed to allocate {size} bytes of unmanaged memory.");
        }
        return newPtr;
    }

    public static void Return(void* ptr, nuint size) {
        if (ptr == null) return;
        
        if (_pools.TryGetValue(size, out var queue)) {
            queue.Enqueue((IntPtr)ptr);
        } else {
            // Should not happen if Rent was used, but for safety:
            NativeMemory.Free(ptr);
        }
    }

    public static void Cleanup() {
        foreach (var queue in _pools.Values) {
            while (queue.TryDequeue(out var ptr)) {
                NativeMemory.Free((void*)ptr);
            }
        }
        _pools.Clear();
    }
}
