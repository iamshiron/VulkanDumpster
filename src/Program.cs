using System.Numerics;
using Silk.NET.Core;
using Silk.NET.Core.Contexts;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

namespace Shiron.VulkanDumpster;

/// <summary>
/// Minimal Vulkan learning app that renders a triangle.
/// Each method maps to a single Vulkan concept to keep the flow readable.
/// </summary>
public class Program {
    public static Program Instance;

    private static IWindow _window = null!;

    // Vulkan handles (mapped from C++ types)
    private static Vk _vk = null!;                          // Vulkan API
    private static Instance _instance;                       // VkInstance - Vulkan library handle
    private static InstanceBuilder _instanceBuilder = null!; // Manages instance + debug messenger (VkDebugUtilsMessengerEXT)
    private static PhysicalDevice _chosenGPU;               // VkPhysicalDevice - GPU chosen as the default device
    private static Device _device;                           // VkDevice - Vulkan device for commands
    private static SurfaceKHR _surface;                      // VkSurfaceKHR - Vulkan window surface

    // Builders (for proper disposal)
    private static PhysicalDeviceSelector _physicalDeviceSelector = null!;
    private static LogicalDeviceBuilder _logicalDeviceBuilder = null!;
    private static SwapchainBuilder _swapchainBuilder = null!;

    // Surface extension
    private static KhrSurface _khrSurface = null!;

    // Swapchain extension
    private static KhrSwapchain _khrSwapchain = null!;

    // Queues
    private static Queue _graphicsQueue;
    private static Queue _presentQueue;
    private static uint _graphicsQueueFamily;

    // Frame data for triple buffering (must match swapchain image count)
    private const int _frameOverlap = 3;
    private static FrameData[] _frames = new FrameData[_frameOverlap];
    private static int _frameNumber;
    private static double _elapsedTime;

    // Swapchain
    private static SwapchainKHR _swapchain;
    private static Image[] _swapchainImages = [];
    private static ImageView[] _swapchainImageViews = [];
    private static Format _swapchainImageFormat;
    private static Extent2D _swapchainExtent;

    // Triangle pipeline
    private static PipelineLayout _trianglePipelineLayout;
    private static Pipeline _trianglePipeline;

    private Silk.NET.Vulkan.Buffer _vertexBuffer;
    private DeviceMemory _vertexBufferMemory;
    private Silk.NET.Vulkan.Buffer _indexBuffer;
    private DeviceMemory _indexBufferMemory;

    private static ushort[] _indices = {
            0, 1, 2, 2, 3, 0
    };

    public static unsafe void Main(string[] args) {
        Instance = new();
        Instance.Run();
    }

    public void Run() {
        CreateWindow();
        InitializeVulkan();
        HookWindowEvents();

        // Create render data
        CreateVertexBuffer();
        CreateIndexBuffer();

        _window.Run();
        Cleanup();
    }

    /// <summary>
    /// Create the window and its Vulkan-compatible surface provider.
    /// </summary>
    private static void CreateWindow() {
        var options = WindowOptions.DefaultVulkan;
        options.Title = "Vulkan Dumpster Project";
        options.Size = new Vector2D<int>(1920, 1080);

        _window = Window.Create(options);
        _window.Initialize();
    }

    /// <summary>
    /// Build the Vulkan instance, device, swapchain, and render pipeline.
    /// This is the high-level "boot sequence" of the renderer.
    /// </summary>
    private static void InitializeVulkan() {
        _vk = Vk.GetApi();

        CreateInstance();
        CreateSurface();
        SelectPhysicalDevice();
        CreateLogicalDevice();
        CreateSwapchain();
        InitCommands();
        InitSyncStructures();
        InitPipelines();

        PrintCommandsInfo();
        PrintPipelineInfo();

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("  ✓ Vulkan initialized successfully!");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();
    }

    /// <summary>
    /// Create the Vulkan instance and (optionally) a debug messenger.
    /// </summary>
    private static void CreateInstance() {
        _instanceBuilder = new InstanceBuilder(_vk)
            .WithApp("VulkanDumpster", new Version32(1, 0, 0))
            .WithEngine("NoEngine", new Version32(1, 0, 0))
            .WithApiVersion(Vk.Version13)
            .AddExtensions(GetRequiredExtensions())
            .EnableValidationLayers(enable: true);
        _instance = _instanceBuilder.Build();
        PrintInstanceInfo();
    }

    /// <summary>
    /// Create the window surface and cache the surface extension for presentation checks.
    /// </summary>
    private static unsafe void CreateSurface() {
        _surface = _window.VkSurface!.Create<AllocationCallbacks>(_instance.ToHandle(), null).ToSurface();

        if (!_vk.TryGetInstanceExtension(_instance, out _khrSurface))
            throw new Exception("Failed to get KHR_surface extension.");
    }

    /// <summary>
    /// Choose a physical device (GPU) that supports the required queues and extensions.
    /// </summary>
    private static void SelectPhysicalDevice() {
        _physicalDeviceSelector = new PhysicalDeviceSelector(_vk, _instance)
            .PreferDeviceType(PhysicalDeviceType.DiscreteGpu)
            .AddRequiredExtensions(KhrSwapchain.ExtensionName)
            .RequireGraphicsQueue()
            .RequirePresentQueue(_surface, CheckPresentSupport);
        _chosenGPU = _physicalDeviceSelector.Select();
        PrintPhysicalDeviceInfo();
    }

