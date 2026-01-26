using System;
using System.Collections.Generic;
using Silk.NET.Vulkan;
namespace Shiron.VulkanDumpster.Vulkan;
/// <summary>
/// Abstraction for a renderable mesh.
/// Manages vertex/index data and synchronizes with GPU buffers.
/// </summary>
public class Mesh : IDisposable {
    private readonly VulkanContext _ctx;
    private List<Vertex> _vertices = new();
    private List<uint> _indices = new();
    private VulkanBuffer? _vertexBuffer;
    private VulkanBuffer? _indexBuffer;
    private bool _isDirty = true;
    public int VertexCount => _vertices.Count;
    public int IndexCount => _indices.Count;
    public VulkanBuffer? VertexBuffer => _vertexBuffer;
    public VulkanBuffer? IndexBuffer => _indexBuffer;
    public Mesh(VulkanContext ctx) {
        _ctx = ctx;
    }
    public void AddVertex(Vertex vertex) {
        _vertices.Add(vertex);
        _isDirty = true;
    }
    public void AddIndex(uint index) {
        _indices.Add(index);
        _isDirty = true;
    }
    public (List<Vertex>, List<uint>) SetData(List<Vertex> vertices, List<uint> indices) {
        var oldV = _vertices;
        var oldI = _indices;
        _vertices = vertices;
        _indices = indices;
        _isDirty = true;
        return (oldV, oldI);
    }
    public void Clear() {
        _vertices.Clear();
        _indices.Clear();
        _isDirty = true;
    }
    public void Build() {
        _isDirty = true;
    }
    
    public void UpdateGpuBuffers(BatchUploader uploader) {
        if (!_isDirty || _vertices.Count == 0) return;

        ulong requiredVertexSize = (ulong) (_vertices.Count * System.Runtime.InteropServices.Marshal.SizeOf<Vertex>());
        if (_vertexBuffer == null || _vertexBuffer.Size < requiredVertexSize) {
            EnsureVertexBuffer(requiredVertexSize);
        }

        var vertexSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices);
        if (!uploader.Upload(vertexSpan, _vertexBuffer!, 0)) {
            _vertexBuffer!.UploadData(vertexSpan, _ctx.CommandPool, _ctx.GraphicsQueue, _ctx);
        }

        if (_indices.Count > 0) {
            ulong requiredIndexSize = (ulong) (_indices.Count * sizeof(uint));
            if (_indexBuffer == null || _indexBuffer.Size < requiredIndexSize) {
                EnsureIndexBuffer(requiredIndexSize);
            }
            var indexSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_indices);
            if (!uploader.Upload(indexSpan, _indexBuffer!, 0)) {
                 _indexBuffer!.UploadData(indexSpan, _ctx.CommandPool, _ctx.GraphicsQueue, _ctx);
            }
        }
        _isDirty = false;
    }

    private void EnsureVertexBuffer(ulong size) {
        var oldBuffer = _vertexBuffer;
        if (oldBuffer != null) {
            _ctx.EnqueueDispose(() => _ctx.BufferPool.Return(oldBuffer));
        }
        _vertexBuffer = _ctx.BufferPool.Rent(size,
            BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.DeviceLocalBit);
    }

    private void EnsureIndexBuffer(ulong size) {
        var oldBuffer = _indexBuffer;
        if (oldBuffer != null) {
            _ctx.EnqueueDispose(() => _ctx.BufferPool.Return(oldBuffer));
        }
        _indexBuffer = _ctx.BufferPool.Rent(size,
            BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
            MemoryPropertyFlags.DeviceLocalBit);
    }

    // Legacy method kept for compatibility if needed, but updated to use pool
    public void UpdateGpuBuffers() {
        if (!_isDirty || _vertices.Count == 0) return;
        ulong requiredVertexSize = (ulong) (_vertices.Count * System.Runtime.InteropServices.Marshal.SizeOf<Vertex>());
        if (_vertexBuffer == null || _vertexBuffer.Size < requiredVertexSize) {
            EnsureVertexBuffer(requiredVertexSize);
        }
        var vertexSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices);
        _vertexBuffer!.UploadData(vertexSpan, _ctx.CommandPool, _ctx.GraphicsQueue, _ctx);
        if (_indices.Count > 0) {
            ulong requiredIndexSize = (ulong) (_indices.Count * sizeof(uint));
            if (_indexBuffer == null || _indexBuffer.Size < requiredIndexSize) {
                EnsureIndexBuffer(requiredIndexSize);
            }
            var indexSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_indices);
            _indexBuffer!.UploadData(indexSpan, _ctx.CommandPool, _ctx.GraphicsQueue, _ctx);
        }
        _isDirty = false;
    }

    public void Bind(VulkanCommandBuffer cmd) {
        if (_vertexBuffer != null) {
            cmd.BindVertexBuffer(_vertexBuffer);
        }
        if (_indexBuffer != null) {
            cmd.BindIndexBuffer(_indexBuffer, IndexType.Uint32);
        }
    }

    public void Dispose() {
        var vb = _vertexBuffer;
        var ib = _indexBuffer;
        if (vb != null || ib != null) {
            _ctx.EnqueueDispose(() => {
                if (vb != null) _ctx.BufferPool.Return(vb);
                if (ib != null) _ctx.BufferPool.Return(ib);
            });
        }
        _vertexBuffer = null;
        _indexBuffer = null;
    }
}