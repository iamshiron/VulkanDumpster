using System.Numerics;
using System.Runtime.CompilerServices;
using Shiron.VulkanDumpster.Voxels;
using Shiron.VulkanDumpster.Voxels.Generation;
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
    private static AppSettings _settings = new();
    private static DescriptorSetManager _descriptorManager = null!;
    private static VulkanPipeline _trianglePipeline = null!;
    private static VulkanPipeline _wireframePipeline = null!;
    private static DescriptorSetLayout _descriptorSetLayout;
    private static DescriptorSet[] _descriptorSets = [];
    private static World _world = null!;
    private static IWorldGenerator _worldGenerator = null!;
    private static VulkanBuffer[] _uniformBuffers = [];
    private static TextureArray _textureArray = null!;
    private static DebugRenderer _debugRenderer = null!;
    private static TextRenderer _textRenderer = null!;
    private static FPSCamera _camera = null!;
    private static readonly Frustum _frustum = new();
    private static readonly HashSet<Key> _pressedKeys = new();
    private static Vector2 _lastMousePos;
    private static bool _firstMouse = true;
    private static double _elapsedTime;
    private static int _frameCount;
    private static double _fpsTimer;
    private static bool _isWireframe = false;
    private static bool _showUI = true;
    private static bool _bKeyWasPressed = false;
    private static bool _pKeyWasPressed = false;
    private static bool _rKeyWasPressed = false;
    private static bool _hKeyWasPressed = false;
    private static bool _gKeyWasPressed = false;
    private static readonly RingBuffer<float> _fpsRollingAverage = new(1000);
    private static readonly RingBuffer<float> _deltaRollingAverage = new(1000);

    private static float _uiUpdateTimer = 0.11f;
    private static string _cachedFpsText = "";
    private static string _cachedFrameTimeText = "";
    private static string _cachedChunkCountText = "";
    private static string _cachedRenderedChunksText = "";
    private static string _cachedRegionsText = "";
    private static string _cachedUpdatesText = "";
    private static string _cachedPosText = "";
    private static string _cachedRenderDistanceText = "";
    private static string _cachedVsyncText = "";
    private static List<Profiler.ProfileResult> _cachedCpuResults = new();
    private static Dictionary<string, double> _cachedGpuResults = new();
    private static string _cachedVulkanDrawCalls = "";
    private static string _cachedVulkanPipelineBinds = "";
    private static string _cachedVulkanDescSetBinds = "";
    private static string _cachedVulkanVbBinds = "";
    private static string _cachedVulkanIbBinds = "";
    private static string _cachedVulkanPushConstants = "";
    private static List<(string name, int count)> _cachedShaderExecutions = new();
    private static string _cachedMemWorkingSet = "";
    private static string _cachedMemPrivate = "";
    private static string _cachedMemManaged = "";
    private static string _cachedMemGc = "";
    private static string _cachedAllocPerFrameText = "";
    private static long _lastTotalAllocatedBytes;
    private static long _accumulatedAllocatedBytes;
    private static int _uiFrameCount;

    // Global UBO: Shared by everything
    private struct GlobalUniforms {
        public Matrix4X4<float> ViewProj;
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
        options.Title = "Vulkan Dumpster - Infinite World";
        options.Size = new Vector2D<int>(1920, 1080);
        options.FramesPerSecond = _settings.MaxFPS;
        options.UpdatesPerSecond = _settings.MaxFPS;
        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += Render;
        _window.Initialize();
    }
    private unsafe void InitializeVulkan() {
        _context = new VulkanContext(_window);
        _renderer = new Renderer(_context, _window, _settings);
        CreateDescriptorSetLayout();
        _descriptorManager = new DescriptorSetManager(_context.Vk, _context.Device, 3,
            new DescriptorPoolSize(DescriptorType.UniformBuffer, 3),
            new DescriptorPoolSize(DescriptorType.CombinedImageSampler, 3));
        CreateTextureArray();
        _worldGenerator = new BiomeWorldGenerator();
        _worldGenerator.Initialize(1337);
        _world = new World(_context, _renderer, _settings, _worldGenerator);
        CreateUniformBuffers();
        CreatePipelines();
        _debugRenderer = new DebugRenderer(_context, _renderer, _descriptorSetLayout);

        string fontPath = "assets/font.ttf";
        if (!System.IO.File.Exists(fontPath)) {
            fontPath = @"C:\Windows\Fonts\arial.ttf";
        }
        _textRenderer = new TextRenderer(_context, _renderer, fontPath, 20);

        _camera = new FPSCamera(new Position(0, 100, 0)) {
            Pitch = -45.0f
        };
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("  ✓ World System Initialized!");
        Console.WriteLine($"  • {_settings.RenderDistance * 2}x{_settings.RenderDistance * 2} Chunk Grid ({_settings.RenderDistance * 2 * 32}x{_settings.RenderDistance * 2 * 32} blocks)");
        Console.WriteLine("  • Seamless terrain generation");
        Console.WriteLine("  • Press 'B' to toggle wireframe mode");
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
        byte[] grass = [0, 255, 0, 255, 30, 200, 30, 255, 0, 255, 0, 255, 30, 200, 30, 255];
        byte[] dirt = [139, 69, 19, 255, 100, 50, 10, 255, 139, 69, 19, 255, 100, 50, 10, 255];
        byte[] stone = [128, 128, 128, 255, 100, 100, 100, 255, 128, 128, 128, 255, 100, 100, 100, 255];
        byte[] sand = [240, 230, 140, 255, 255, 255, 224, 255, 240, 230, 140, 255, 255, 255, 224, 255];
        _textureArray = new TextureArray(_context, 2, 2, new[] { grass, dirt, stone, sand }, Filter.Nearest, Filter.Nearest);
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
    private unsafe void CreatePipelines() {
        ShaderUtils.LoadShaderModule(_context.Vk, _context.Device, "shaders/colored_triangle.vert.spv", out var vert);
        ShaderUtils.LoadShaderModule(_context.Vk, _context.Device, "shaders/colored_triangle.frag.spv", out var frag);
        var pushConstantRange = new PushConstantRange {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = (uint) Unsafe.SizeOf<Matrix4X4<float>>()
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

        // Solid pipeline
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
        _trianglePipeline = new VulkanPipeline(_context.Vk, _context.Device, pipeline, pipelineLayout, "WorldSolid");
        // Wireframe pipeline
        builder.SetPolygonMode(PolygonMode.Line);
        var wfPipeline = builder.Build(_context.Device);
        // We reuse the layout for the second pipeline object, but we shouldn't double-dispose it in VulkanPipeline.
        // VulkanPipeline takes ownership. We need to be careful.
        // Ideally we'd clone the layout or reference count it, but for now we'll just not dispose it in the second one or create a new one.
        // Creating a new layout is safer for simple wrapper.
        _context.Vk.CreatePipelineLayout(_context.Device, &layoutInfo, null, out var wfPipelineLayout);
        _wireframePipeline = new VulkanPipeline(_context.Vk, _context.Device, wfPipeline, wfPipelineLayout, "WorldWireframe");

        _context.Vk.DestroyShaderModule(_context.Device, vert, null);
        _context.Vk.DestroyShaderModule(_context.Device, frag, null);
    }
    private unsafe void Render(double delta) {
        Profiler.Begin("Total Frame");

        long currentAlloc = GC.GetTotalAllocatedBytes();
        if (_lastTotalAllocatedBytes != 0) {
            _accumulatedAllocatedBytes += (currentAlloc - _lastTotalAllocatedBytes);
        }
        _lastTotalAllocatedBytes = currentAlloc;
        _uiFrameCount++;

        _elapsedTime += delta;
        _frameCount++;
        _fpsTimer += delta;

        _deltaRollingAverage.Add((float) delta);
        float smoothedDelta = _deltaRollingAverage.Average;
        _fpsRollingAverage.Add(smoothedDelta > 0 ? 1.0f / smoothedDelta : 0);

        if (_fpsTimer >= 1.0) {
            _fpsTimer = 0;
            _frameCount = 0;
        }

        if (delta > 0.25) { // If frame takes more than 250ms, consider it 0 FPS for display
            _fpsRollingAverage.Add(0);
        }

        Profiler.Begin("World Update");
        _world.Update(_camera.Position);
        Profiler.End("World Update");

        // Debug: Draw borders for active chunks?
        // For now, let's just clear debug
        _debugRenderer.Begin();

        Profiler.Begin("Begin Frame");
        var cmd = _renderer.BeginFrame();
        Profiler.End("Begin Frame");

        if (cmd.Handle.Handle == 0) {
            Profiler.End("Total Frame");
            return;
        }

        _renderer.GPUProfiler.BeginSection(cmd.Handle, "Main Pass");

        int frameIndex = _renderer.CurrentFrameIndex;
        var viewProj = UpdateUniformBuffer(frameIndex);
        _frustum.Update(viewProj);

        // Draw World
        Profiler.Begin("World Render");
        var activePipeline = _isWireframe ? _wireframePipeline : _trianglePipeline;
        cmd.BindPipeline(activePipeline);
        cmd.BindDescriptorSets(activePipeline, _descriptorSets[frameIndex]);

        // Push identity matrix for baked positions
        cmd.PushConstants(activePipeline, ShaderStageFlags.VertexBit, Matrix4X4<float>.Identity);

        _world.Render(cmd, activePipeline, _descriptorSets[frameIndex], _frustum);
        Profiler.End("World Render");

        // Draw Debug
        _debugRenderer.Render(cmd, _descriptorSets[frameIndex]);

        _renderer.GPUProfiler.EndSection(cmd.Handle, "Main Pass");

        // Draw Text
        Profiler.Begin("Text Render");
        if (_showUI) {
            _textRenderer.Begin();

            _uiUpdateTimer += (float) delta;
            if (_uiUpdateTimer >= 0.1f) {
                _uiUpdateTimer = 0;
                _cachedFpsText = $"FPS: {_fpsRollingAverage.Average:F1} " +
                                 $"(Med: {_fpsRollingAverage.GetMedian():F1}, " +
                                 $"Min: {_fpsRollingAverage.GetMin():F1}, " +
                                 $"Max: {_fpsRollingAverage.GetMax():F1}, " +
                                 $"1%: {_fpsRollingAverage.GetPercentile(0.01f):F1}, " +
                                 $"0.1%: {_fpsRollingAverage.GetPercentile(0.001f):F1})";

                double avgMs = _deltaRollingAverage.Average * 1000.0;
                double jitterMs = _deltaRollingAverage.GetStandardDeviation() * 1000.0;
                double maxMs = _deltaRollingAverage.GetMax() * 1000.0;
                _cachedFrameTimeText = $"Frame Time: {avgMs:F2}ms (Jitter: {jitterMs:F2}ms, Max: {maxMs:F2}ms)";

                _cachedChunkCountText = $"Total Chunks: {_world.ChunkCount * YChunk.HeightInChunks}";
                _cachedRenderedChunksText = $"Rendered Chunks: {_world.RenderedChunksCount}";
                _cachedRegionsText = $"Regions (Rendered/Total): {_world.RenderedRegionsCount}/{_world.TotalRegionsCount}";
                _cachedUpdatesText = $"Updates: {_world.LastFrameUpdates}";
                _cachedPosText = _camera.Position.ToString("F1", null);
                _cachedRenderDistanceText = $"Render Distance: {_settings.RenderDistance}";
                _cachedVsyncText = $"VSync: {_settings.VSync}";

                _cachedCpuResults = Profiler.GetResults();
                _cachedGpuResults = new Dictionary<string, double>(_renderer.GPUProfiler.GetLatestResults());

                _cachedVulkanDrawCalls = $"Draw Calls: {VulkanCommandProfiler.DrawCalls}";
                _cachedVulkanPipelineBinds = $"Pipeline Binds: {VulkanCommandProfiler.PipelineBinds}";
                _cachedVulkanDescSetBinds = $"Desc Set Binds: {VulkanCommandProfiler.DescriptorSetBinds}";
                _cachedVulkanVbBinds = $"VB Binds: {VulkanCommandProfiler.VertexBufferBinds}";
                _cachedVulkanIbBinds = $"IB Binds: {VulkanCommandProfiler.IndexBufferBinds}";
                _cachedVulkanPushConstants = $"Push Constants: {VulkanCommandProfiler.PushConstantsCount}";

                _cachedShaderExecutions.Clear();
                VulkanCommandProfiler.ForEachShaderExecution((name, count) => _cachedShaderExecutions.Add((name, count)));

                var mem = MemoryProfiler.GetStats();
                _cachedMemWorkingSet = $"Working Set: {mem.WorkingSetMB:F1} MB";
                _cachedMemPrivate = $"Private: {mem.PrivateMemoryMB:F1} MB";
                _cachedMemManaged = $"Managed: {mem.ManagedMemoryMB:F1} MB";
                _cachedMemGc = $"GC G0/G1/G2: {mem.Gen0Collections}/{mem.Gen1Collections}/{mem.Gen2Collections}";

                float avgAlloc = _uiFrameCount > 0 ? _accumulatedAllocatedBytes / (float) _uiFrameCount : 0;
                _cachedAllocPerFrameText = $"Alloc/Frame: {avgAlloc / 1024.0:F2} KB";
                _accumulatedAllocatedBytes = 0;
                _uiFrameCount = 0;
            }

            _textRenderer.DrawText(_cachedFpsText, 10, 30, 1.0f, new Vector4(1, 1, 1, 1));

            // Color code based on stutter intensity
            double avgMs_ft = _deltaRollingAverage.Average * 1000.0;
            double jitterMs_ft = _deltaRollingAverage.GetStandardDeviation() * 1000.0;
            Vector4 ftColor = new Vector4(0.8f, 0.8f, 0.8f, 1);
            if (jitterMs_ft > avgMs_ft * 0.15) ftColor = new Vector4(1, 1, 0, 1); // Warning: > 15% variance
            if (jitterMs_ft > avgMs_ft * 0.50) ftColor = new Vector4(1, 0, 0, 1); // Critical: > 50% variance

            _textRenderer.DrawText(_cachedFrameTimeText, 10, 55, 0.9f, ftColor);

            _textRenderer.DrawText(_cachedChunkCountText, 10, 80, 1.0f, new Vector4(1, 1, 1, 1));
            _textRenderer.DrawText(_cachedRenderedChunksText, 10, 105, 1.0f, new Vector4(1, 1, 1, 1));
            _textRenderer.DrawText(_cachedRegionsText, 10, 130, 1.0f, new Vector4(0.4f, 1.0f, 0.4f, 1));
            _textRenderer.DrawText(_cachedUpdatesText, 10, 155, 1.0f, new Vector4(1, 1, 1, 1));
            _textRenderer.DrawText(_cachedPosText, 10, 180, 1.0f, new Vector4(1, 1, 1, 1));
            _textRenderer.DrawText(_cachedRenderDistanceText, 10, 205, 1.0f, new Vector4(1, 1, 1, 1));
                        _textRenderer.DrawText(_cachedVsyncText, 10, 230, 1.0f, new Vector4(1, 1, 1, 1));
            
                                    // Right HUD: World Gen Debug
                                    var genDebug = _world.Generator.GetDebugData(_camera.Position.Block.X, _camera.Position.Block.Z);
                                    int rightY = 30;
                                    float screenWidth = _renderer.SwapchainExtent.Width;
                                    foreach (var kvp in genDebug) {
                                        string text = $"{kvp.Key}: {kvp.Value}";
                                        float textWidth = _textRenderer.GetTextWidth(text, 0.9f);
                                        _textRenderer.DrawText(text, screenWidth - textWidth - 10, rightY, 0.9f, new Vector4(0.4f, 0.8f, 1.0f, 1.0f));
                                        rightY += 25;
                                    }                    
                        // Display Profiler Results
                        int yOffset = 260;
            _textRenderer.DrawText("--- CPU Profiler ---", 10, yOffset, 0.8f, new Vector4(1, 0.8f, 0, 1));
            yOffset += 20;
            foreach (var res in _cachedCpuResults) {
                _textRenderer.DrawText($"{res.Name}: {res.Average:F3} ms (Max: {res.Max:F2})", 20, yOffset, 0.8f, new Vector4(0.8f, 0.8f, 0.8f, 1));
                yOffset += 20;
            }

            yOffset += 10;
            _textRenderer.DrawText("--- GPU Profiler ---", 10, yOffset, 0.8f, new Vector4(0, 0.8f, 1, 1));
            yOffset += 20;
            foreach (var pair in _cachedGpuResults) {
                _textRenderer.DrawText($"{pair.Key}: {pair.Value:F3} ms", 20, yOffset, 0.8f, new Vector4(0.8f, 0.8f, 0.8f, 1));
                yOffset += 20;
            }

            yOffset += 10;
            _textRenderer.DrawText("--- Vulkan Stats ---", 10, yOffset, 0.8f, new Vector4(1, 0.2f, 0.2f, 1));
            yOffset += 20;
            _textRenderer.DrawText(_cachedVulkanDrawCalls, 20, yOffset, 0.8f, new Vector4(0.8f, 0.8f, 0.8f, 1));
            yOffset += 20;
            _textRenderer.DrawText(_cachedVulkanPipelineBinds, 20, yOffset, 0.8f, new Vector4(0.8f, 0.8f, 0.8f, 1));
            _textRenderer.DrawText(_cachedVulkanDescSetBinds, 200, yOffset, 0.8f, new Vector4(0.8f, 0.8f, 0.8f, 1));
            yOffset += 20;
            _textRenderer.DrawText(_cachedVulkanVbBinds, 20, yOffset, 0.8f, new Vector4(0.8f, 0.8f, 0.8f, 1));
            _textRenderer.DrawText(_cachedVulkanIbBinds, 200, yOffset, 0.8f, new Vector4(0.8f, 0.8f, 0.8f, 1));
            yOffset += 20;
            _textRenderer.DrawText(_cachedVulkanPushConstants, 20, yOffset, 0.8f, new Vector4(0.8f, 0.8f, 0.8f, 1));

            yOffset += 20;
            _textRenderer.DrawText("--- Shader Executions ---", 10, yOffset, 0.8f, new Vector4(0.5f, 1f, 0.5f, 1));
            yOffset += 20;
            foreach (var exec in _cachedShaderExecutions) {
                _textRenderer.DrawText($"{exec.name}: {exec.count}", 20, yOffset, 0.8f, new Vector4(0.8f, 0.8f, 0.8f, 1));
                yOffset += 20;
            }

            yOffset += 10;
            _textRenderer.DrawText("--- Memory & GC ---", 10, yOffset, 0.8f, new Vector4(0.2f, 0.6f, 1f, 1));
            yOffset += 20;
            _textRenderer.DrawText(_cachedMemWorkingSet, 20, yOffset, 0.8f, new Vector4(0.8f, 0.8f, 0.8f, 1));
            _textRenderer.DrawText(_cachedMemPrivate, 200, yOffset, 0.8f, new Vector4(0.8f, 0.8f, 0.8f, 1));
            yOffset += 20;
            _textRenderer.DrawText(_cachedMemManaged, 20, yOffset, 0.8f, new Vector4(0.8f, 0.8f, 0.8f, 1));
            _textRenderer.DrawText(_cachedMemGc, 200, yOffset, 0.8f, new Vector4(0.8f, 0.8f, 0.8f, 1));
            yOffset += 20;
            _textRenderer.DrawText(_cachedAllocPerFrameText, 20, yOffset, 0.8f, new Vector4(1, 1, 0, 1));

            _renderer.GPUProfiler.BeginSection(cmd.Handle, "UI Pass");
            _textRenderer.Render(cmd, new Vector2D<int>((int) _renderer.SwapchainExtent.Width, (int) _renderer.SwapchainExtent.Height), new Vector4(1, 1, 1, 1));
            _renderer.GPUProfiler.EndSection(cmd.Handle, "UI Pass");
        }
        Profiler.End("Text Render");

        Profiler.Begin("End Frame");
        _renderer.EndFrame();
        Profiler.End("End Frame");
        Profiler.End("Total Frame");
    }
    private unsafe Matrix4X4<float> UpdateUniformBuffer(int index) {
        var extent = _renderer.SwapchainExtent;
        var view = _camera.GetViewMatrix();
        var proj = _camera.GetProjectionMatrix(extent.Width / (float) extent.Height);
        proj.M22 *= -1;
        var viewProj = view * proj;
        var ubo = new GlobalUniforms {
            ViewProj = viewProj
        };
        Unsafe.Copy(_uniformBuffers[index].MappedData, ref ubo);
        return viewProj;
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

        bool isBPressed = _pressedKeys.Contains(Key.B);
        if (isBPressed && !_bKeyWasPressed) {
            _isWireframe = !_isWireframe;
        }
        _bKeyWasPressed = isBPressed;

        bool isPPressed = _pressedKeys.Contains(Key.P);
        if (isPPressed && !_pKeyWasPressed) {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"profile_{timestamp}.json";

            var metadata = new Profiler.ProfileSnapshot.FrameMetadata {
                DeltaTime = deltaTime,
                AverageFPS = _fpsRollingAverage.Average,
                MedianFPS = _fpsRollingAverage.GetMedian(),
                MinFPS = _fpsRollingAverage.GetMin(),
                MaxFPS = _fpsRollingAverage.GetMax(),
                Low1PercentFPS = _fpsRollingAverage.GetPercentile(0.01f),
                Low01PercentFPS = _fpsRollingAverage.GetPercentile(0.001f),
                CameraPosition = (Vector3) _camera.Position,
                TotalChunks = _world.ChunkCount * YChunk.HeightInChunks,
                RenderedChunks = _world.RenderedChunksCount,
                TotalRegions = _world.TotalRegionsCount,
                RenderedRegions = _world.RenderedRegionsCount,
                ChunkUpdates = _world.LastFrameUpdates,
                AllocPerFrameKB = _uiFrameCount > 0 ? (_accumulatedAllocatedBytes / (float) _uiFrameCount) / 1024.0f : 0
            };

            Profiler.SaveProfile(fileName, _renderer.GPUProfiler.GetLatestResults(), metadata);
        }
        _pKeyWasPressed = isPPressed;

        bool isRPressed = _pressedKeys.Contains(Key.R);
        if (isRPressed && !_rKeyWasPressed) {
            Profiler.ResetMax();
        }
        _rKeyWasPressed = isRPressed;

        bool isHPressed = _pressedKeys.Contains(Key.H);
        if (isHPressed && !_hKeyWasPressed) {
            _showUI = !_showUI;
        }
        _hKeyWasPressed = isHPressed;

        bool isGPressed = _pressedKeys.Contains(Key.G);
        if (isGPressed && !_gKeyWasPressed) {
            GC.Collect(2, GCCollectionMode.Forced, true);
            Console.WriteLine("[System] Manual GC Collection Triggered.");
        }
        _gKeyWasPressed = isGPressed;
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
        _fpsRollingAverage.Dispose();
        _deltaRollingAverage.Dispose();
        _textRenderer?.Dispose();
        _debugRenderer.Dispose();
        _trianglePipeline?.Dispose();
        _wireframePipeline?.Dispose();
        _context.Vk.DestroyDescriptorSetLayout(_context.Device, _descriptorSetLayout, null);
        _world.Dispose();
        _textureArray?.Dispose();
        foreach (var ubo in _uniformBuffers) ubo.Dispose();
        _descriptorManager?.Dispose();
        _renderer?.Dispose();
        _context?.Dispose();
        _input?.Dispose();
        _window?.Dispose();
    }
}
