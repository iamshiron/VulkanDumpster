using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using StbTrueTypeSharp;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using File = System.IO.File;

namespace Shiron.VulkanDumpster.Vulkan;

public unsafe class TextRenderer : IDisposable {
    private readonly VulkanContext _ctx;
    private readonly Renderer _renderer;
    
    private Texture _fontTexture;
    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorPool _descriptorPool;
    private DescriptorSet _descriptorSet;
    private VulkanPipeline _pipeline;
    private readonly VulkanBuffer[] _vertexBuffers;
    
    private readonly Dictionary<char, Character> _characters = new();
    private readonly List<TextVertex> _vertices = new();
    private const int InitialVertexCapacity = 10000;

    public struct Character {
        public Vector2D<float> TexCoordMin;
        public Vector2D<float> TexCoordMax;
        public Vector2D<float> Size;
        public Vector2D<float> Bearing;
        public float Advance;
    }

    private struct TextVertex {
        public Vector3D<float> Position;
        public Vector2D<float> TexCoord;
        public float TexIndex;
        
        public static VertexInputBindingDescription GetBindingDescription() {
            return new VertexInputBindingDescription {
                Binding = 0,
                Stride = (uint) Unsafe.SizeOf<TextVertex>(),
                InputRate = VertexInputRate.Vertex
            };
        }

        public static VertexInputAttributeDescription[] GetAttributeDescriptions() {
            return new[] {
                new VertexInputAttributeDescription {
                    Binding = 0,
                    Location = 0,
                    Format = Format.R32G32B32Sfloat,
                    Offset = (uint)Marshal.OffsetOf<TextVertex>(nameof(Position))
                },
                new VertexInputAttributeDescription {
                    Binding = 0,
                    Location = 1,
                    Format = Format.R32G32Sfloat,
                    Offset = (uint)Marshal.OffsetOf<TextVertex>(nameof(TexCoord))
                },
                new VertexInputAttributeDescription {
                    Binding = 0,
                    Location = 2,
                    Format = Format.R32Sfloat,
                    Offset = (uint)Marshal.OffsetOf<TextVertex>(nameof(TexIndex))
                }
            };
        }
    }

    private struct PushConsts {
        public Matrix4X4<float> Proj;
        public Vector4 Color;
    }

    public TextRenderer(VulkanContext ctx, Renderer renderer, string fontPath, float fontSize) {
        _ctx = ctx;
        _renderer = renderer;
        _vertexBuffers = new VulkanBuffer[3];
        
        InitFont(fontPath, fontSize);
        InitResources();
    }

    private void InitFont(string fontPath, float fontSize) {
        byte[] ttfData = File.ReadAllBytes(fontPath);
        
        int atlasWidth = 1024;
        int atlasHeight = 1024;
        byte[] bitmap = new byte[atlasWidth * atlasHeight]; // 1 channel
        var packedChars = new StbTrueType.stbtt_packedchar[96]; // ASCII 32..127
        
        fixed (byte* pBitmap = bitmap)
        fixed (byte* pTtf = ttfData)
        fixed (StbTrueType.stbtt_packedchar* pChars = packedChars) {
            var context = new StbTrueType.stbtt_pack_context();
            
            if (StbTrueType.stbtt_PackBegin(context, pBitmap, atlasWidth, atlasHeight, 0, 1, null) == 0)
                throw new Exception("Failed to init font packer");

            StbTrueType.stbtt_PackFontRange(context, pTtf, 0, fontSize, 32, 96, pChars);
            StbTrueType.stbtt_PackEnd(context);
        }

        // Convert 1-channel bitmap to 4-channel RGBA
        byte[] atlasData = new byte[atlasWidth * atlasHeight * 4];
        for (int i = 0; i < bitmap.Length; i++) {
            atlasData[i * 4 + 0] = 255;
            atlasData[i * 4 + 1] = 255;
            atlasData[i * 4 + 2] = 255;
            atlasData[i * 4 + 3] = bitmap[i];
        }

        // Process packed chars
        for (int i = 0; i < 96; i++) {
            char c = (char)(32 + i);
            var pc = packedChars[i];
            
            // Calculate UVs
            float u0 = pc.x0 / (float)atlasWidth;
            float v0 = pc.y0 / (float)atlasHeight;
            float u1 = pc.x1 / (float)atlasWidth;
            float v1 = pc.y1 / (float)atlasHeight;
            
            // Calculate Size/Bearing
            float w = pc.x1 - pc.x0;
            float h = pc.y1 - pc.y0;
            
            var charInfo = new Character {
                TexCoordMin = new Vector2D<float>(u0, v0),
                TexCoordMax = new Vector2D<float>(u1, v1),
                Size = new Vector2D<float>(w, h),
                Bearing = new Vector2D<float>(pc.xoff, pc.yoff),
                Advance = pc.xadvance
            };
            _characters.Add(c, charInfo);
        }

        // Upload texture
        _fontTexture = new Texture(_ctx, (uint)atlasWidth, (uint)atlasHeight, atlasData, Filter.Linear, Filter.Linear);
    }

