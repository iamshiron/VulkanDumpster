using System.Numerics;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
namespace Shiron.VulkanDumpster.Vulkan;

public unsafe class Renderer : IDisposable {
    private readonly VulkanContext _ctx;
    private readonly IWindow _window;
    private SwapchainBuilder _swapchainBuilder = null!;
    private SwapchainKHR _swapchain;
    private Image[] _swapchainImages = null!;
    private ImageView[] _swapchainImageViews = null!;
    private Silk.NET.Vulkan.Semaphore[] _renderSemaphores = null!;
    private Format _swapchainImageFormat;
    private Extent2D _swapchainExtent;
    // Depth resources
    private Image _depthImage;
    private DeviceMemory _depthImageMemory;
    private ImageView _depthImageView;
    private Format _depthFormat = Format.D32Sfloat;
    private const int FrameOverlap = 3;
    private FrameData[] _frames = null!;
    private int _frameNumber;
    private bool _resized;
    public int CurrentFrameIndex => _frameNumber % FrameOverlap;
    public ref FrameData CurrentFrame => ref _frames[CurrentFrameIndex];
    public Extent2D SwapchainExtent => _swapchainExtent;
    public Format SwapchainImageFormat => _swapchainImageFormat;
    public Format DepthFormat => _depthFormat;
    private uint _imageIndex;
    private CommandBuffer _currentCmd;
    private readonly AppSettings _settings;
    public GPUProfiler GPUProfiler { get; private set; }

