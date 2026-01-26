using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Shiron.VulkanDumpster.Vulkan;

namespace Shiron.VulkanDumpster.Vulkan;

/// <summary>
/// A "Mega-Buffer" that manages large global vertex and index buffers.
/// Uses a simple free-list allocator to manage sub-allocations for meshes.
/// </summary>
public class ChunkHeap : IDisposable {
    private readonly VulkanContext _ctx;
    private VulkanBuffer _vertexBuffer;
    private VulkanBuffer _indexBuffer;

    public VulkanBuffer VertexBuffer => _vertexBuffer;
    public VulkanBuffer IndexBuffer => _indexBuffer;

    private readonly SimpleAllocator _vertexAllocator;
    private readonly SimpleAllocator _indexAllocator;

    public ChunkHeap(VulkanContext ctx, ulong vertexBufferSize, ulong indexBufferSize) {
        _ctx = ctx;
        _vertexBuffer = new VulkanBuffer(ctx.Vk, ctx.Device, ctx.PhysicalDevice,
            vertexBufferSize,
            BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.DeviceLocalBit);

        _indexBuffer = new VulkanBuffer(ctx.Vk, ctx.Device, ctx.PhysicalDevice,
            indexBufferSize,
            BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.DeviceLocalBit);

        _vertexAllocator = new SimpleAllocator(vertexBufferSize);
        _indexAllocator = new SimpleAllocator(indexBufferSize);
    }

    public Allocation Allocate(ulong vertexSize, ulong indexSize) {
        if (!_vertexAllocator.TryAllocate(vertexSize, out ulong vOffset)) {
            GrowVertex(vertexSize);
            if (!_vertexAllocator.TryAllocate(vertexSize, out vOffset)) {
                throw new Exception("Failed to allocate vertex buffer even after growth");
            }
        }
        if (!_indexAllocator.TryAllocate(indexSize, out ulong iOffset)) {
            GrowIndex(indexSize);
            if (!_indexAllocator.TryAllocate(indexSize, out iOffset)) {
                _vertexAllocator.Free(vOffset, vertexSize);
                throw new Exception("Failed to allocate index buffer even after growth");
            }
        }
        return new Allocation(vOffset, iOffset, vertexSize, indexSize);
    }

    private unsafe void GrowVertex(ulong requiredSize) {
        ulong oldSize = _vertexAllocator.TotalSize;
        ulong newSize = Math.Max(oldSize * 2, oldSize + ((requiredSize + 3) & ~3UL));
        Console.WriteLine($"[ChunkHeap] Growing vertex buffer: {oldSize / 1024 / 1024}MB -> {newSize / 1024 / 1024}MB");

        var newBuffer = new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
            newSize,
            BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.DeviceLocalBit);
        
        CopyBuffer(_vertexBuffer.Handle, newBuffer.Handle, oldSize);
        
