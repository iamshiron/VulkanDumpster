using System.Numerics;
using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using Silk.NET.Maths;

namespace Shiron.VulkanDumpster.Vulkan;

public unsafe class Renderer : IDisposable {
    private readonly VulkanContext _ctx;
    private readonly IWindow _window;

    private SwapchainBuilder _swapchainBuilder = null!;
    private SwapchainKHR _swapchain;
    private Image[] _swapchainImages = null!;
    private ImageView[] _swapchainImageViews = null!;
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

    public Renderer(VulkanContext ctx, IWindow window) {
        _ctx = ctx;
        _window = window;
        _window.Resize += OnResize;

        CreateSwapchain();
        CreateDepthResources();
        CreateFrameResources();
    }

    private void OnResize(Vector2D<int> size) {
        _resized = true;
    }

    public void CreateSwapchain() {
        _swapchainBuilder = new SwapchainBuilder(
                _ctx.Vk, _ctx.Device, _ctx.PhysicalDevice, _ctx.Surface, _ctx.KhrSurface,
                _ctx.QueueFamilies)
            .WithExtent(_window.Size)
            .WithFormat(Format.B8G8R8A8Srgb)
            .WithPresentMode(PresentModeKHR.MailboxKhr)
            .WithImageCount(FrameOverlap);
        
        if (_swapchain.Handle != 0) {
            _swapchainBuilder.WithOldSwapchain(_swapchain);
        }

        _swapchain = _swapchainBuilder.Build();
        _swapchainImages = _swapchainBuilder.Images;
        _swapchainImageViews = _swapchainBuilder.ImageViews;
        _swapchainImageFormat = _swapchainBuilder.ImageFormat;
        _swapchainExtent = _swapchainBuilder.Extent;
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
                return (uint)i;
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
            _ctx.Vk.CreateFence(_ctx.Device, &fenceInfo, null, out _frames[i].RenderFence);
            _ctx.Vk.CreateSemaphore(_ctx.Device, &semaphoreInfo, null, out _frames[i].SwapchainSemaphore);
            _ctx.Vk.CreateSemaphore(_ctx.Device, &semaphoreInfo, null, out _frames[i].RenderSemaphore);
        }
    }

    public VulkanCommandBuffer BeginFrame() {
        var fence = CurrentFrame.RenderFence;
        _ctx.Vk.WaitForFences(_ctx.Device, 1, &fence, true, 1_000_000_000);

        if (_resized) {
            _ctx.Vk.DeviceWaitIdle(_ctx.Device);
            CreateSwapchain();
            _resized = false;
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
            Semaphore = CurrentFrame.RenderSemaphore,
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
        var renderSemaphore = CurrentFrame.RenderSemaphore;
        var imageIndex = _imageIndex;
        var presentInfo = new PresentInfoKHR {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &renderSemaphore,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex
        };

        _ctx.KhrSwapchain.QueuePresent(_ctx.PresentQueue, &presentInfo);

        _frameNumber++;
    }

    public void Dispose() {
        _ctx.Vk.DeviceWaitIdle(_ctx.Device);

        _ctx.Vk.DestroyImageView(_ctx.Device, _depthImageView, null);
        _ctx.Vk.DestroyImage(_ctx.Device, _depthImage, null);
        _ctx.Vk.FreeMemory(_ctx.Device, _depthImageMemory, null);

        for (int i = 0; i < FrameOverlap; i++) {
             _ctx.Vk.DestroyFence(_ctx.Device, _frames[i].RenderFence, null);
             _ctx.Vk.DestroySemaphore(_ctx.Device, _frames[i].SwapchainSemaphore, null);
             _ctx.Vk.DestroySemaphore(_ctx.Device, _frames[i].RenderSemaphore, null);
        }

        foreach (var view in _swapchainImageViews) {
            _ctx.Vk.DestroyImageView(_ctx.Device, view, null);
        }

        _ctx.KhrSwapchain.DestroySwapchain(_ctx.Device, _swapchain, null);
    }
}