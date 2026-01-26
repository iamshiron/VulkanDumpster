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
        // Take ownership of the lists to avoid copying
        // The caller MUST NOT reuse these lists after passing them
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
        ulong requiredVertexSize = (ulong) (_vertices.Count * System.Runtime.InteropServices.Marshal.SizeOf<Vertex>());
        if (_vertexBuffer == null || _vertexBuffer.Size < requiredVertexSize) {
            var oldBuffer = _vertexBuffer;
            if (oldBuffer != null) {
                _ctx.EnqueueDispose(() => oldBuffer.Dispose());
            }
            _vertexBuffer = new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
                requiredVertexSize,
                BufferUsageFlags.VertexBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.DeviceLocalBit);
        }
        // Use Span to avoid ToArray() allocation
        var vertexSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_vertices);
        _vertexBuffer.UploadData(vertexSpan, _ctx.CommandPool, _ctx.GraphicsQueue, _ctx);
        if (_indices.Count > 0) {
            ulong requiredIndexSize = (ulong) (_indices.Count * sizeof(uint));
            if (_indexBuffer == null || _indexBuffer.Size < requiredIndexSize) {
                var oldBuffer = _indexBuffer;
                if (oldBuffer != null) {
                    _ctx.EnqueueDispose(() => oldBuffer.Dispose());
                }
                _indexBuffer = new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
                    requiredIndexSize,
                    BufferUsageFlags.IndexBufferBit | BufferUsageFlags.TransferDstBit,
                    MemoryPropertyFlags.DeviceLocalBit);
            }
            var indexSpan = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_indices);
            _indexBuffer.UploadData(indexSpan, _ctx.CommandPool, _ctx.GraphicsQueue, _ctx);
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
        // Defer destruction to ensure GPU is done using them
        if (vb != null || ib != null) {
            _ctx.EnqueueDispose(() => {
                vb?.Dispose();
                ib?.Dispose();
            });
        }
        _vertexBuffer = null;
        _indexBuffer = null;
    }
}
