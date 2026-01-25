using System;
using System.Collections.Generic;
using System.Linq;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Shiron.VulkanDumpster.Vulkan;

/// <summary>
/// Utility class for creating and managing a Vulkan swapchain for presenting rendered images.
/// </summary>
public sealed unsafe class SwapchainBuilder : IDisposable {
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly SurfaceKHR _surface;
    private readonly KhrSurface _khrSurface;
    private readonly KhrSwapchain _khrSwapchain;

    private readonly QueueFamilyIndices _queueFamilyIndices;

    // Configuration
    private Vector2D<uint>? _desiredExtent;
    private Format _desiredFormat = Format.B8G8R8A8Srgb;
    private ColorSpaceKHR _desiredColorSpace = ColorSpaceKHR.SpaceSrgbNonlinearKhr;
    private PresentModeKHR _desiredPresentMode = PresentModeKHR.MailboxKhr;
    private uint _desiredImageCount = 3; // Triple buffering
    private ImageUsageFlags _imageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit;
    private CompositeAlphaFlagsKHR _compositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr;
    private bool _clipped = true;
    private SwapchainKHR _oldSwapchain;

    // Created resources
    private SwapchainKHR _swapchain;
    private Image[] _swapchainImages = [];
    private ImageView[] _swapchainImageViews = [];
    private Format _swapchainFormat;
    private Extent2D _swapchainExtent;

    // Cached surface info
    private SurfaceCapabilitiesKHR _surfaceCapabilities;
    private SurfaceFormatKHR[] _surfaceFormats = [];
    private PresentModeKHR[] _presentModes = [];

    private bool _built;

    public SwapchainBuilder(
        Vk vk,
        Device device,
        PhysicalDevice physicalDevice,
        SurfaceKHR surface,
        KhrSurface khrSurface,
        QueueFamilyIndices queueFamilyIndices) {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        if (device.Handle == 0) throw new ArgumentException("Invalid device.", nameof(device));
        if (physicalDevice.Handle == 0) throw new ArgumentException("Invalid physical device.", nameof(physicalDevice));
        if (surface.Handle == 0) throw new ArgumentException("Invalid surface.", nameof(surface));

        _device = device;
        _physicalDevice = physicalDevice;
        _surface = surface;
        _khrSurface = khrSurface ?? throw new ArgumentNullException(nameof(khrSurface));
        _queueFamilyIndices = queueFamilyIndices;

        if (!_vk.TryGetDeviceExtension(_vk.CurrentInstance!.Value, _device, out _khrSwapchain))
            throw new VulkanException("Failed to get KHR_swapchain extension.", Result.ErrorExtensionNotPresent);

        QuerySurfaceSupport();
    }

    public SwapchainKHR Swapchain => _swapchain;
    public Image[] Images => _swapchainImages;
    public ImageView[] ImageViews => _swapchainImageViews;
    public Format ImageFormat => _swapchainFormat;
    public Extent2D Extent => _swapchainExtent;
    public uint ImageCount => (uint)_swapchainImages.Length;

    // Expose surface info for external use
    public SurfaceCapabilitiesKHR SurfaceCapabilities => _surfaceCapabilities;
    public SurfaceFormatKHR[] SurfaceFormats => _surfaceFormats;
    public PresentModeKHR[] PresentModes => _presentModes;

    /// <summary>
    /// Set the desired swapchain extent. If not set, uses the surface's current extent.
    /// </summary>
    public SwapchainBuilder WithExtent(uint width, uint height) {
        _desiredExtent = new Vector2D<uint>(width, height);
        return this;
    }

    /// <summary>
    /// Set the desired swapchain extent from window size.
    /// </summary>
    public SwapchainBuilder WithExtent(Vector2D<int> windowSize) {
        _desiredExtent = new Vector2D<uint>((uint)windowSize.X, (uint)windowSize.Y);
        return this;
    }

    /// <summary>
    /// Set the desired surface format and color space.
    /// </summary>
    public SwapchainBuilder WithFormat(Format format, ColorSpaceKHR colorSpace = ColorSpaceKHR.SpaceSrgbNonlinearKhr) {
        _desiredFormat = format;
        _desiredColorSpace = colorSpace;
        return this;
    }

    /// <summary>
    /// Set the desired present mode. Defaults to Mailbox (triple buffering) if available.
    /// </summary>
    public SwapchainBuilder WithPresentMode(PresentModeKHR presentMode) {
        _desiredPresentMode = presentMode;
        return this;
    }

    /// <summary>
    /// Set the desired number of swapchain images. Defaults to 3 (triple buffering).
    /// </summary>
    public SwapchainBuilder WithImageCount(uint count) {
        _desiredImageCount = count;
        return this;
    }