    public Renderer(VulkanContext ctx, IWindow window, AppSettings settings) {
        _ctx = ctx;
        _window = window;
        _settings = settings;
        _window.Resize += OnResize;
        RecreateSwapchain();
        CreateFrameResources();
        GPUProfiler = new GPUProfiler(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice, FrameOverlap);
    }
    private void OnResize(Vector2D<int> size) {
        _resized = true;
    }
    public void RecreateSwapchain() {
        if (_window.Size.X == 0 || _window.Size.Y == 0) return;
        _ctx.Vk.DeviceWaitIdle(_ctx.Device);
        if (_depthImageView.Handle != 0) _ctx.Vk.DestroyImageView(_ctx.Device, _depthImageView, null);
        if (_depthImage.Handle != 0) _ctx.Vk.DestroyImage(_ctx.Device, _depthImage, null);
        if (_depthImageMemory.Handle != 0) _ctx.Vk.FreeMemory(_ctx.Device, _depthImageMemory, null);
        _depthImageView = default;
        _depthImage = default;
        _depthImageMemory = default;
        if (_swapchainImageViews != null) {
            foreach (var view in _swapchainImageViews) {
                if (view.Handle != 0) _ctx.Vk.DestroyImageView(_ctx.Device, view, null);
            }
        }
        if (_renderSemaphores != null) {
            foreach (var sem in _renderSemaphores) {
                if (sem.Handle != 0) _ctx.Vk.DestroySemaphore(_ctx.Device, sem, null);
            }
        }
        var oldSwapchain = _swapchain;

        // FIFO is the standard VSync mode. 
        // Mailbox is often called "Fast VSync" or "Triple Buffering" but it still allows the GPU to run faster than refresh 
        // in some implementations or just drops frames. Strictly use FIFO for standard VSync.
        var presentMode = _settings.VSync ? PresentModeKHR.FifoKhr : PresentModeKHR.ImmediateKhr;

        var builder = new SwapchainBuilder(
                _ctx.Vk, _ctx.Device, _ctx.PhysicalDevice, _ctx.Surface, _ctx.KhrSurface,
                _ctx.QueueFamilies)
            .WithExtent(_window.Size)
            .WithFormat(Format.B8G8R8A8Srgb)
            .WithPresentMode(presentMode)
            .WithImageCount(FrameOverlap);
        if (oldSwapchain.Handle != 0) {
            builder.WithOldSwapchain(oldSwapchain);
        }
        _swapchain = builder.Build();
        if (oldSwapchain.Handle != 0) {
            _ctx.KhrSwapchain.DestroySwapchain(_ctx.Device, oldSwapchain, null);
        }
        _swapchainImages = builder.Images;
        _swapchainImageViews = builder.ImageViews;
        _swapchainImageFormat = builder.ImageFormat;
        _swapchainExtent = builder.Extent;

        _renderSemaphores = new Silk.NET.Vulkan.Semaphore[_swapchainImages.Length];
        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        for (int i = 0; i < _renderSemaphores.Length; i++) {
            _ctx.Vk.CreateSemaphore(_ctx.Device, &semaphoreInfo, null, out _renderSemaphores[i]);
        }

        CreateDepthResources();
        builder.Dispose();
    }
    private void CreateDepthResources() {
        var imageInfo = new ImageCreateInfo {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Extent = new Extent3D(_swapchainExtent.Width, _swapchainExtent.Height, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Format = _depthFormat,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Undefined,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            Samples = SampleCountFlags.Count1Bit,
            SharingMode = SharingMode.Exclusive
        };
        if (_ctx.Vk.CreateImage(_ctx.Device, &imageInfo, null, out _depthImage) != Result.Success) {
            throw new Exception("Failed to create depth image!");
        }
        _ctx.Vk.GetImageMemoryRequirements(_ctx.Device, _depthImage, out var memRequirements);
        var allocInfo = new MemoryAllocateInfo {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, MemoryPropertyFlags.DeviceLocalBit)
        };
        if (_ctx.Vk.AllocateMemory(_ctx.Device, &allocInfo, null, out _depthImageMemory) != Result.Success) {
            throw new Exception("Failed to allocate depth image memory!");
        }
        _ctx.Vk.BindImageMemory(_ctx.Device, _depthImage, _depthImageMemory, 0);
        var viewInfo = new ImageViewCreateInfo {
            SType = StructureType.ImageViewCreateInfo,
            Image = _depthImage,
            ViewType = ImageViewType.Type2D,
            Format = _depthFormat,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.DepthBit, 0, 1, 0, 1)
        };
        if (_ctx.Vk.CreateImageView(_ctx.Device, &viewInfo, null, out _depthImageView) != Result.Success) {
            throw new Exception("Failed to create depth image view!");
        }
    }
    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties) {
        _ctx.Vk.GetPhysicalDeviceMemoryProperties(_ctx.PhysicalDevice, out var memProperties);
        for (int i = 0; i < memProperties.MemoryTypeCount; i++) {
            if ((typeFilter & (1 << i)) != 0 && (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties) {
                return (uint) i;
            }
        }
        throw new Exception("Failed to find suitable memory type!");
    }
    private void CreateFrameResources() {
        _frames = new FrameData[FrameOverlap];
        var cmdAllocInfo = new CommandBufferAllocateInfo {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _ctx.CommandPool,
            CommandBufferCount = 1,
            Level = CommandBufferLevel.Primary
        };
        var fenceInfo = new FenceCreateInfo {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit
        };
        var semaphoreInfo = new SemaphoreCreateInfo {
            SType = StructureType.SemaphoreCreateInfo
        };
        for (int i = 0; i < FrameOverlap; i++) {
            _ctx.Vk.AllocateCommandBuffers(_ctx.Device, &cmdAllocInfo, out _frames[i].MainCommandBuffer);
            _frames[i].CommandPool = _ctx.CommandPool;
            // Create Sync Objects
            _ctx.Vk.CreateFence(_ctx.Device, &fenceInfo, null, out _frames[i].RenderFence);
            _ctx.Vk.CreateSemaphore(_ctx.Device, &semaphoreInfo, null, out _frames[i].SwapchainSemaphore);
            _frames[i].DeletionQueue = new List<Action>();
        }
    }
    public VulkanCommandBuffer BeginFrame() {
        if (_window.Size.X == 0 || _window.Size.Y == 0) return default;
        var fence = CurrentFrame.RenderFence;
        _ctx.Vk.WaitForFences(_ctx.Device, 1, &fence, true, 1_000_000_000);
        
        GPUProfiler.BeginFrame(CurrentFrameIndex);
        VulkanCommandProfiler.Reset();

        // 1. Process resources queued for deletion on THIS frame (now safe to delete)
        foreach (var action in CurrentFrame.DeletionQueue) {
            action();
        }
        CurrentFrame.DeletionQueue.Clear();
        // 2. Move newly queued global deletions to THIS frame's queue (to be deleted when we come back to this frame)
        // This effectively delays deletion by 'FrameOverlap' frames.
        while (_ctx.DeletionQueue.TryDequeue(out var action)) {
            CurrentFrame.DeletionQueue.Add(action);
        }
        if (_resized) {
            RecreateSwapchain();
            _resized = false;
            // Return early to skip this frame if minimized or just recreated
            if (_swapchain.Handle == 0) return default;
        }
        _ctx.Vk.ResetFences(_ctx.Device, 1, &fence);
        uint imageIndex = 0;
        var result = _ctx.KhrSwapchain.AcquireNextImage(_ctx.Device, _swapchain, 1_000_000_000,
            CurrentFrame.SwapchainSemaphore, default, &imageIndex);
        _imageIndex = imageIndex;
        if (result == Result.ErrorOutOfDateKhr) {
            _resized = true;
            return default;
        }
        _currentCmd = CurrentFrame.MainCommandBuffer;
        _ctx.Vk.ResetCommandBuffer(_currentCmd, 0);
        var beginInfo = new CommandBufferBeginInfo {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        _ctx.Vk.BeginCommandBuffer(_currentCmd, &beginInfo);
        GPUProfiler.Reset(_currentCmd);
        ImageUtils.TransitionImage(_ctx.Vk, _currentCmd, _swapchainImages[_imageIndex],
            ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal);
        ImageUtils.TransitionImage(_ctx.Vk, _currentCmd, _depthImage,
            ImageLayout.Undefined, ImageLayout.DepthAttachmentOptimal);
        var colorAttachment = new RenderingAttachmentInfo {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = _swapchainImageViews[_imageIndex],
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = new ClearValue { Color = new ClearColorValue(0.01f, 0.01f, 0.01f, 1.0f) }
        };
        var depthAttachment = new RenderingAttachmentInfo {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = _depthImageView,
            ImageLayout = ImageLayout.DepthAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue(1.0f, 0) }
        };
        var renderInfo = new RenderingInfo {
            SType = StructureType.RenderingInfo,
            RenderArea = new Rect2D { Offset = new Offset2D(0, 0), Extent = _swapchainExtent },
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachment,
            PDepthAttachment = &depthAttachment
        };
        _ctx.Vk.CmdBeginRendering(_currentCmd, &renderInfo);
        var viewport = new Viewport(0, 0, _swapchainExtent.Width, _swapchainExtent.Height, 0, 1);
        var scissor = new Rect2D(new Offset2D(0, 0), _swapchainExtent);
        _ctx.Vk.CmdSetViewport(_currentCmd, 0, 1, &viewport);
        _ctx.Vk.CmdSetScissor(_currentCmd, 0, 1, &scissor);
        return new VulkanCommandBuffer(_ctx.Vk, _currentCmd);
    }
    public void EndFrame() {
        if (_currentCmd.Handle == 0) return;
        _ctx.Vk.CmdEndRendering(_currentCmd);
        ImageUtils.TransitionImage(_ctx.Vk, _currentCmd, _swapchainImages[_imageIndex],
            ImageLayout.ColorAttachmentOptimal, ImageLayout.PresentSrcKhr);
        _ctx.Vk.EndCommandBuffer(_currentCmd);
        var cmdInfo = new CommandBufferSubmitInfo {
            SType = StructureType.CommandBufferSubmitInfo,
            CommandBuffer = _currentCmd
        };
        var waitInfo = new SemaphoreSubmitInfo {
            SType = StructureType.SemaphoreSubmitInfo,
            Semaphore = CurrentFrame.SwapchainSemaphore,
            StageMask = PipelineStageFlags2.ColorAttachmentOutputBit
        };
        var signalInfo = new SemaphoreSubmitInfo {
            SType = StructureType.SemaphoreSubmitInfo,
            Semaphore = _renderSemaphores[_imageIndex],
            StageMask = PipelineStageFlags2.AllGraphicsBit
        };
        var submitInfo = new SubmitInfo2 {
            SType = StructureType.SubmitInfo2,
            CommandBufferInfoCount = 1,
            PCommandBufferInfos = &cmdInfo,
            WaitSemaphoreInfoCount = 1,
            PWaitSemaphoreInfos = &waitInfo,
            SignalSemaphoreInfoCount = 1,
            PSignalSemaphoreInfos = &signalInfo
        };
        _ctx.Vk.QueueSubmit2(_ctx.GraphicsQueue, 1, &submitInfo, CurrentFrame.RenderFence);
        var swapchain = _swapchain;
        var renderSemaphore = _renderSemaphores[_imageIndex];
        var imageIndex = _imageIndex;
        var presentInfo = new PresentInfoKHR {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &renderSemaphore,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex
        };
        var result = _ctx.KhrSwapchain.QueuePresent(_ctx.PresentQueue, &presentInfo);
        
        GPUProfiler.MarkSubmitted();

        if (result == Result.ErrorOutOfDateKhr || result == Result.SuboptimalKhr) {
            _resized = true;
        }
        _frameNumber++;
    }
    public void Dispose() {
        _ctx.Vk.DeviceWaitIdle(_ctx.Device);
        GPUProfiler.Dispose();
        // FLUSH DELETION QUEUES
        // 1. Process all per-frame queues
        for (int i = 0; i < FrameOverlap; i++) {
            if (_frames[i].DeletionQueue != null) {
                foreach (var action in _frames[i].DeletionQueue) {
                    action();
                }
                _frames[i].DeletionQueue.Clear();
            }
        }
        // 2. Process global queue
        while (_ctx.DeletionQueue.TryDequeue(out var action)) {
            action();
        }
        _ctx.Vk.DestroyImageView(_ctx.Device, _depthImageView, null);
        _ctx.Vk.DestroyImage(_ctx.Device, _depthImage, null);
        _ctx.Vk.FreeMemory(_ctx.Device, _depthImageMemory, null);
        for (int i = 0; i < FrameOverlap; i++) {
            _ctx.Vk.DestroyFence(_ctx.Device, _frames[i].RenderFence, null);
            _ctx.Vk.DestroySemaphore(_ctx.Device, _frames[i].SwapchainSemaphore, null);
        }
        foreach (var sem in _renderSemaphores) {
            _ctx.Vk.DestroySemaphore(_ctx.Device, sem, null);
        }
        foreach (var view in _swapchainImageViews) {
            _ctx.Vk.DestroyImageView(_ctx.Device, view, null);
        }
        _ctx.KhrSwapchain.DestroySwapchain(_ctx.Device, _swapchain, null);
    }
}
