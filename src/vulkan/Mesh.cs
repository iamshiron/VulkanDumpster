using System;
using System.Collections.Generic;
using Silk.NET.Vulkan;

namespace Shiron.VulkanDumpster.Vulkan;

/// <summary>
/// Abstraction for a renderable mesh that lives in a ChunkHeap.
/// </summary>
public class Mesh : IDisposable {
    private readonly VulkanContext _ctx;
    private ChunkHeap.Allocation? _allocation;
    private int _vertexCount;
    private int _indexCount;

    public int VertexCount => _vertexCount;
    public int IndexCount => _indexCount;
    
    public ulong VertexOffset => _allocation?.VertexOffset ?? 0;
    public ulong IndexOffset => _allocation?.IndexOffset ?? 0;
    public bool HasAllocation => _allocation != null;

    public Mesh(VulkanContext ctx) {
        _ctx = ctx;
    }
    
    public void Update(BatchUploader uploader, ChunkHeap heap, ReadOnlySpan<Vertex> vertices, ReadOnlySpan<uint> indices) {
        _vertexCount = vertices.Length;
        _indexCount = indices.Length;

        if (_vertexCount == 0) {
            if (_allocation != null) {
                heap.Free(_allocation.Value);
                _allocation = null;
            }
            return;
        }

        ulong requiredVertexSize = (ulong) (_vertexCount * System.Runtime.CompilerServices.Unsafe.SizeOf<Vertex>());
        ulong requiredIndexSize = (ulong) (_indexCount * sizeof(uint));

        // If we have an allocation and it's too small, free it
        if (_allocation != null && (_allocation.Value.VertexSize < requiredVertexSize || _allocation.Value.IndexSize < requiredIndexSize)) {
            heap.Free(_allocation.Value);
            _allocation = null;
        }

        // If we don't have an allocation, get one
        if (_allocation == null) {
            _allocation = heap.Allocate(requiredVertexSize, requiredIndexSize);
        }

        // Upload
        heap.Upload(_allocation.Value, vertices, indices, uploader);
    }

        public void Free(ChunkHeap heap) {

            if (_allocation != null) {

                heap.Free(_allocation.Value);

                _allocation = null;

            }

        }

    

        public void Dispose() {

            // Allocation must be freed via Free(heap)

        }

    }

    