using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
namespace Shiron.VulkanDumpster.Vulkan;

public unsafe class DebugRenderer : IDisposable {
    private readonly VulkanContext _ctx;
    private readonly Renderer _renderer;
    private VulkanPipeline _pipeline;
    private VulkanBuffer[] _vertexBuffers;
    private const int InitialVertexCapacity = 10000;
    private readonly List<DebugVertex> _vertices = new();
    private struct DebugVertex {
        public Vector3D<float> Position;
        public Vector3D<float> Color;
        public DebugVertex(Vector3D<float> pos, Vector3D<float> col) {
            Position = pos;
            Color = col;
        }
        public static VertexInputBindingDescription GetBindingDescription() {
            return new VertexInputBindingDescription {
                Binding = 0,
                Stride = (uint) Unsafe.SizeOf<DebugVertex>(),
                InputRate = VertexInputRate.Vertex
            };
        }
        public static VertexInputAttributeDescription[] GetAttributeDescriptions() {
            return new[] {
                new VertexInputAttributeDescription {
                    Binding = 0,
                    Location = 0,
                    Format = Format.R32G32B32Sfloat,
                    Offset = (uint)Marshal.OffsetOf<DebugVertex>(nameof(Position))
                },
                new VertexInputAttributeDescription {
                    Binding = 0,
                    Location = 1,
                    Format = Format.R32G32B32Sfloat,
                    Offset = (uint)Marshal.OffsetOf<DebugVertex>(nameof(Color))
                }
            };
        }
    }
    public DebugRenderer(VulkanContext ctx, Renderer renderer, DescriptorSetLayout globalLayout) {
        _ctx = ctx;
        _renderer = renderer;
        _vertexBuffers = new VulkanBuffer[3];
        CreatePipeline(globalLayout);
        CreateBuffers(InitialVertexCapacity);
    }
    private void CreatePipeline(DescriptorSetLayout globalLayout) {
        ShaderUtils.LoadShaderModule(_ctx.Vk, _ctx.Device, "shaders/debug_line.vert.spv", out var vert);
        ShaderUtils.LoadShaderModule(_ctx.Vk, _ctx.Device, "shaders/debug_line.frag.spv", out var frag);
        var pushConstantRange = new PushConstantRange {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = (uint) sizeof(Matrix4X4<float>)
        };
        var layoutInfo = new PipelineLayoutCreateInfo {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &globalLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstantRange
        };
        _ctx.Vk.CreatePipelineLayout(_ctx.Device, &layoutInfo, null, out var layout);
        var builder = new PipelineBuilder(_ctx.Vk) { PipelineLayout = layout };
        builder.SetShaders(vert, frag)
               .SetInputTopology(PrimitiveTopology.LineList)
               .SetPolygonMode(PolygonMode.Line)
               .SetCullMode(CullModeFlags.None, FrontFace.Clockwise)
               .SetMultisamplingNone()
               .DisableBlending()
               .EnableDepthTest(true, CompareOp.Less)
               .SetVertexInput(DebugVertex.GetBindingDescription(), DebugVertex.GetAttributeDescriptions())
               .SetColorAttachmentFormat(_renderer.SwapchainImageFormat)
               .SetDepthFormat(_renderer.DepthFormat);
        var pipeline = builder.Build(_ctx.Device);
        _pipeline = new VulkanPipeline(_ctx.Vk, _ctx.Device, pipeline, layout, "DebugLines");
        _ctx.Vk.DestroyShaderModule(_ctx.Device, vert, null);
        _ctx.Vk.DestroyShaderModule(_ctx.Device, frag, null);
    }
    private void CreateBuffers(int capacity) {
        for (int i = 0; i < _vertexBuffers.Length; i++) {
            var oldBuffer = _vertexBuffers[i];
            if (oldBuffer != null) {
                _ctx.EnqueueDispose(() => oldBuffer.Dispose());
            }
            _vertexBuffers[i] = new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
                (ulong) (capacity * Unsafe.SizeOf<DebugVertex>()),
                BufferUsageFlags.VertexBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }
    }
    public void Begin() {
        _vertices.Clear();
    }
    public void DrawLine(Vector3D<float> p1, Vector3D<float> p2, Vector3D<float> color) {
        _vertices.Add(new DebugVertex(p1, color));
        _vertices.Add(new DebugVertex(p2, color));
    }
    public void DrawBox(Vector3D<float> min, Vector3D<float> max, Vector3D<float> color) {
        // Bottom
        DrawLine(new Vector3D<float>(min.X, min.Y, min.Z), new Vector3D<float>(max.X, min.Y, min.Z), color);
        DrawLine(new Vector3D<float>(max.X, min.Y, min.Z), new Vector3D<float>(max.X, min.Y, max.Z), color);
        DrawLine(new Vector3D<float>(max.X, min.Y, max.Z), new Vector3D<float>(min.X, min.Y, max.Z), color);
        DrawLine(new Vector3D<float>(min.X, min.Y, max.Z), new Vector3D<float>(min.X, min.Y, min.Z), color);
        // Top
        DrawLine(new Vector3D<float>(min.X, max.Y, min.Z), new Vector3D<float>(max.X, max.Y, min.Z), color);
        DrawLine(new Vector3D<float>(max.X, max.Y, min.Z), new Vector3D<float>(max.X, max.Y, max.Z), color);
        DrawLine(new Vector3D<float>(max.X, max.Y, max.Z), new Vector3D<float>(min.X, max.Y, max.Z), color);
        DrawLine(new Vector3D<float>(min.X, max.Y, max.Z), new Vector3D<float>(min.X, max.Y, min.Z), color);
        // Verticals
        DrawLine(new Vector3D<float>(min.X, min.Y, min.Z), new Vector3D<float>(min.X, max.Y, min.Z), color);
        DrawLine(new Vector3D<float>(max.X, min.Y, min.Z), new Vector3D<float>(max.X, max.Y, min.Z), color);
        DrawLine(new Vector3D<float>(max.X, min.Y, max.Z), new Vector3D<float>(max.X, max.Y, max.Z), color);
        DrawLine(new Vector3D<float>(min.X, min.Y, max.Z), new Vector3D<float>(min.X, max.Y, max.Z), color);
    }
    public void Render(VulkanCommandBuffer cmd, DescriptorSet descriptorSet) {
        if (_vertices.Count == 0) return;
        // Resize if needed
        int frameIndex = _renderer.CurrentFrameIndex;
        var buffer = _vertexBuffers[frameIndex];

        ulong requiredSize = (ulong) (_vertices.Count * Unsafe.SizeOf<DebugVertex>());
        if (buffer.Size < requiredSize) {
            CreateBuffers(_vertices.Count * 2); // Grow all
            buffer = _vertexBuffers[frameIndex];
        }
        // Upload
        var span = CollectionsMarshal.AsSpan(_vertices);
        fixed (DebugVertex* pData = span) {
            System.Buffer.MemoryCopy(pData, buffer.MappedData, requiredSize, requiredSize);
        }
        cmd.BindPipeline(_pipeline, PipelineBindPoint.Graphics);
        // We reuse the global UBO descriptor set for ViewProj
        cmd.BindDescriptorSets(_pipeline, descriptorSet);
        // Identity model matrix
        var pc = Matrix4X4<float>.Identity;
        cmd.PushConstants(_pipeline, ShaderStageFlags.VertexBit, pc);
        cmd.BindVertexBuffer(buffer);
        cmd.Draw((uint) _vertices.Count);
    }
    public void Dispose() {
        var p = _pipeline;
        _ctx.EnqueueDispose(() => {
            p?.Dispose();
            foreach (var vb in _vertexBuffers) vb?.Dispose();
        });
    }
}