    /// <summary>
    /// Set image usage flags. Defaults to ColorAttachment.
    /// </summary>
    public SwapchainBuilder WithImageUsage(ImageUsageFlags usage) {
        _imageUsage = usage;
        return this;
    }

    /// <summary>
    /// Set composite alpha mode. Defaults to Opaque.
    /// </summary>
    public SwapchainBuilder WithCompositeAlpha(CompositeAlphaFlagsKHR compositeAlpha) {
        _compositeAlpha = compositeAlpha;
        return this;
    }

    /// <summary>
    /// Set whether pixels obscured by other windows can be discarded. Defaults to true.
    /// </summary>
    public SwapchainBuilder WithClipping(bool clipped) {
        _clipped = clipped;
        return this;
    }

    /// <summary>
    /// Set the old swapchain for recreation (allows for smoother transitions).
    /// </summary>
    public SwapchainBuilder WithOldSwapchain(SwapchainKHR oldSwapchain) {
        _oldSwapchain = oldSwapchain;
        return this;
    }

    /// <summary>
    /// Build the swapchain with the specified configuration.
    /// Chooses format, present mode, extent, and image count based on device capabilities.
    /// </summary>
    public SwapchainKHR Build() {
        if (_built) throw new InvalidOperationException("SwapchainBuilder.Build() can only be called once.");
        _built = true;

        // Re-query surface capabilities (may have changed since construction)
        QuerySurfaceSupport();

        // Choose best surface format
        var surfaceFormat = ChooseSurfaceFormat();
        _swapchainFormat = surfaceFormat.Format;

        // Choose best present mode
        var presentMode = ChoosePresentMode();

        // Choose extent
        _swapchainExtent = ChooseExtent();

        // Determine image count
        var imageCount = _desiredImageCount;
        if (imageCount < _surfaceCapabilities.MinImageCount)
            imageCount = _surfaceCapabilities.MinImageCount;
        if (_surfaceCapabilities.MaxImageCount > 0 && imageCount > _surfaceCapabilities.MaxImageCount)
            imageCount = _surfaceCapabilities.MaxImageCount;

        // Create swapchain
        var createInfo = new SwapchainCreateInfoKHR {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = _swapchainFormat,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = _swapchainExtent,
            ImageArrayLayers = 1,
            ImageUsage = _imageUsage,
            PreTransform = _surfaceCapabilities.CurrentTransform,
            CompositeAlpha = _compositeAlpha,
            PresentMode = presentMode,
            Clipped = _clipped,
            OldSwapchain = _oldSwapchain
        };

        // Handle queue family sharing (concurrent when graphics/present differ).
        var graphicsFamily = _queueFamilyIndices.GraphicsFamily!.Value;
        var presentFamily = _queueFamilyIndices.PresentFamily!.Value;

        if (graphicsFamily != presentFamily) {
            var queueFamilyIndices = stackalloc uint[] { graphicsFamily, presentFamily };
            createInfo.ImageSharingMode = SharingMode.Concurrent;
            createInfo.QueueFamilyIndexCount = 2;
            createInfo.PQueueFamilyIndices = queueFamilyIndices;
        } else {
            createInfo.ImageSharingMode = SharingMode.Exclusive;
            createInfo.QueueFamilyIndexCount = 0;
            createInfo.PQueueFamilyIndices = null;
        }

        var result = _khrSwapchain.CreateSwapchain(_device, &createInfo, null, out _swapchain);
        if (result != Result.Success) {
            throw new VulkanException("Failed to create swapchain. " + result, result);
        }

        // Retrieve swapchain images
        RetrieveSwapchainImages();

        // Create image views
        CreateImageViews();

        return _swapchain;
    }

    /// <summary>
    /// Acquire the next image from the swapchain.
    /// </summary>
    public Result AcquireNextImage(Silk.NET.Vulkan.Semaphore semaphore, Fence fence, out uint imageIndex) {
        if (!_built)
            throw new InvalidOperationException("Swapchain not built. Call Build() first.");

        imageIndex = 0;
        return _khrSwapchain.AcquireNextImage(_device, _swapchain, ulong.MaxValue, semaphore, fence, ref imageIndex);
    }

    /// <summary>
    /// Present an image to the screen.
    /// </summary>
    public Result Present(Queue presentQueue, uint imageIndex, Silk.NET.Vulkan.Semaphore waitSemaphore) {
        if (!_built)
            throw new InvalidOperationException("Swapchain not built. Call Build() first.");

        var swapchain = _swapchain;
        var presentInfo = new PresentInfoKHR {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSemaphore,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndex
        };

        return _khrSwapchain.QueuePresent(presentQueue, &presentInfo);
    }

    private void QuerySurfaceSupport() {
        // Get surface capabilities
        _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_physicalDevice, _surface, out _surfaceCapabilities);