    /// <summary>
    /// Create a logical device and retrieve the graphics/present queues we will submit to.
    /// </summary>
    private static void CreateLogicalDevice() {
        _logicalDeviceBuilder = new LogicalDeviceBuilder(_vk, _physicalDeviceSelector)
            .AddExtensions(KhrSwapchain.ExtensionName)
            .AddGraphicsQueue()
            .AddPresentQueue();
        _device = _logicalDeviceBuilder.Build();

        _graphicsQueue = _logicalDeviceBuilder.GetGraphicsQueue();
        _presentQueue = _logicalDeviceBuilder.GetPresentQueue();
        _graphicsQueueFamily = _physicalDeviceSelector.QueueFamilies.GraphicsFamily!.Value;

        if (!_vk.TryGetDeviceExtension(_instance, _device, out _khrSwapchain))
            throw new Exception("Failed to get KHR_swapchain extension.");
        PrintLogicalDeviceInfo();
    }

    /// <summary>
    /// Create a swapchain and cache its images/views so the render loop can access them quickly.
    /// </summary>
    private static void CreateSwapchain() {
        _swapchainBuilder = new SwapchainBuilder(
                _vk, _device, _chosenGPU, _surface, _khrSurface,
                _physicalDeviceSelector.QueueFamilies)
            .WithExtent(_window.Size)
            .WithFormat(Format.B8G8R8A8Srgb)
            .WithPresentMode(PresentModeKHR.MailboxKhr)
            .WithImageCount(3);
        _swapchain = _swapchainBuilder.Build();

        _swapchainImages = _swapchainBuilder.Images;
        _swapchainImageViews = _swapchainBuilder.ImageViews;
        _swapchainImageFormat = _swapchainBuilder.ImageFormat;
        _swapchainExtent = _swapchainBuilder.Extent;
        PrintSwapchainInfo();
    }

    /// <summary>
    /// Hook window callbacks for input and per-frame rendering.
    /// </summary>
    private void HookWindowEvents() {
        _window.Load += OnLoad;
        _window.Render += Render;
        _window.Closing += OnClosing;
    }

    /// <summary>
    /// Query the windowing system for the required instance extensions.
    /// </summary>
    private static unsafe string[] GetRequiredExtensions() {
        // These include platform-specific WSI (window system integration) extensions.
        var windowExtensions = _window.VkSurface!.GetRequiredExtensions(out var count);
        var extensions = new string[count];
        for (var i = 0; i < count; i++) {
            extensions[i] = System.Runtime.InteropServices.Marshal.PtrToStringAnsi((nint) windowExtensions[i]) ?? "";
        }
        return extensions;
    }

    /// <summary>
    /// Check whether a queue family can present to the current window surface.
    /// </summary>
    private static unsafe bool CheckPresentSupport(PhysicalDevice device, uint queueFamilyIndex) {
        _khrSurface.GetPhysicalDeviceSurfaceSupport(device, queueFamilyIndex, _surface, out var supported);
        return supported;
    }

    /// <summary>
    /// Get the frame data for the current frame (rotates through our frame overlap count).
    /// </summary>
    private static ref FrameData GetCurrentFrame() => ref _frames[_frameNumber % _frameOverlap];

    /// <summary>
    /// Initialize command pools and command buffers for each frame.
    /// </summary>
    private static unsafe void InitCommands() {
        // Create a command pool for commands submitted to the graphics queue.
        // We want the pool to allow resetting of individual command buffers.
        var commandPoolInfo = new CommandPoolCreateInfo {
            SType = StructureType.CommandPoolCreateInfo,
            PNext = null,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = _graphicsQueueFamily
        };

        for (int i = 0; i < _frameOverlap; i++) {
            // Create command pool for this frame
            fixed (CommandPool* pCommandPool = &_frames[i].CommandPool) {
                if (_vk.CreateCommandPool(_device, &commandPoolInfo, null, pCommandPool) != Result.Success) {
                    throw new Exception($"Failed to create command pool for frame {i}.");
                }
            }

            // Allocate the main command buffer for rendering
            var cmdAllocInfo = new CommandBufferAllocateInfo {
                SType = StructureType.CommandBufferAllocateInfo,
                PNext = null,
                CommandPool = _frames[i].CommandPool,
                CommandBufferCount = 1,
                Level = CommandBufferLevel.Primary
            };

            fixed (CommandBuffer* pCommandBuffer = &_frames[i].MainCommandBuffer) {
                if (_vk.AllocateCommandBuffers(_device, &cmdAllocInfo, pCommandBuffer) != Result.Success) {
                    throw new Exception($"Failed to allocate command buffer for frame {i}.");
                }
            }
        }
    }

    /// <summary>
    /// Initialize synchronization structures (fences and semaphores) for each frame.
    /// </summary>
    private static unsafe void InitSyncStructures() {
        // Create fence with SIGNALED bit so we can wait on it on the first frame
        var fenceCreateInfo = new FenceCreateInfo {
            SType = StructureType.FenceCreateInfo,
            PNext = null,
            Flags = FenceCreateFlags.SignaledBit
        };

        var semaphoreCreateInfo = new SemaphoreCreateInfo {
            SType = StructureType.SemaphoreCreateInfo,
            PNext = null,
            Flags = 0
        };

        for (int i = 0; i < _frameOverlap; i++) {
            fixed (Fence* pFence = &_frames[i].RenderFence) {
                if (_vk.CreateFence(_device, &fenceCreateInfo, null, pFence) != Result.Success) {
                    throw new Exception($"Failed to create render fence for frame {i}.");
                }
            }

            fixed (Silk.NET.Vulkan.Semaphore* pSemaphore = &_frames[i].SwapchainSemaphore) {
                if (_vk.CreateSemaphore(_device, &semaphoreCreateInfo, null, pSemaphore) != Result.Success) {
                    throw new Exception($"Failed to create swapchain semaphore for frame {i}.");
                }
            }

            fixed (Silk.NET.Vulkan.Semaphore* pSemaphore = &_frames[i].RenderSemaphore) {
                if (_vk.CreateSemaphore(_device, &semaphoreCreateInfo, null, pSemaphore) != Result.Success) {
                    throw new Exception($"Failed to create render semaphore for frame {i}.");
                }
            }
        }
    }

