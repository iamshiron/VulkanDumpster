using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Core;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using Shiron.VulkanDumpster.Vulkan;

namespace Shiron.VulkanDumpster;

public class Program {
    private static IWindow _window = null!;
    
    private static VulkanContext _context = null!;
    private static Renderer _renderer = null!;
    private static DescriptorSetManager _descriptorManager = null!;
    
    private static VulkanPipeline _trianglePipeline = null!;
    private static DescriptorSetLayout _descriptorSetLayout;
    private static DescriptorSet[] _descriptorSets = [];
    
    private static Mesh _mainMesh = null!;
    private static VulkanBuffer[] _uniformBuffers = [];

    private static double _elapsedTime;
    
    private struct UniformBufferObject {
        public Matrix4X4<float> Model;
        public Matrix4X4<float> View;
        public Matrix4X4<float> Proj;
    }

    public static void Main(string[] args) {
        new Program().Run();
    }

    public void Run() {
        CreateWindow();
        InitializeVulkan();
        
        _window.Run();
        
        Cleanup();
    }

    private void CreateWindow() {
        var options = WindowOptions.DefaultVulkan;
        options.Title = "Vulkan Dumpster - 3D Cube Mesh";
        options.Size = new Vector2D<int>(1920, 1080);

        _window = Window.Create(options);
        _window.Initialize();
        
        _window.Load += OnLoad;
        _window.Render += Render;
    }

    private unsafe void InitializeVulkan() {
        _context = new VulkanContext(_window);
        _renderer = new Renderer(_context, _window);
        
        CreateDescriptorSetLayout();
        _descriptorManager = new DescriptorSetManager(_context.Vk, _context.Device, 3, 
            new DescriptorPoolSize(DescriptorType.UniformBuffer, 3));
        
        CreateMesh();
        CreateUniformBuffers();
        CreateTrianglePipeline();

        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("  ✓ 3D Cube initialized successfully!");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
    }

    private unsafe void CreateDescriptorSetLayout() {
        var uboLayoutBinding = new DescriptorSetLayoutBinding {
            Binding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.UniformBuffer,
            StageFlags = ShaderStageFlags.VertexBit
        };

        var layoutInfo = new DescriptorSetLayoutCreateInfo {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &uboLayoutBinding
        };

        _context.Vk.CreateDescriptorSetLayout(_context.Device, &layoutInfo, null, out _descriptorSetLayout);
    }

    private void CreateMesh() {
        _mainMesh = new Mesh(_context);

        // Cube vertices (Position, TexCoord) - 24 vertices for 6 faces
        var vertices = new[] {
            // Front face (z = 0.5)
            new Vertex(new Vector3D<float>(-0.5f, -0.5f,  0.5f), new Vector2D<float>(0, 0)),
            new Vertex(new Vector3D<float>( 0.5f, -0.5f,  0.5f), new Vector2D<float>(1, 0)),
            new Vertex(new Vector3D<float>( 0.5f,  0.5f,  0.5f), new Vector2D<float>(1, 1)),
            new Vertex(new Vector3D<float>(-0.5f,  0.5f,  0.5f), new Vector2D<float>(0, 1)),
            // Back face (z = -0.5)
            new Vertex(new Vector3D<float>(-0.5f, -0.5f, -0.5f), new Vector2D<float>(1, 0)),
            new Vertex(new Vector3D<float>(-0.5f,  0.5f, -0.5f), new Vector2D<float>(1, 1)),
            new Vertex(new Vector3D<float>( 0.5f,  0.5f, -0.5f), new Vector2D<float>(0, 1)),
            new Vertex(new Vector3D<float>( 0.5f, -0.5f, -0.5f), new Vector2D<float>(0, 0)),
            // Top face (y = 0.5)
            new Vertex(new Vector3D<float>(-0.5f,  0.5f, -0.5f), new Vector2D<float>(0, 0)),
            new Vertex(new Vector3D<float>(-0.5f,  0.5f,  0.5f), new Vector2D<float>(0, 1)),
            new Vertex(new Vector3D<float>( 0.5f,  0.5f,  0.5f), new Vector2D<float>(1, 1)),
            new Vertex(new Vector3D<float>( 0.5f,  0.5f, -0.5f), new Vector2D<float>(1, 0)),
            // Bottom face (y = -0.5)
            new Vertex(new Vector3D<float>(-0.5f, -0.5f, -0.5f), new Vector2D<float>(0, 0)),
            new Vertex(new Vector3D<float>( 0.5f, -0.5f, -0.5f), new Vector2D<float>(1, 0)),
            new Vertex(new Vector3D<float>( 0.5f, -0.5f,  0.5f), new Vector2D<float>(1, 1)),
            new Vertex(new Vector3D<float>(-0.5f, -0.5f,  0.5f), new Vector2D<float>(0, 1)),
            // Left face (x = -0.5)
            new Vertex(new Vector3D<float>(-0.5f, -0.5f, -0.5f), new Vector2D<float>(0, 0)),
            new Vertex(new Vector3D<float>(-0.5f, -0.5f,  0.5f), new Vector2D<float>(1, 0)),
            new Vertex(new Vector3D<float>(-0.5f,  0.5f,  0.5f), new Vector2D<float>(1, 1)),
            new Vertex(new Vector3D<float>(-0.5f,  0.5f, -0.5f), new Vector2D<float>(0, 1)),
            // Right face (x = 0.5)
            new Vertex(new Vector3D<float>( 0.5f, -0.5f, -0.5f), new Vector2D<float>(1, 0)),
            new Vertex(new Vector3D<float>( 0.5f,  0.5f, -0.5f), new Vector2D<float>(1, 1)),
            new Vertex(new Vector3D<float>( 0.5f,  0.5f,  0.5f), new Vector2D<float>(0, 1)),
            new Vertex(new Vector3D<float>( 0.5f, -0.5f,  0.5f), new Vector2D<float>(0, 0))
        };

        foreach (var v in vertices) _mainMesh.AddVertex(v);

        // Indices for 6 faces (12 triangles)
        for (ushort i = 0; i < 6; i++) {
            ushort offset = (ushort)(i * 4);
            _mainMesh.AddIndex((ushort)(offset + 0));
            _mainMesh.AddIndex((ushort)(offset + 1));
            _mainMesh.AddIndex((ushort)(offset + 2));
            _mainMesh.AddIndex((ushort)(offset + 2));
            _mainMesh.AddIndex((ushort)(offset + 3));
            _mainMesh.AddIndex((ushort)(offset + 0));
        }

        _mainMesh.Build();
    }