    private void InitResources() {
        // 1. Descriptor Set Layout
        var binding = new DescriptorSetLayoutBinding {
            Binding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            StageFlags = ShaderStageFlags.FragmentBit
        };
        
        var layoutInfo = new DescriptorSetLayoutCreateInfo {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &binding
        };
        
        if (_ctx.Vk.CreateDescriptorSetLayout(_ctx.Device, &layoutInfo, null, out _descriptorSetLayout) != Result.Success)
            throw new Exception("Failed to create text descriptor set layout");

        // 2. Pipeline
        ShaderUtils.LoadShaderModule(_ctx.Vk, _ctx.Device, "shaders/text.vert.spv", out var vert);
        ShaderUtils.LoadShaderModule(_ctx.Vk, _ctx.Device, "shaders/text.frag.spv", out var frag);

        var pushConstantRange = new PushConstantRange {
            StageFlags = ShaderStageFlags.VertexBit, // For projection
            Offset = 0,
            Size = (uint) (sizeof(Matrix4X4<float>) + sizeof(Vector4)) // Proj + Color
        };
        
        fixed (DescriptorSetLayout* pLayout = &_descriptorSetLayout) {
            var pipelineLayoutInfo = new PipelineLayoutCreateInfo {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = pLayout,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushConstantRange
            };
            _ctx.Vk.CreatePipelineLayout(_ctx.Device, &pipelineLayoutInfo, null, out var pipelineLayout);

            var builder = new PipelineBuilder(_ctx.Vk) { PipelineLayout = pipelineLayout };
            builder.SetShaders(vert, frag)
                   .SetInputTopology(PrimitiveTopology.TriangleList)
                   .SetPolygonMode(PolygonMode.Fill)
                   .SetCullMode(CullModeFlags.None, FrontFace.CounterClockwise)
                   .SetMultisamplingNone()
                   .EnableBlendingAlphaBlend() 
                   .DisableDepthTest() 
                   .SetVertexInput(TextVertex.GetBindingDescription(), TextVertex.GetAttributeDescriptions())
                   .SetColorAttachmentFormat(_renderer.SwapchainImageFormat)
                   .SetDepthFormat(_renderer.DepthFormat);

            var pipeline = builder.Build(_ctx.Device);
            _pipeline = new VulkanPipeline(_ctx.Vk, _ctx.Device, pipeline, pipelineLayout, "Text");
        }
        
        _ctx.Vk.DestroyShaderModule(_ctx.Device, vert, null);
        _ctx.Vk.DestroyShaderModule(_ctx.Device, frag, null);

        // 3. Descriptor Pool & Set
        var poolSize = new DescriptorPoolSize {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1
        };
        var poolInfo = new DescriptorPoolCreateInfo {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
            MaxSets = 1
        };
        _ctx.Vk.CreateDescriptorPool(_ctx.Device, &poolInfo, null, out _descriptorPool);

        var allocInfo = new DescriptorSetAllocateInfo {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _descriptorPool,
            DescriptorSetCount = 1,
            PSetLayouts = (DescriptorSetLayout*)Unsafe.AsPointer(ref _descriptorSetLayout)
        };
        _ctx.Vk.AllocateDescriptorSets(_ctx.Device, &allocInfo, out _descriptorSet);

        // 4. Update Descriptor Set
        var imageInfo = new DescriptorImageInfo {
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
            ImageView = _fontTexture.Image.View,
            Sampler = _fontTexture.Sampler.Handle
        };
        var write = new WriteDescriptorSet {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = 0,
            DstArrayElement = 0,
            DescriptorType = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &imageInfo
        };
        _ctx.Vk.UpdateDescriptorSets(_ctx.Device, 1, &write, 0, null);

        // 5. Vertex Buffer
        CreateBuffers(InitialVertexCapacity);
    }
    