    /// <summary>
    /// Initialize all graphics pipelines.
    /// </summary>
    private static void InitPipelines() {
        InitTrianglePipeline();
    }

    /// <summary>
    /// Initialize the triangle rendering pipeline.
    /// </summary>
    private static unsafe void InitTrianglePipeline() {
        // Load shaders
        if (!ShaderUtils.LoadShaderModule(_vk, _device, "shaders/colored_triangle.vert.spv", out var triangleVertexShader)) {
            throw new Exception("Failed to load triangle vertex shader.");
        }

        if (!ShaderUtils.LoadShaderModule(_vk, _device, "shaders/colored_triangle.frag.spv", out var triangleFragShader)) {
            throw new Exception("Failed to load triangle fragment shader.");
        }

        // Build the pipeline layout (empty for now - no push constants or descriptor sets)
        var pipelineLayoutInfo = new PipelineLayoutCreateInfo {
            SType = StructureType.PipelineLayoutCreateInfo,
            PNext = null,
            Flags = 0,
            SetLayoutCount = 0,
            PSetLayouts = null,
            PushConstantRangeCount = 0,
            PPushConstantRanges = null
        };

        fixed (PipelineLayout* pPipelineLayout = &_trianglePipelineLayout) {
            if (_vk.CreatePipelineLayout(_device, &pipelineLayoutInfo, null, pPipelineLayout) != Result.Success) {
                throw new Exception("Failed to create triangle pipeline layout.");
            }
        }

        // Build the pipeline using the PipelineBuilder
        var pipelineBuilder = new PipelineBuilder(_vk) { PipelineLayout = _trianglePipelineLayout };
        pipelineBuilder
            .SetShaders(triangleVertexShader, triangleFragShader)
            .SetInputTopology(PrimitiveTopology.TriangleList)
            .SetPolygonMode(PolygonMode.Fill)
            .SetCullMode(CullModeFlags.None, FrontFace.Clockwise)
            .SetMultisamplingNone()
            .DisableBlending()
            .DisableDepthTest()
            .SetColorAttachmentFormat(_swapchainImageFormat)
            .SetDepthFormat(Format.Undefined);

        _trianglePipeline = pipelineBuilder.Build(_device);

        if (_trianglePipeline.Handle == 0) {
            throw new Exception("Failed to create triangle pipeline.");
        }

        // Clean up shader modules (they're baked into the pipeline now)
        _vk.DestroyShaderModule(_device, triangleFragShader, null);
        _vk.DestroyShaderModule(_device, triangleVertexShader, null);
    }

    /// <summary>
    /// Record geometry drawing commands.
    /// </summary>
    private unsafe void DrawGeometry(CommandBuffer cmd, ImageView targetImageView, Extent2D extent) {
        // Begin a render pass connected to our target image
        var colorAttachment = new RenderingAttachmentInfo {
            SType = StructureType.RenderingAttachmentInfo,
            PNext = null,
            ImageView = targetImageView,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Load,     // Preserve background
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = default
        };

        var renderInfo = new RenderingInfo {
            SType = StructureType.RenderingInfo,
            PNext = null,
            RenderArea = new Rect2D {
                Offset = new Offset2D { X = 0, Y = 0 },
                Extent = extent
            },
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachment,
            PDepthAttachment = null,
            PStencilAttachment = null
        };

        _vk.CmdBeginRendering(cmd, &renderInfo);

        // Bind the triangle pipeline
        _vk.CmdBindPipeline(cmd, PipelineBindPoint.Graphics, _trianglePipeline);
        Silk.NET.Vulkan.Buffer[] vertexBuffers = { _vertexBuffer };
        ulong[] offsets = [0];

        fixed (Silk.NET.Vulkan.Buffer* vertexBuffersPtr = vertexBuffers)
        fixed (ulong* offsetPtr = offsets) {
            _vk.CmdBindVertexBuffers(cmd, 0, 1, vertexBuffersPtr, offsetPtr);
        }

        _vk.CmdBindIndexBuffer(cmd, _indexBuffer, 0, IndexType.Uint16);

        // Set dynamic viewport
        var viewport = new Viewport {
            X = 0,
            Y = 0,
            Width = extent.Width,
            Height = extent.Height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f
        };
        _vk.CmdSetViewport(cmd, 0, 1, &viewport);

        // Set dynamic scissor
        var scissor = new Rect2D {
            Offset = new Offset2D { X = 0, Y = 0 },
            Extent = extent
        };
        _vk.CmdSetScissor(cmd, 0, 1, &scissor);

        // Draw 3 vertices (1 triangle) with hardcoded positions in the shader
        _vk.CmdDrawIndexed(cmd, (uint) _indices.Length, 1, 0, 0, 0);

        _vk.CmdEndRendering(cmd);
    }