    private unsafe void CreateUniformBuffers() {
        _uniformBuffers = new VulkanBuffer[3];
        _descriptorSets = new DescriptorSet[3];
        for (int i = 0; i < 3; i++) {
            _uniformBuffers[i] = new VulkanBuffer(_context.Vk, _context.Device, _context.PhysicalDevice,
                (ulong)sizeof(UniformBufferObject),
                BufferUsageFlags.UniformBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
            
            _descriptorSets[i] = _descriptorManager.Allocate(_descriptorSetLayout);
            _descriptorManager.UpdateBuffer(_descriptorSets[i], 0, DescriptorType.UniformBuffer, 
                _uniformBuffers[i].Handle, (ulong)sizeof(UniformBufferObject));
        }
    }

    private unsafe void CreateTrianglePipeline() {
        ShaderUtils.LoadShaderModule(_context.Vk, _context.Device, "shaders/colored_triangle.vert.spv", out var vert);
        ShaderUtils.LoadShaderModule(_context.Vk, _context.Device, "shaders/colored_triangle.frag.spv", out var frag);

        var layoutInfo = new PipelineLayoutCreateInfo {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = (DescriptorSetLayout*)Unsafe.AsPointer(ref _descriptorSetLayout)
        };

        _context.Vk.CreatePipelineLayout(_context.Device, &layoutInfo, null, out var pipelineLayout);

        var builder = new PipelineBuilder(_context.Vk) { PipelineLayout = pipelineLayout };
        builder.SetShaders(vert, frag)
               .SetInputTopology(PrimitiveTopology.TriangleList)
               .SetPolygonMode(PolygonMode.Fill)
               .SetCullMode(CullModeFlags.BackBit, FrontFace.CounterClockwise)
               .SetMultisamplingNone()
               .DisableBlending()
               .EnableDepthTest(true, CompareOp.Less)
               .SetColorAttachmentFormat(_renderer.SwapchainImageFormat)
               .SetDepthFormat(_renderer.DepthFormat);

        var pipeline = builder.Build(_context.Device);
        _trianglePipeline = new VulkanPipeline(_context.Vk, _context.Device, pipeline, pipelineLayout);

        _context.Vk.DestroyShaderModule(_context.Device, vert, null);
        _context.Vk.DestroyShaderModule(_context.Device, frag, null);
    }

    private unsafe void Render(double delta) {
        _elapsedTime += delta;
        _mainMesh.UpdateGpuBuffers();

        var cmd = _renderer.BeginFrame();
        if (cmd.Handle.Handle == 0) return;

        int frameIndex = _renderer.CurrentFrameIndex;
        UpdateUniformBuffer(frameIndex);

        cmd.BindPipeline(_trianglePipeline);
        cmd.BindDescriptorSets(_trianglePipeline, new[] { _descriptorSets[frameIndex] });
        _mainMesh.Bind(cmd);
        cmd.DrawIndexed((uint)_mainMesh.IndexCount);

        _renderer.EndFrame();
    }

    private unsafe void UpdateUniformBuffer(int index) {
        var extent = _renderer.SwapchainExtent;
        var ubo = new UniformBufferObject {
            Model = Matrix4X4.CreateRotationY((float)_elapsedTime) * Matrix4X4.CreateRotationX((float)_elapsedTime * 0.5f),
            View = Matrix4X4.CreateLookAt<float>(new Vector3D<float>(2, 2, 2), Vector3D<float>.Zero, new Vector3D<float>(0, 0, 1)),
            Proj = Matrix4X4.CreatePerspectiveFieldOfView<float>(MathF.PI / 4, extent.Width / (float)extent.Height, 0.1f, 10.0f)
        };
        ubo.Proj.M22 *= -1;
        Unsafe.Copy(_uniformBuffers[index].MappedData, ref ubo);
    }

    private void OnLoad() {
        _window.CreateInput().Keyboards[0].KeyDown += (k, key, _) => { if (key == Key.Escape) _window.Close(); };
    }

    private unsafe void Cleanup() {
        _context.Vk.DeviceWaitIdle(_context.Device);

        _trianglePipeline?.Dispose();
        _context.Vk.DestroyDescriptorSetLayout(_context.Device, _descriptorSetLayout, null);
        
        _mainMesh?.Dispose();
        foreach (var ubo in _uniformBuffers) ubo.Dispose();
        
        _descriptorManager?.Dispose();
        _renderer?.Dispose();
        _context?.Dispose();
        _window?.Dispose();
    }
}