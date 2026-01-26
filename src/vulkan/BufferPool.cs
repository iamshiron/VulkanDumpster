using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Silk.NET.Vulkan;

namespace Shiron.VulkanDumpster.Vulkan;

public class BufferPool : IDisposable {
    private readonly VulkanContext _ctx;
    private readonly ConcurrentDictionary<int, ConcurrentQueue<VulkanBuffer>> _pools = new();
    
    // We pool buffers based on "buckets" of power-of-two sizes or similar
    // For chunks, they usually fall into a few size categories.

    public BufferPool(VulkanContext ctx) {
        _ctx = ctx;
    }

    public VulkanBuffer Rent(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties) {
        int bucket = GetBucketIndex(size, usage, properties);
        
        if (_pools.TryGetValue(bucket, out var queue)) {
            if (queue.TryDequeue(out var buffer)) {
                return buffer;
            }
        }

        // Allocate new
        ulong allocSize = GetPooledSize(size); // Use power-of-two size
        return new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice, allocSize, usage, properties);
    }

    public void Return(VulkanBuffer buffer) {
        if (buffer.Handle.Handle == 0) return;
        
        // We need to know properties to hash it back
        // VulkanBuffer doesn't store usage/properties publicly, we might need to add them 
        // or just store them in the pool key if we can infer.
        // Actually, VulkanBuffer in this project is simple. 
        // We can just add Usage and Properties to VulkanBuffer class to make this easy.
        
        // For now, let's assume we modify VulkanBuffer to store these.
        int bucket = GetBucketIndex(buffer.Size, buffer.Usage, buffer.MemoryProperties);
        
        var queue = _pools.GetOrAdd(bucket, _ => new ConcurrentQueue<VulkanBuffer>());
        queue.Enqueue(buffer);
    }

    private int GetBucketIndex(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties) {
        // Hash usage and properties into the high bits, size into low bits
        // Next power of two for size
        ulong alignedSize = NextPowerOfTwo(size);
        if (alignedSize < 1024) alignedSize = 1024; // Min 1KB

        int sizeHash = (int)Math.Log2(alignedSize);
        int usageHash = (int)usage;
        int propHash = (int)properties;

        // Simple composite hash
        return HashCode.Combine(sizeHash, usageHash, propHash);
    }

    private ulong GetSizeFromBucket(int bucket) {
        // We can't easily reverse hash. 
        // We actually need the Rent logic to determine size BEFORE hashing.
        // The bucket index is derived from the *Allocated* size, not requested size.
        // So when Renting, we compute NextPowerOfTwo(size) -> AllocSize -> Bucket.
        // When Returning, we compute bucket from buffer.Size.
        return 0; // Not used really, we just allocate NextPowerOfTwo(requested)
    }
    
    // Helper to calculate bucket size for allocation
    public ulong GetPooledSize(ulong size) {
        ulong aligned = NextPowerOfTwo(size);
        if (aligned < 1024) aligned = 1024;
        return aligned;
    }

    private ulong NextPowerOfTwo(ulong v) {
        v--;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        v |= v >> 32;
        v++;
        return v;
    }

    public void Dispose() {
        foreach (var queue in _pools.Values) {
            while (queue.TryDequeue(out var buffer)) {
                buffer.Dispose();
            }
        }
        _pools.Clear();
    }
}
