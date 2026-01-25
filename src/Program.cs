using System.Numerics;
using System.Runtime.CompilerServices;
using Shiron.VulkanDumpster.Voxels;
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

    private static Chunk _chunk = null!;
    private static VulkanBuffer[] _uniformBuffers = [];
    private static TextureArray _textureArray = null!;

    private static FPSCamera _camera = null!;
    private static readonly HashSet<Key> _pressedKeys = new();
    private static Vector2 _lastMousePos;
    private static bool _firstMouse = true;

    private static double _elapsedTime;

    // Global UBO: Shared by everything
    private struct GlobalUniforms {
        public Matrix4X4<float> ViewProj;
    }

    // Push Constants: Changes per draw call (perfect for voxel chunks)
    private struct PushConstants {
        public Matrix4X4<float> Model;
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
        options.Title = "Vulkan Dumpster - Voxel Chunk";
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

        CreateTextureArray();
        CreateChunk();
        CreateUniformBuffers();
        CreateTrianglePipeline();

        _camera = new FPSCamera(new Vector3D<float>(16, 32, 40));
        _camera.Pitch = -45.0f;

        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("  ✓ Voxel Chunk System Initialized!");
        Console.WriteLine("  • Texture Array with 3 layers (Grass, Dirt, Stone)");
        Console.WriteLine("  • Naive meshing of 32x32x32 chunk space");
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

    private void CreateTextureArray() {
        // Create 3 textures: Grass(Green), Dirt(Brown), Stone(Gray)
        byte[] grass = [0, 255, 0, 255, 30, 200, 30, 255, 0, 255, 0, 255, 30, 200, 30, 255];
        byte[] dirt = [139, 69, 19, 255, 100, 50, 10, 255, 139, 69, 19, 255, 100, 50, 10, 255];
        byte[] stone = [128, 128, 128, 255, 100, 100, 100, 255, 128, 128, 128, 255, 100, 100, 100, 255];

        _textureArray = new TextureArray(_context, 2, 2, new[] { grass, dirt, stone }, Filter.Nearest, Filter.Nearest);
    }

    private void CreateChunk() {
        _chunk = new Chunk(_context, new Vector3D<float>(0, 0, 0));

        // Generate terrain
        for (int x = 0; x < Chunk.Size; x++) {
            for (int z = 0; z < Chunk.Size; z++) {
                // Simple sine wave terrain
                int height = (int) (MathF.Sin(x * 0.2f) * 4 + MathF.Cos(z * 0.2f) * 4 + 8);
                height = Math.Clamp(height, 1, Chunk.Size - 1);

                for (int y = 0; y < height; y++) {
                    BlockType type = BlockType.Stone;
                    if (y == height - 1) type = BlockType.Grass;
                    else if (y > height - 4) type = BlockType.Dirt;

                    _chunk.SetBlock(x, y, z, type);
                }
            }
        }
    }

    private unsafe void CreateUniformBuffers() {
        _uniformBuffers = new VulkanBuffer[3];
        _descriptorSets = new DescriptorSet[3];
        for (int i = 0; i < 3; i++) {
            _uniformBuffers[i] = new VulkanBuffer(_context.Vk, _context.Device, _context.PhysicalDevice,
                (ulong) sizeof(GlobalUniforms),
                BufferUsageFlags.UniformBufferBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            _descriptorSets[i] = _descriptorManager.Allocate(_descriptorSetLayout);
            _descriptorManager.UpdateBuffer(_descriptorSets[i], 0, DescriptorType.UniformBuffer,
                _uniformBuffers[i].Handle, (ulong) sizeof(GlobalUniforms));

            _descriptorManager.UpdateImage(_descriptorSets[i], 1, DescriptorType.CombinedImageSampler,
                _textureArray.Image.View, _textureArray.Sampler.Handle);
        }
    }

    private unsafe void CreateTrianglePipeline() {
        ShaderUtils.LoadShaderModule(_context.Vk, _context.Device, "shaders/colored_triangle.vert.spv", out var vert);
        ShaderUtils.LoadShaderModule(_context.Vk, _context.Device, "shaders/colored_triangle.frag.spv", out var frag);

        var pushConstantRange = new PushConstantRange {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = (uint) sizeof(PushConstants)
        };

        var layoutInfo = new PipelineLayoutCreateInfo {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = (DescriptorSetLayout*) Unsafe.AsPointer(ref _descriptorSetLayout),
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstantRange
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
        _chunk.Update(); // Rebuild mesh if needed

        var cmd = _renderer.BeginFrame();
        if (cmd.Handle.Handle == 0) return;

        int frameIndex = _renderer.CurrentFrameIndex;
        UpdateUniformBuffer(frameIndex);

        cmd.BindPipeline(_trianglePipeline);
        cmd.BindDescriptorSets(_trianglePipeline, new[] { _descriptorSets[frameIndex] });

        // Push chunk model matrix
        var pc = new PushConstants {
            Model = Matrix4X4.CreateTranslation(_chunk.Position)
        };
        cmd.PushConstants(_trianglePipeline, ShaderStageFlags.VertexBit, pc);

        if (_chunk.Mesh.IndexCount > 0) {
            _chunk.Mesh.Bind(cmd);
            cmd.DrawIndexed((uint) _chunk.Mesh.IndexCount);
        }

        _renderer.EndFrame();
    }

    private unsafe void UpdateUniformBuffer(int index) {
        var extent = _renderer.SwapchainExtent;
        var view = _camera.GetViewMatrix();
        var proj = _camera.GetProjectionMatrix(extent.Width / (float) extent.Height);
        proj.M22 *= -1;

        var ubo = new GlobalUniforms {
            ViewProj = view * proj
        };
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

        _chunk.Mesh.Dispose(); // Note: Chunk should probably be disposable
        _textureArray?.Dispose();
        foreach (var ubo in _uniformBuffers) ubo.Dispose();

        _descriptorManager?.Dispose();
        _renderer?.Dispose();
        _context?.Dispose();
        _input?.Dispose();
        _window?.Dispose();
    }
}