    private static void OnLoad() {
        Console.WriteLine("Window loaded");
        var inputContext = _window.CreateInput();

        foreach (var kb in inputContext.Keyboards) {
            kb.KeyDown += KeyDown;
        }
    }

    private static unsafe void Cleanup() {
        try {
            // Wait for device to finish all operations before cleanup
            if (_device.Handle != 0) {
                _vk.DeviceWaitIdle(_device);
            }

            // Destroy per-frame resources
            for (int i = 0; i < _frameOverlap; i++) {
                // Destroy command pool (this also frees all command buffers allocated from it)
                if (_frames[i].CommandPool.Handle != 0) {
                    _vk.DestroyCommandPool(_device, _frames[i].CommandPool, null);
                    _frames[i].CommandPool = default;
                    _frames[i].MainCommandBuffer = default;
                }

                // Destroy sync objects
                if (_frames[i].RenderFence.Handle != 0) {
                    _vk.DestroyFence(_device, _frames[i].RenderFence, null);
                    _frames[i].RenderFence = default;
                }

                if (_frames[i].RenderSemaphore.Handle != 0) {
                    _vk.DestroySemaphore(_device, _frames[i].RenderSemaphore, null);
                    _frames[i].RenderSemaphore = default;
                }

                if (_frames[i].SwapchainSemaphore.Handle != 0) {
                    _vk.DestroySemaphore(_device, _frames[i].SwapchainSemaphore, null);
                    _frames[i].SwapchainSemaphore = default;
                }
            }

            // Destroy pipelines
            if (_trianglePipeline.Handle != 0) {
                _vk.DestroyPipeline(_device, _trianglePipeline, null);
                _trianglePipeline = default;
            }

            if (_trianglePipelineLayout.Handle != 0) {
                _vk.DestroyPipelineLayout(_device, _trianglePipelineLayout, null);
                _trianglePipelineLayout = default;
            }

            // Destroy resources in reverse order of creation
            _swapchainBuilder?.Dispose();
            _swapchain = default;
            _swapchainImages = [];
            _swapchainImageViews = [];

            _logicalDeviceBuilder?.Dispose();
            _device = default;
            _graphicsQueue = default;
            _presentQueue = default;

            if (_surface.Handle != 0 && _khrSurface != null) {
                _khrSurface.DestroySurface(_instance, _surface, null);
                _surface = default;
            }

            _instanceBuilder?.Dispose();
            _instance = default;

            _vk?.Dispose();
            _vk = null!;

            _window?.Dispose();
            _window = null!;

            Console.WriteLine("All Vulkan resources cleaned up successfully.");
        } catch (Exception ex) {
            Console.Error.WriteLine($"Error during cleanup: {ex.Message}");
        }
    }

    private static void KeyDown(IKeyboard keyboard, Key key, int arg3) {
        if (key == Key.Escape) {
            _window.Close();
        }
    }

    private static void OnClosing() {
        // Window is closing - cleanup will happen after Run() returns
    }

    /// <summary>
    /// Per-frame callback from the windowing system.
    /// </summary>
    private void Render(double delta) {
        _elapsedTime += delta;
        Draw();
    }