        var oldBuffer = _vertexBuffer;
        _ctx.EnqueueDispose(() => oldBuffer.Dispose());
        _vertexBuffer = newBuffer;
        _vertexAllocator.Grow(newSize);
    }

    private unsafe void GrowIndex(ulong requiredSize) {
        ulong oldSize = _indexAllocator.TotalSize;
        ulong newSize = Math.Max(oldSize * 2, oldSize + ((requiredSize + 3) & ~3UL));
        Console.WriteLine($"[ChunkHeap] Growing index buffer: {oldSize / 1024 / 1024}MB -> {newSize / 1024 / 1024}MB");

        var newBuffer = new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
            newSize,
            BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.DeviceLocalBit);
        
        CopyBuffer(_indexBuffer.Handle, newBuffer.Handle, oldSize);
        
        var oldBuffer = _indexBuffer;
        _ctx.EnqueueDispose(() => oldBuffer.Dispose());
        _indexBuffer = newBuffer;
        _indexAllocator.Grow(newSize);
    }

    private unsafe void CopyBuffer(Silk.NET.Vulkan.Buffer src, Silk.NET.Vulkan.Buffer dst, ulong size) {
        var allocInfo = new CommandBufferAllocateInfo {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = _ctx.CommandPool,
            CommandBufferCount = 1
        };
        _ctx.Vk.AllocateCommandBuffers(_ctx.Device, &allocInfo, out var cmd);

        var beginInfo = new CommandBufferBeginInfo {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        _ctx.Vk.BeginCommandBuffer(cmd, &beginInfo);

        var copyRegion = new BufferCopy {
            SrcOffset = 0,
            DstOffset = 0,
            Size = size
        };
        _ctx.Vk.CmdCopyBuffer(cmd, src, dst, 1, &copyRegion);

        _ctx.Vk.EndCommandBuffer(cmd);

        var submitInfo = new SubmitInfo {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd
        };
        _ctx.Vk.QueueSubmit(_ctx.GraphicsQueue, 1, &submitInfo, default);
        _ctx.Vk.QueueWaitIdle(_ctx.GraphicsQueue);

        _ctx.Vk.FreeCommandBuffers(_ctx.Device, _ctx.CommandPool, 1, &cmd);
    }

    public void Free(Allocation allocation) {
        _vertexAllocator.Free(allocation.VertexOffset, allocation.VertexSize);
        _indexAllocator.Free(allocation.IndexOffset, allocation.IndexSize);
    }

    public void Upload(Allocation allocation, ReadOnlySpan<Vertex> vertices, ReadOnlySpan<uint> indices, BatchUploader uploader) {
        if (!uploader.Upload(vertices, _vertexBuffer, allocation.VertexOffset)) {
            _vertexBuffer.UploadData(vertices, allocation.VertexOffset, _ctx.CommandPool, _ctx.GraphicsQueue, _ctx);
        }
        if (!uploader.Upload(indices, _indexBuffer, allocation.IndexOffset)) {
            _indexBuffer.UploadData(indices, allocation.IndexOffset, _ctx.CommandPool, _ctx.GraphicsQueue, _ctx);
        }
    }

    public void Bind(VulkanCommandBuffer cmd) {
        cmd.BindVertexBuffer(_vertexBuffer);
        cmd.BindIndexBuffer(_indexBuffer, IndexType.Uint32);
    }

    public void Dispose() {
        _vertexBuffer.Dispose();
        _indexBuffer.Dispose();
    }

    public struct Allocation {
        public ulong VertexOffset;
        public ulong IndexOffset;
        public ulong VertexSize;
        public ulong IndexSize;

        public Allocation(ulong vOffset, ulong iOffset, ulong vSize, ulong iSize) {
            VertexOffset = vOffset;
            IndexOffset = iOffset;
            VertexSize = vSize;
            IndexSize = iSize;
        }
    }

    private class SimpleAllocator {
        private readonly List<Range> _freeRanges = new();
        public ulong TotalSize { get; private set; }

        public SimpleAllocator(ulong totalSize) {
            TotalSize = totalSize;
            _freeRanges.Add(new Range(0, totalSize));
        }

        public void Grow(ulong newTotalSize) {
            if (newTotalSize <= TotalSize) return;
            ulong additional = newTotalSize - TotalSize;
            ulong oldSize = TotalSize;
            TotalSize = newTotalSize;
            Free(oldSize, additional);
        }

        public bool TryAllocate(ulong size, out ulong offset) {
            // Align to 4 bytes
            size = (size + 3) & ~3UL;

            for (int i = 0; i < _freeRanges.Count; i++) {
                var range = _freeRanges[i];
                if (range.Size >= size) {
                    offset = range.Offset;
                    if (range.Size == size) {
                        _freeRanges.RemoveAt(i);
                    } else {
                        _freeRanges[i] = new Range(range.Offset + size, range.Size - size);
                    }
                    return true;
                }
            }
            offset = 0;
            return false;
        }

        public void Free(ulong offset, ulong size) {
            size = (size + 3) & ~3UL;
            var newRange = new Range(offset, size);
            
            // Insert and merge
            int insertIndex = 0;
            while (insertIndex < _freeRanges.Count && _freeRanges[insertIndex].Offset < offset) {
                insertIndex++;
            }
            _freeRanges.Insert(insertIndex, newRange);

            // Merge with next
            if (insertIndex + 1 < _freeRanges.Count) {
                var next = _freeRanges[insertIndex + 1];
                if (newRange.Offset + newRange.Size == next.Offset) {
                    newRange = new Range(newRange.Offset, newRange.Size + next.Size);
                    _freeRanges[insertIndex] = newRange;
                    _freeRanges.RemoveAt(insertIndex + 1);
                }
            }

            // Merge with previous
            if (insertIndex > 0) {
                var prev = _freeRanges[insertIndex - 1];
                if (prev.Offset + prev.Size == newRange.Offset) {
                    _freeRanges[insertIndex - 1] = new Range(prev.Offset, prev.Size + newRange.Size);
                    _freeRanges.RemoveAt(insertIndex);
                }
            }
        }

        private struct Range {
            public ulong Offset;
            public ulong Size;
            public Range(ulong offset, ulong size) {
                Offset = offset;
                Size = size;
            }
        }
    }
}