        // Get surface formats
        uint formatCount = 0;
        _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, &formatCount, null);
        if (formatCount > 0) {
            _surfaceFormats = new SurfaceFormatKHR[formatCount];
            fixed (SurfaceFormatKHR* pFormats = _surfaceFormats) {
                _khrSurface.GetPhysicalDeviceSurfaceFormats(_physicalDevice, _surface, &formatCount, pFormats);
            }
        }

        // Get present modes
        uint presentModeCount = 0;
        _khrSurface.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, &presentModeCount, null);
        if (presentModeCount > 0) {
            _presentModes = new PresentModeKHR[presentModeCount];
            fixed (PresentModeKHR* pModes = _presentModes) {
                _khrSurface.GetPhysicalDeviceSurfacePresentModes(_physicalDevice, _surface, &presentModeCount, pModes);
            }
        }
    }

    private SurfaceFormatKHR ChooseSurfaceFormat() {
        // Look for desired format
        foreach (var format in _surfaceFormats) {
            if (format.Format == _desiredFormat && format.ColorSpace == _desiredColorSpace)
                return format;
        }

        // Look for any SRGB format
        foreach (var format in _surfaceFormats) {
            if (format.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                return format;
        }

        // Return first available
        return _surfaceFormats[0];
    }

    private PresentModeKHR ChoosePresentMode() {
        // Look for desired present mode
        if (_presentModes.Contains(_desiredPresentMode))
            return _desiredPresentMode;

        // Mailbox (triple buffering) is preferred
        if (_presentModes.Contains(PresentModeKHR.MailboxKhr))
            return PresentModeKHR.MailboxKhr;

        // FIFO is guaranteed to be available
        return PresentModeKHR.FifoKhr;
    }

    private Extent2D ChooseExtent() {
        // If the surface extent is defined (not uint.MaxValue), use it
        if (_surfaceCapabilities.CurrentExtent.Width != uint.MaxValue) {
            return _surfaceCapabilities.CurrentExtent;
        }

        // Otherwise, use desired extent clamped to surface limits
        if (_desiredExtent.HasValue) {
            return new Extent2D {
                Width = Math.Clamp(_desiredExtent.Value.X,
                    _surfaceCapabilities.MinImageExtent.Width,
                    _surfaceCapabilities.MaxImageExtent.Width),
                Height = Math.Clamp(_desiredExtent.Value.Y,
                    _surfaceCapabilities.MinImageExtent.Height,
                    _surfaceCapabilities.MaxImageExtent.Height)
            };
        }

        // Fallback to min extent
        return _surfaceCapabilities.MinImageExtent;
    }

    private void RetrieveSwapchainImages() {
        uint imageCount = 0;
        _khrSwapchain.GetSwapchainImages(_device, _swapchain, &imageCount, null);

        _swapchainImages = new Image[imageCount];
        fixed (Image* pImages = _swapchainImages) {
            _khrSwapchain.GetSwapchainImages(_device, _swapchain, &imageCount, pImages);
        }
    }

    private void CreateImageViews() {
        _swapchainImageViews = new ImageView[_swapchainImages.Length];

        for (int i = 0; i < _swapchainImages.Length; i++) {
            var createInfo = new ImageViewCreateInfo {
                SType = StructureType.ImageViewCreateInfo,
                Image = _swapchainImages[i],
                ViewType = ImageViewType.Type2D,
                Format = _swapchainFormat,
                Components = new ComponentMapping {
                    R = ComponentSwizzle.Identity,
                    G = ComponentSwizzle.Identity,
                    B = ComponentSwizzle.Identity,
                    A = ComponentSwizzle.Identity
                },
                SubresourceRange = new ImageSubresourceRange {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            var result = _vk.CreateImageView(_device, &createInfo, null, out _swapchainImageViews[i]);
            if (result != Result.Success) {
                throw new VulkanException($"Failed to create image view {i}. " + result, result);
            }
        }
    }

    /// <summary>
    /// Get a description of the chosen present mode.
    /// </summary>
    public static string GetPresentModeDescription(PresentModeKHR mode) {
        return mode switch {
            PresentModeKHR.ImmediateKhr => "Immediate (no vsync, may tear)",
            PresentModeKHR.MailboxKhr => "Mailbox (triple buffering, no tearing)",
            PresentModeKHR.FifoKhr => "FIFO (vsync, double buffering)",
            PresentModeKHR.FifoRelaxedKhr => "FIFO Relaxed (vsync with late frame allowance)",
            PresentModeKHR.SharedDemandRefreshKhr => "Shared Demand Refresh",
            PresentModeKHR.SharedContinuousRefreshKhr => "Shared Continuous Refresh",
            _ => mode.ToString()
        };
    }

    public void Dispose() {
        // Builder no longer owns the swapchain lifecycle after Build() is called.
        // Cleanup of intermediate resources if any would go here.
    }
}