    /// <summary>
    /// Main draw function - records and submits rendering commands.
    /// </summary>
    private unsafe void Draw() {
        // 1) Synchronize with the GPU so we can reuse per-frame resources.
        var fence = GetCurrentFrame().RenderFence;
        if (_vk.WaitForFences(_device, 1, &fence, true, 1_000_000_000) != Result.Success) {
            throw new Exception("Failed to wait for render fence.");
        }

        // Reset the fence for the next frame
        if (_vk.ResetFences(_device, 1, &fence) != Result.Success) {
            throw new Exception("Failed to reset render fence.");
        }

        // 2) Acquire a swapchain image to render into.
        uint swapchainImageIndex = 0;
        var result = _khrSwapchain.AcquireNextImage(
            _device, _swapchain, 1_000_000_000,
            GetCurrentFrame().SwapchainSemaphore, default, &swapchainImageIndex);

        if (result != Result.Success && result != Result.SuboptimalKhr) {
            throw new Exception($"Failed to acquire swapchain image: {result}");
        }

        // 3) Record command buffer for this frame.
        var cmd = GetCurrentFrame().MainCommandBuffer;
        if (_vk.ResetCommandBuffer(cmd, 0) != Result.Success) {
            throw new Exception("Failed to reset command buffer.");
        }

        // Begin recording command buffer (one-time submit for potential optimization)
        var cmdBeginInfo = new CommandBufferBeginInfo {
            SType = StructureType.CommandBufferBeginInfo,
            PNext = null,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
            PInheritanceInfo = null
        };

        if (_vk.BeginCommandBuffer(cmd, &cmdBeginInfo) != Result.Success) {
            throw new Exception("Failed to begin command buffer.");
        }

        // Transition swapchain image to writable mode (General layout) for compute-style background clear
        ImageUtils.TransitionImage(_vk, cmd, _swapchainImages[swapchainImageIndex],
            ImageLayout.Undefined, ImageLayout.General);

        // Make a clear color from elapsed time - smooth 2-second cycle regardless of frame rate
        float flash = MathF.Abs(MathF.Sin((float) _elapsedTime * MathF.PI)); // Full cycle every 2 seconds
        var clearValue = new ClearColorValue(0.0f, 0.0f, flash, 1.0f);

        var clearRange = ImageUtils.ImageSubresourceRange(ImageAspectFlags.ColorBit);

        // Clear the image (background)
        _vk.CmdClearColorImage(cmd, _swapchainImages[swapchainImageIndex],
            ImageLayout.General, &clearValue, 1, &clearRange);

        // Transition to ColorAttachmentOptimal for geometry rendering
        ImageUtils.TransitionImage(_vk, cmd, _swapchainImages[swapchainImageIndex],
            ImageLayout.General, ImageLayout.ColorAttachmentOptimal);

        // Draw geometry (triangle)
        DrawGeometry(cmd, _swapchainImageViews[swapchainImageIndex], _swapchainExtent);

        // Transition swapchain image to presentable mode
        ImageUtils.TransitionImage(_vk, cmd, _swapchainImages[swapchainImageIndex],
            ImageLayout.ColorAttachmentOptimal, ImageLayout.PresentSrcKhr);

        // Finalize the command buffer
        if (_vk.EndCommandBuffer(cmd) != Result.Success) {
            throw new Exception("Failed to end command buffer.");
        }

        // Prepare submission to the queue
        var cmdInfo = new CommandBufferSubmitInfo {
            SType = StructureType.CommandBufferSubmitInfo,
            PNext = null,
            CommandBuffer = cmd,
            DeviceMask = 0
        };

        var waitInfo = new SemaphoreSubmitInfo {
            SType = StructureType.SemaphoreSubmitInfo,
            PNext = null,
            Semaphore = GetCurrentFrame().SwapchainSemaphore,
            StageMask = PipelineStageFlags2.ColorAttachmentOutputBit,
            DeviceIndex = 0,
            Value = 1
        };

        var signalInfo = new SemaphoreSubmitInfo {
            SType = StructureType.SemaphoreSubmitInfo,
            PNext = null,
            Semaphore = GetCurrentFrame().RenderSemaphore,
            StageMask = PipelineStageFlags2.AllGraphicsBit,
            DeviceIndex = 0,
            Value = 1
        };

        var submitInfo = new SubmitInfo2 {
            SType = StructureType.SubmitInfo2,
            PNext = null,
            WaitSemaphoreInfoCount = 1,
            PWaitSemaphoreInfos = &waitInfo,
            SignalSemaphoreInfoCount = 1,
            PSignalSemaphoreInfos = &signalInfo,
            CommandBufferInfoCount = 1,
            PCommandBufferInfos = &cmdInfo
        };

        // 4) Submit to the graphics queue (GPU executes recorded commands).
        if (_vk.QueueSubmit2(_graphicsQueue, 1, &submitInfo, GetCurrentFrame().RenderFence) != Result.Success) {
            throw new Exception("Failed to submit command buffer.");
        }

        // 5) Present the rendered image to the screen.
        var swapchain = _swapchain;
        var renderSemaphore = GetCurrentFrame().RenderSemaphore;
        var presentInfo = new PresentInfoKHR {
            SType = StructureType.PresentInfoKhr,
            PNext = null,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &renderSemaphore,
            PImageIndices = &swapchainImageIndex
        };

        result = _khrSwapchain.QueuePresent(_graphicsQueue, &presentInfo);
        if (result != Result.Success && result != Result.SuboptimalKhr) {
            throw new Exception($"Failed to present swapchain image: {result}");
        }

        // Increase frame counter
        _frameNumber++;
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties) {
        _vk.GetPhysicalDeviceMemoryProperties(_chosenGPU, out var memProperties);

        for (int i = 0; i < memProperties.MemoryTypeCount; i++) {
            if ((typeFilter & (1 << i)) != 0 &&
                (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties) {
                return (uint) i;
            }
        }

        throw new Exception("failed to find suitable memory type!");
    }

    private unsafe void CreateVertexBuffer() {
        // 1. Define your triangle data
        var vertices = new Vertex[] {
            new(new Vector3(-0.5f, -0.5f, 0.0f), new Vector3(1.0f, 0.0f, 0.0f)),
            new(new Vector3(0.5f, -0.5f, 0.0f), new Vector3(0.0f, 1.0f, 0.0f)),
            new(new Vector3(0.5f, 0.5f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f)),
            new(new Vector3(-0.5f, 0.5f, 0.0f), new Vector3(1.0f, 1.0f, 1.0f))
        };

        ulong bufferSize = (ulong) (sizeof(Vertex) * vertices.Length);

        // 2. Create the Buffer Object
        var bufferInfo = new BufferCreateInfo {
            SType = StructureType.BufferCreateInfo,
            Size = bufferSize,
            Usage = BufferUsageFlags.VertexBufferBit,
            SharingMode = SharingMode.Exclusive
        };

        var res = _vk.CreateBuffer(_device, ref bufferInfo, null, out _vertexBuffer);
        if (res != Result.Success) {
            throw new VulkanException("Could not create buffer!", res);
        }

        // 3. Get Memory Requirements
        _vk.GetBufferMemoryRequirements(_device, _vertexBuffer, out var memRequirements);

        // 4. Allocate Memory
        // Note: We use HostVisible | HostCoherent so we can map it directly from CPU.
        // In a real engine, you'd use a "Staging Buffer" (CPU -> Staging -> GPU) for better performance.
        var allocInfo = new MemoryAllocateInfo {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };

        _vk.AllocateMemory(_device, allocInfo, null, out _vertexBufferMemory);

        // 5. Bind Memory to Buffer
        _vk.BindBufferMemory(_device, _vertexBuffer, _vertexBufferMemory, 0);

        // 6. Copy Data (Map -> Copy -> Unmap)
        void* data;
        _vk.MapMemory(_device, _vertexBufferMemory, 0, bufferSize, 0, &data);
        vertices.AsSpan().CopyTo(new Span<Vertex>(data, vertices.Length));
        _vk.UnmapMemory(_device, _vertexBufferMemory);
    }

    private unsafe void CreateIndexBuffer() {
        // 1. Define your triangle data
        ulong bufferSize = (ulong) (sizeof(ushort) * _indices.Length);

        // 2. Create the Buffer Object
        var bufferInfo = new BufferCreateInfo {
            SType = StructureType.BufferCreateInfo,
            Size = bufferSize,
            Usage = BufferUsageFlags.IndexBufferBit,
            SharingMode = SharingMode.Exclusive
        };

        var res = _vk.CreateBuffer(_device, ref bufferInfo, null, out _indexBuffer);
        if (res != Result.Success) {
            throw new VulkanException("Could not create buffer!", res);
        }

        // 3. Get Memory Requirements
        _vk.GetBufferMemoryRequirements(_device, _indexBuffer, out var memRequirements);

        // 4. Allocate Memory
        // Note: We use HostVisible | HostCoherent so we can map it directly from CPU.
        // In a real engine, you'd use a "Staging Buffer" (CPU -> Staging -> GPU) for better performance.
        var allocInfo = new MemoryAllocateInfo {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
        };

        _vk.AllocateMemory(_device, allocInfo, null, out _indexBufferMemory);

        // 5. Bind Memory to Buffer
        _vk.BindBufferMemory(_device, _indexBuffer, _indexBufferMemory, 0);

        // 6. Copy Data (Map -> Copy -> Unmap)
        void* data;
        _vk.MapMemory(_device, _indexBufferMemory, 0, bufferSize, 0, &data);
        _indices.AsSpan().CopyTo(new Span<ushort>(data, _indices.Length));
        _vk.UnmapMemory(_device, _indexBufferMemory);
    }

    #region Debug Print Methods

    private static unsafe void PrintInstanceInfo() {
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("                    VULKAN INSTANCE INFO");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        // Get instance version
        uint apiVersion = 0;
        _vk.EnumerateInstanceVersion(ref apiVersion);
        var major = apiVersion >> 22;
        var minor = (apiVersion >> 12) & 0x3FF;
        var patch = apiVersion & 0xFFF;
        Console.WriteLine($"  Instance API Version: {major}.{minor}.{patch}");

        // Requested API version
        Console.WriteLine($"  Requested API Version: 1.3 (Vk.Version13)");

        // Get enabled extensions from window surface requirements
        var extensions = GetRequiredExtensions();
        Console.WriteLine($"  Enabled Extensions ({extensions.Length + 1}):");
        foreach (var ext in extensions) {
            Console.WriteLine($"    • {ext}");
        }
        Console.WriteLine($"    • VK_EXT_debug_utils (validation)");

        Console.WriteLine($"  Validation Layers: Enabled");
        Console.WriteLine($"    • VK_LAYER_KHRONOS_validation");
        Console.WriteLine();
    }

    private static unsafe void PrintPhysicalDeviceInfo() {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("                   PHYSICAL DEVICE INFO");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        var props = _physicalDeviceSelector.Properties;
        var memProps = _physicalDeviceSelector.MemoryProperties;
        var features = _physicalDeviceSelector.Features;
        var queueFamilies = _physicalDeviceSelector.QueueFamilies;

        // Basic info
        Console.WriteLine($"  Device Name: {_physicalDeviceSelector.GetDeviceName()}");
        Console.WriteLine($"  Device Type: {props.DeviceType}");

        // API version supported
        var apiVersion = props.ApiVersion;
        var major = apiVersion >> 22;
        var minor = (apiVersion >> 12) & 0x3FF;
        var patch = apiVersion & 0xFFF;
        Console.WriteLine($"  API Version: {major}.{minor}.{patch}");

        // Driver version (vendor-specific encoding)
        var driverVersion = props.DriverVersion;
        Console.WriteLine($"  Driver Version: {driverVersion >> 22}.{(driverVersion >> 12) & 0x3FF}.{driverVersion & 0xFFF}");

        // Vendor ID
        var vendorName = props.VendorID switch {
            0x1002 => "AMD",
            0x1010 => "ImgTec",
            0x10DE => "NVIDIA",
            0x13B5 => "ARM",
            0x5143 => "Qualcomm",
            0x8086 => "Intel",
            _ => $"Unknown (0x{props.VendorID:X4})"
        };
        Console.WriteLine($"  Vendor: {vendorName}");
        Console.WriteLine($"  Device ID: 0x{props.DeviceID:X4}");

        // Limits
        Console.WriteLine();
        Console.WriteLine("  Key Limits:");
        Console.WriteLine($"    Max Image Dimension 2D: {props.Limits.MaxImageDimension2D}");
        Console.WriteLine($"    Max Uniform Buffer Range: {props.Limits.MaxUniformBufferRange / 1024} KB");
        Console.WriteLine($"    Max Storage Buffer Range: {props.Limits.MaxStorageBufferRange / (1024 * 1024)} MB");
        Console.WriteLine($"    Max Push Constants Size: {props.Limits.MaxPushConstantsSize} bytes");
        Console.WriteLine($"    Max Memory Allocation Count: {props.Limits.MaxMemoryAllocationCount}");
        Console.WriteLine($"    Max Bound Descriptor Sets: {props.Limits.MaxBoundDescriptorSets}");
        Console.WriteLine($"    Max Vertex Input Attributes: {props.Limits.MaxVertexInputAttributes}");
        Console.WriteLine($"    Max Framebuffer Width: {props.Limits.MaxFramebufferWidth}");
        Console.WriteLine($"    Max Framebuffer Height: {props.Limits.MaxFramebufferHeight}");

        // Memory info
        Console.WriteLine();
        Console.WriteLine($"  Memory Heaps ({memProps.MemoryHeapCount}):");
        for (uint i = 0; i < memProps.MemoryHeapCount; i++) {
            var heap = memProps.MemoryHeaps[(int) i];
            var sizeMB = heap.Size / (1024 * 1024);
            var sizeGB = sizeMB / 1024.0;
            var flags = heap.Flags.HasFlag(MemoryHeapFlags.DeviceLocalBit) ? "Device Local" : "Host";
            Console.WriteLine($"    Heap {i}: {(sizeGB >= 1 ? $"{sizeGB:F2} GB" : $"{sizeMB} MB")} [{flags}]");
        }

        Console.WriteLine();
        Console.WriteLine($"  Memory Types ({memProps.MemoryTypeCount}):");
        for (uint i = 0; i < memProps.MemoryTypeCount; i++) {
            var memType = memProps.MemoryTypes[(int) i];
            var flagsList = new List<string>();
            if (memType.PropertyFlags.HasFlag(MemoryPropertyFlags.DeviceLocalBit)) flagsList.Add("DeviceLocal");
            if (memType.PropertyFlags.HasFlag(MemoryPropertyFlags.HostVisibleBit)) flagsList.Add("HostVisible");
            if (memType.PropertyFlags.HasFlag(MemoryPropertyFlags.HostCoherentBit)) flagsList.Add("HostCoherent");
            if (memType.PropertyFlags.HasFlag(MemoryPropertyFlags.HostCachedBit)) flagsList.Add("HostCached");
            Console.WriteLine($"    Type {i}: Heap {memType.HeapIndex} [{string.Join(", ", flagsList)}]");
        }

        // Key features
        Console.WriteLine();
        Console.WriteLine("  Key Features:");
        Console.WriteLine($"    Geometry Shader: {(features.GeometryShader ? "✓" : "✗")}");
        Console.WriteLine($"    Tessellation Shader: {(features.TessellationShader ? "✓" : "✗")}");
        Console.WriteLine($"    Sampler Anisotropy: {(features.SamplerAnisotropy ? "✓" : "✗")}");
        Console.WriteLine($"    Multi Viewport: {(features.MultiViewport ? "✓" : "✗")}");
        Console.WriteLine($"    Wide Lines: {(features.WideLines ? "✓" : "✗")}");
        Console.WriteLine($"    Fill Mode Non-Solid: {(features.FillModeNonSolid ? "✓" : "✗")}");
        Console.WriteLine($"    Multi Draw Indirect: {(features.MultiDrawIndirect ? "✓" : "✗")}");
        Console.WriteLine($"    Shader Float64: {(features.ShaderFloat64 ? "✓" : "✗")}");
        Console.WriteLine($"    Shader Int64: {(features.ShaderInt64 ? "✓" : "✗")}");

        // Queue families
        Console.WriteLine();
        Console.WriteLine("  Queue Families:");
        Console.WriteLine($"    Graphics: {(queueFamilies.GraphicsFamily.HasValue ? $"Family {queueFamilies.GraphicsFamily.Value}" : "Not found")}");
        Console.WriteLine($"    Compute:  {(queueFamilies.ComputeFamily.HasValue ? $"Family {queueFamilies.ComputeFamily.Value}" : "Not found")}");
        Console.WriteLine($"    Transfer: {(queueFamilies.TransferFamily.HasValue ? $"Family {queueFamilies.TransferFamily.Value}" : "Not found")}");
        Console.WriteLine($"    Present:  {(queueFamilies.PresentFamily.HasValue ? $"Family {queueFamilies.PresentFamily.Value}" : "Not found")}");
        Console.WriteLine();
    }

    private static void PrintLogicalDeviceInfo() {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("                    LOGICAL DEVICE INFO");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        var queueFamilies = _physicalDeviceSelector.QueueFamilies;

        Console.WriteLine($"  Device Handle: 0x{_device.Handle:X16}");
        Console.WriteLine();
        Console.WriteLine("  Enabled Extensions:");
        Console.WriteLine($"    • {KhrSwapchain.ExtensionName}");
        Console.WriteLine();
        Console.WriteLine("  Created Queues:");
        Console.WriteLine($"    Graphics Queue: Handle 0x{_graphicsQueue.Handle:X16} (Family {queueFamilies.GraphicsFamily})");
        Console.WriteLine($"    Present Queue:  Handle 0x{_presentQueue.Handle:X16} (Family {queueFamilies.PresentFamily})");

        if (queueFamilies.GraphicsFamily == queueFamilies.PresentFamily) {
            Console.WriteLine("    (Graphics and Present share the same queue family)");
        }
        Console.WriteLine();
    }

    private static void PrintSwapchainInfo() {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("                      SWAPCHAIN INFO");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        var surfaceCaps = _swapchainBuilder.SurfaceCapabilities;

        Console.WriteLine($"  Swapchain Handle: 0x{_swapchain.Handle:X16}");
        Console.WriteLine();
        Console.WriteLine("  Configuration:");
        Console.WriteLine($"    Extent: {_swapchainExtent.Width} x {_swapchainExtent.Height}");
        Console.WriteLine($"    Image Format: {_swapchainImageFormat}");
        Console.WriteLine($"    Image Count: {_swapchainImages.Length}");

        // Find which present mode was chosen
        var presentModes = _swapchainBuilder.PresentModes;
        var chosenMode = presentModes.Contains(PresentModeKHR.MailboxKhr)
            ? PresentModeKHR.MailboxKhr
            : PresentModeKHR.FifoKhr;
        Console.WriteLine($"    Present Mode: {SwapchainBuilder.GetPresentModeDescription(chosenMode)}");

        Console.WriteLine();
        Console.WriteLine("  Surface Capabilities:");
        Console.WriteLine($"    Min Image Count: {surfaceCaps.MinImageCount}");
        Console.WriteLine($"    Max Image Count: {(surfaceCaps.MaxImageCount == 0 ? "Unlimited" : surfaceCaps.MaxImageCount)}");
        Console.WriteLine($"    Current Extent: {surfaceCaps.CurrentExtent.Width} x {surfaceCaps.CurrentExtent.Height}");
        Console.WriteLine($"    Min Extent: {surfaceCaps.MinImageExtent.Width} x {surfaceCaps.MinImageExtent.Height}");
        Console.WriteLine($"    Max Extent: {surfaceCaps.MaxImageExtent.Width} x {surfaceCaps.MaxImageExtent.Height}");
        Console.WriteLine($"    Max Image Array Layers: {surfaceCaps.MaxImageArrayLayers}");
        Console.WriteLine($"    Supported Transforms: {surfaceCaps.SupportedTransforms}");
        Console.WriteLine($"    Current Transform: {surfaceCaps.CurrentTransform}");
        Console.WriteLine($"    Supported Composite Alpha: {surfaceCaps.SupportedCompositeAlpha}");
        Console.WriteLine($"    Supported Usage Flags: {surfaceCaps.SupportedUsageFlags}");

        Console.WriteLine();
        Console.WriteLine("  Available Surface Formats:");
        foreach (var format in _swapchainBuilder.SurfaceFormats.Take(5)) {
            var marker = format.Format == _swapchainImageFormat ? " ← selected" : "";
            Console.WriteLine($"    • {format.Format} ({format.ColorSpace}){marker}");
        }
        if (_swapchainBuilder.SurfaceFormats.Length > 5)
            Console.WriteLine($"    ... and {_swapchainBuilder.SurfaceFormats.Length - 5} more");

        Console.WriteLine();
        Console.WriteLine("  Available Present Modes:");
        foreach (var mode in _swapchainBuilder.PresentModes) {
            var marker = mode == chosenMode ? " ← selected" : "";
            Console.WriteLine($"    • {SwapchainBuilder.GetPresentModeDescription(mode)}{marker}");
        }

        Console.WriteLine();
        Console.WriteLine("  Swapchain Images:");
        for (int i = 0; i < _swapchainImages.Length; i++) {
            Console.WriteLine($"    Image {i}: Handle 0x{_swapchainImages[i].Handle:X16}");
            Console.WriteLine($"             View   0x{_swapchainImageViews[i].Handle:X16}");
        }
        Console.WriteLine();
    }

    private static void PrintCommandsInfo() {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("                   COMMAND & SYNC STRUCTURES");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        Console.WriteLine($"  Frame Overlap: {_frameOverlap} frames (triple buffering)");
        Console.WriteLine($"  Graphics Queue Family: {_graphicsQueueFamily}");
        Console.WriteLine();
        Console.WriteLine("  Per-Frame Resources:");
        for (int i = 0; i < _frameOverlap; i++) {
            Console.WriteLine($"    Frame {i}:");
            Console.WriteLine($"      Command Pool:        0x{_frames[i].CommandPool.Handle:X16}");
            Console.WriteLine($"      Command Buffer:      0x{_frames[i].MainCommandBuffer.Handle:X16}");
            Console.WriteLine($"      Render Fence:        0x{_frames[i].RenderFence.Handle:X16}");
            Console.WriteLine($"      Swapchain Semaphore: 0x{_frames[i].SwapchainSemaphore.Handle:X16}");
            Console.WriteLine($"      Render Semaphore:    0x{_frames[i].RenderSemaphore.Handle:X16}");
        }
        Console.WriteLine();
    }

    private static void PrintPipelineInfo() {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("                      GRAPHICS PIPELINES");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        Console.WriteLine("  Triangle Pipeline:");
        Console.WriteLine($"    Pipeline Handle: 0x{_trianglePipeline.Handle:X16}");
        Console.WriteLine($"    Layout Handle:   0x{_trianglePipelineLayout.Handle:X16}");
        Console.WriteLine("    Configuration:");
        Console.WriteLine("      • Topology: Triangle List");
        Console.WriteLine("      • Polygon Mode: Fill");
        Console.WriteLine("      • Cull Mode: None");
        Console.WriteLine("      • Depth Test: Disabled");
        Console.WriteLine("      • Blending: Disabled");
        Console.WriteLine($"      • Color Format: {_swapchainImageFormat}");
        Console.WriteLine("    Shaders:");
        Console.WriteLine("      • Vertex: colored_triangle.vert (hardcoded positions)");
        Console.WriteLine("      • Fragment: colored_triangle.frag (vertex colors)");
        Console.WriteLine();
    }

    #endregion
}
