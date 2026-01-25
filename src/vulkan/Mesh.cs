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
    private readonly List<Vertex> _vertices = new();
    private readonly List<uint> _indices = new();

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

    public void Clear() {
        _vertices.Clear();
        _indices.Clear();
        _isDirty = true;
    }

    /// <summary>
    /// Marks the mesh as ready for upload.
    /// </summary>
    public void Build() {
        _isDirty = true;
    }

    /// <summary>
    /// Uploads the mesh data to the GPU if it has been modified.
    /// Should be called before rendering.
    /// </summary>
    public void UpdateGpuBuffers() {
        if (!_isDirty || _vertices.Count == 0) return;

        // Ensure buffers are large enough or recreate if necessary
        ulong requiredVertexSize = (ulong)(_vertices.Count * System.Runtime.InteropServices.Marshal.SizeOf<Vertex>());
        if (_vertexBuffer == null || _vertexBuffer.Size < requiredVertexSize) {
            _vertexBuffer?.Dispose();
            _vertexBuffer = new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
                requiredVertexSize,
                BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.DeviceLocalBit);
        }

        _vertexBuffer.UploadData(_vertices.ToArray(), _ctx.CommandPool, _ctx.GraphicsQueue);

        if (_indices.Count > 0) {
            ulong requiredIndexSize = (ulong)(_indices.Count * sizeof(uint));
            if (_indexBuffer == null || _indexBuffer.Size < requiredIndexSize) {
                _indexBuffer?.Dispose();
                _indexBuffer = new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
                    requiredIndexSize,
                    BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
                    MemoryPropertyFlags.DeviceLocalBit);
            }
            _indexBuffer.UploadData(_indices.ToArray(), _ctx.CommandPool, _ctx.GraphicsQueue);
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
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
    }
}