    private void CreateBuffers(int capacity) {
        for (int i = 0; i < _vertexBuffers.Length; i++) {
            var oldBuffer = _vertexBuffers[i];
            if (oldBuffer != null) {
                _ctx.EnqueueDispose(() => oldBuffer.Dispose());
            }
            _vertexBuffers[i] = new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
                (ulong) (capacity * Unsafe.SizeOf<TextVertex>()),
                BufferUsageFlags.VertexBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }
    }

    public void Begin() {
        _vertices.Clear();
    }

    public void DrawText(string text, float x, float y, float scale, Vector4 color) {
        foreach (char c in text) {
            if (!_characters.TryGetValue(c, out var ch))
                continue;

            float qX = x + ch.Bearing.X * scale;
            float qY = y + ch.Bearing.Y * scale;
            
            float w = ch.Size.X * scale;
            float h = ch.Size.Y * scale;

            // 0: TL
            _vertices.Add(new TextVertex { Position = new Vector3D<float>(qX, qY, 0), TexCoord = new Vector2D<float>(ch.TexCoordMin.X, ch.TexCoordMin.Y), TexIndex = 0 });
            // 1: BL
            _vertices.Add(new TextVertex { Position = new Vector3D<float>(qX, qY + h, 0), TexCoord = new Vector2D<float>(ch.TexCoordMin.X, ch.TexCoordMax.Y), TexIndex = 0 });
            // 2: BR
            _vertices.Add(new TextVertex { Position = new Vector3D<float>(qX + w, qY + h, 0), TexCoord = new Vector2D<float>(ch.TexCoordMax.X, ch.TexCoordMax.Y), TexIndex = 0 });
            
            // 2: BR
            _vertices.Add(new TextVertex { Position = new Vector3D<float>(qX + w, qY + h, 0), TexCoord = new Vector2D<float>(ch.TexCoordMax.X, ch.TexCoordMax.Y), TexIndex = 0 });
            // 3: TR
            _vertices.Add(new TextVertex { Position = new Vector3D<float>(qX + w, qY, 0), TexCoord = new Vector2D<float>(ch.TexCoordMax.X, ch.TexCoordMin.Y), TexIndex = 0 });
            // 0: TL
            _vertices.Add(new TextVertex { Position = new Vector3D<float>(qX, qY, 0), TexCoord = new Vector2D<float>(ch.TexCoordMin.X, ch.TexCoordMin.Y), TexIndex = 0 });

            x += ch.Advance * scale;
        }
    }

    public float GetTextWidth(string text, float scale) {
        float width = 0;
        foreach (char c in text) {
            if (_characters.TryGetValue(c, out var ch)) {
                width += ch.Advance * scale;
            }
        }
        return width;
    }

    public void Render(VulkanCommandBuffer cmd, Vector2D<int> screenSize, Vector4 color) {
        if (_vertices.Count == 0) return;

        int frameIndex = _renderer.CurrentFrameIndex;
        var buffer = _vertexBuffers[frameIndex];

        ulong requiredSize = (ulong) (_vertices.Count * Unsafe.SizeOf<TextVertex>());
        if (buffer.Size < requiredSize) {
            CreateBuffers(_vertices.Count * 2);
            buffer = _vertexBuffers[frameIndex];
        }

        var span = CollectionsMarshal.AsSpan(_vertices);
        fixed (TextVertex* pData = span) {
            System.Buffer.MemoryCopy(pData, buffer.MappedData, requiredSize, requiredSize);
        }

        cmd.BindPipeline(_pipeline);
        cmd.BindDescriptorSets(_pipeline, _descriptorSet);
        cmd.BindVertexBuffer(buffer);

        var ortho = Matrix4X4.CreateOrthographicOffCenter(0f, (float)screenSize.X, 0f, (float)screenSize.Y, -1f, 1f);
        
        var pc = new PushConsts { Proj = ortho, Color = color };
        
        cmd.PushConstants(_pipeline, ShaderStageFlags.VertexBit, pc);

        cmd.Draw((uint)_vertices.Count);
    }

    public void Dispose() {
        _pipeline?.Dispose();
        _fontTexture?.Dispose();
        foreach (var vb in _vertexBuffers) vb?.Dispose();
        if (_descriptorSetLayout.Handle != 0)
            _ctx.Vk.DestroyDescriptorSetLayout(_ctx.Device, _descriptorSetLayout, null);
        if (_descriptorPool.Handle != 0)
            _ctx.Vk.DestroyDescriptorPool(_ctx.Device, _descriptorPool, null);
    }
}