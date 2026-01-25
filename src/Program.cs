using System.Numerics;
using System.Runtime.CompilerServices;
using Shiron.VulkanDumpster.Vulkan;
using Silk.NET.Core;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;

namespace Shiron.VulkanDumpster;

public class Program {
    private static IWindow _window = null!;
    private static IInputContext _input = null!;

    private static VulkanContext _context = null!;
    private static Renderer _renderer = null!;
    private static DescriptorSetManager _descriptorManager = null!;

    private static VulkanPipeline _trianglePipeline = null!;
    private static DescriptorSetLayout _descriptorSetLayout;
    private static DescriptorSet[] _descriptorSets = [];

    private static Mesh _mainMesh = null!;
    private static VulkanBuffer[] _uniformBuffers = [];
    private static Texture _texture = null!;

    private static FPSCamera _camera = null!;
    private static readonly HashSet<Key> _pressedKeys = new();
    private static Vector2 _lastMousePos;
    private static bool _firstMouse = true;

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
        options.Title = "Vulkan Dumpster - Textures";
        options.Size = new Vector2D<int>(1920, 1080);

        _window = Window.Create(options);

        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += Render;
        _window.Initialize();
    }

    private unsafe void InitializeVulkan() {
        _context = new VulkanContext(_window);
        _renderer = new Renderer(_context, _window);

        CreateDescriptorSetLayout();
        _descriptorManager = new DescriptorSetManager(_context.Vk, _context.Device, 3,
            new DescriptorPoolSize(DescriptorType.UniformBuffer, 3),
            new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 3));

        CreateTexture();
        CreateMesh();
        CreateUniformBuffers();
        CreateTrianglePipeline();

        _camera = new FPSCamera(new Vector3D<float>(0, 0, 5));

        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("  ✓ Texture abstraction implemented successfully!");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
    }

    private unsafe void CreateDescriptorSetLayout() {
        var uboLayoutBinding = new DescriptorSetLayoutBinding {
            Binding = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.UniformBuffer,
            StageFlags = ShaderStageFlags.VertexBit
        };

        var samplerLayoutBinding = new DescriptorSetLayoutBinding {
            Binding = 1,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        var bindings = stackalloc[] { uboLayoutBinding, samplerLayoutBinding };
        var layoutInfo = new DescriptorSetLayoutCreateInfo {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 2,
            PBindings = bindings
        };

        _context.Vk.CreateDescriptorSetLayout(_context.Device, &layoutInfo, null, out _descriptorSetLayout);
    }

    private void CreateTexture() {
        // Create a 2x2 procedural checkerboard
        byte[] pixels = [
            255, 255, 255, 255,   0,   0,   0, 255,
              0,   0,   0, 255, 255, 255, 255, 255
        ];
        //_texture = new Texture(_context, 2, 2, pixels, Filter.Nearest, Filter.Nearest);
        _texture = new Texture(_context, "assets/pixiewall-p5kvnp-5120x2880.jpg", Filter.Linear, Filter.Linear);
    }

    private void CreateMesh() {
        _mainMesh = new Mesh(_context);
        var vertices = new[] {
            new Vertex(new Vector3D<float>(-0.5f, -0.5f,  0.5f), new Vector2D<float>(0, 0)),
            new Vertex(new Vector3D<float>( 0.5f, -0.5f,  0.5f), new Vector2D<float>(1, 0)),
            new Vertex(new Vector3D<float>( 0.5f,  0.5f,  0.5f), new Vector2D<float>(1, 1)),
            new Vertex(new Vector3D<float>(-0.5f,  0.5f,  0.5f), new Vector2D<float>(0, 1)),
            new Vertex(new Vector3D<float>(-0.5f, -0.5f, -0.5f), new Vector2D<float>(1, 0)),
            new Vertex(new Vector3D<float>(-0.5f,  0.5f, -0.5f), new Vector2D<float>(1, 1)),
            new Vertex(new Vector3D<float>( 0.5f,  0.5f, -0.5f), new Vector2D<float>(0, 1)),
            new Vertex(new Vector3D<float>( 0.5f, -0.5f, -0.5f), new Vector2D<float>(0, 0)),
            new Vertex(new Vector3D<float>(-0.5f,  0.5f, -0.5f), new Vector2D<float>(0, 0)),
            new Vertex(new Vector3D<float>(-0.5f,  0.5f,  0.5f), new Vector2D<float>(0, 1)),
            new Vertex(new Vector3D<float>( 0.5f,  0.5f,  0.5f), new Vector2D<float>(1, 1)),
            new Vertex(new Vector3D<float>( 0.5f,  0.5f, -0.5f), new Vector2D<float>(1, 0)),
            new Vertex(new Vector3D<float>(-0.5f, -0.5f, -0.5f), new Vector2D<float>(0, 0)),
            new Vertex(new Vector3D<float>( 0.5f, -0.5f, -0.5f), new Vector2D<float>(1, 0)),
            new Vertex(new Vector3D<float>( 0.5f, -0.5f,  0.5f), new Vector2D<float>(1, 1)),
            new Vertex(new Vector3D<float>(-0.5f, -0.5f,  0.5f), new Vector2D<float>(0, 1)),
            new Vertex(new Vector3D<float>(-0.5f, -0.5f, -0.5f), new Vector2D<float>(0, 0)),
            new Vertex(new Vector3D<float>(-0.5f, -0.5f,  0.5f), new Vector2D<float>(1, 0)),
            new Vertex(new Vector3D<float>(-0.5f,  0.5f,  0.5f), new Vector2D<float>(1, 1)),
            new Vertex(new Vector3D<float>(-0.5f,  0.5f, -0.5f), new Vector2D<float>(0, 1)),
            new Vertex(new Vector3D<float>( 0.5f, -0.5f, -0.5f), new Vector2D<float>(1, 0)),
            new Vertex(new Vector3D<float>( 0.5f,  0.5f, -0.5f), new Vector2D<float>(1, 1)),
            new Vertex(new Vector3D<float>( 0.5f,  0.5f,  0.5f), new Vector2D<float>(0, 1)),
            new Vertex(new Vector3D<float>( 0.5f, -0.5f,  0.5f), new Vector2D<float>(0, 0))
        };

        foreach (var v in vertices) _mainMesh.AddVertex(v);
        for (ushort i = 0; i < 6; i++) {
            ushort offset = (ushort) (i * 4);
            _mainMesh.AddIndex((ushort) (offset + 0));
            _mainMesh.AddIndex((ushort) (offset + 1));
            _mainMesh.AddIndex((ushort) (offset + 2));
            _mainMesh.AddIndex((ushort) (offset + 2));
            _mainMesh.AddIndex((ushort) (offset + 3));
            _mainMesh.AddIndex((ushort) (offset + 0));
        }
        _mainMesh.Build();
    }

    private unsafe void CreateUniformBuffers() {
        _uniformBuffers = new VulkanBuffer[3];
        _descriptorSets = new DescriptorSet[3];
        for (int i = 0; i < 3; i++) {
            _uniformBuffers[i] = new VulkanBuffer(_context.Vk, _context.Device, _context.PhysicalDevice,
                (ulong) sizeof(UniformBufferObject),
                BufferUsageFlags.UniformBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            _descriptorSets[i] = _descriptorManager.Allocate(_descriptorSetLayout);
            _descriptorManager.UpdateBuffer(_descriptorSets[i], 0, DescriptorType.UniformBuffer,
                _uniformBuffers[i].Handle, (ulong) sizeof(UniformBufferObject));

            // Update the same descriptor set with the texture sampler
            _descriptorManager.UpdateImage(_descriptorSets[i], 1, DescriptorType.CombinedImageSampler,
                _texture.Image.View, _texture.Sampler.Handle);
        }
    }

    private unsafe void CreateTrianglePipeline() {
        ShaderUtils.LoadShaderModule(_context.Vk, _context.Device, "shaders/colored_triangle.vert.spv", out var vert);
        ShaderUtils.LoadShaderModule(_context.Vk, _context.Device, "shaders/colored_triangle.frag.spv", out var frag);

        var layoutInfo = new PipelineLayoutCreateInfo {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = (DescriptorSetLayout*) Unsafe.AsPointer(ref _descriptorSetLayout)
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
        cmd.DrawIndexed((uint) _mainMesh.IndexCount);

        _renderer.EndFrame();
    }

    private unsafe void UpdateUniformBuffer(int index) {
        var extent = _renderer.SwapchainExtent;
        var ubo = new UniformBufferObject {
            Model = Matrix4X4<float>.Identity,
            View = _camera.GetViewMatrix(),
            Proj = _camera.GetProjectionMatrix(extent.Width / (float) extent.Height)
        };
        ubo.Proj.M22 *= -1;
        Unsafe.Copy(_uniformBuffers[index].MappedData, ref ubo);
    }

    private void OnLoad() {
        _input = _window.CreateInput();
        foreach (var kb in _input.Keyboards) {
            kb.KeyDown += (k, key, _) => { _pressedKeys.Add(key); if (key == Key.Escape) _window.Close(); };
            kb.KeyUp += (k, key, _) => { _pressedKeys.Remove(key); };
        }

        foreach (var mouse in _input.Mice) {
            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += (m, button) => {
                if (button == MouseButton.Right) mouse.Cursor.CursorMode = CursorMode.Raw;
            };
            mouse.MouseUp += (m, button) => {
                if (button == MouseButton.Right) mouse.Cursor.CursorMode = CursorMode.Normal;
            };
        }
    }

    private void OnUpdate(double deltaTime) {
        foreach (var key in _pressedKeys) {
            _camera.ProcessKeyboard(key, deltaTime);
        }
    }

    private void OnMouseMove(IMouse mouse, Vector2 position) {
        if (mouse.Cursor.CursorMode != CursorMode.Raw) {
            _firstMouse = true;
            return;
        }

        if (_firstMouse) {
            _lastMousePos = position;
            _firstMouse = false;
        }

        float xOffset = position.X - _lastMousePos.X;
        float yOffset = position.Y - _lastMousePos.Y;
        _lastMousePos = position;

        _camera.ProcessMouseMovement(xOffset, yOffset);
    }

    private unsafe void Cleanup() {
        _context.Vk.DeviceWaitIdle(_context.Device);

        _trianglePipeline?.Dispose();
        _context.Vk.DestroyDescriptorSetLayout(_context.Device, _descriptorSetLayout, null);

        _mainMesh?.Dispose();
        _texture?.Dispose();
        foreach (var ubo in _uniformBuffers) ubo.Dispose();

        _descriptorManager?.Dispose();
        _renderer?.Dispose();
        _context?.Dispose();
        _input?.Dispose();
        _window?.Dispose();
    }
}
