using Silk.NET.Vulkan;

namespace Shiron.VulkanDumpster;

/// <summary>
/// Utility functions for working with Vulkan images.
/// </summary>
public static class ImageUtils {
    /// <summary>
    /// Transition an image from one layout to another using a pipeline barrier.
    /// Uses synchronization2 features from Vulkan 1.3.
    /// </summary>
    /// <param name="vk">The Vulkan API instance.</param>
    /// <param name="cmd">The command buffer to record the transition into.</param>
    /// <param name="image">The image to transition.</param>
    /// <param name="currentLayout">The current layout of the image.</param>
    /// <param name="newLayout">The target layout for the image.</param>
    /// <remarks>
    /// This is a simple implementation using ALL_COMMANDS stage mask which may cause
    /// some GPU pipeline stalls. For production use with many transitions, consider
    /// using more fine-grained stage masks.
    /// </remarks>
    public static unsafe void TransitionImage(Vk vk, CommandBuffer cmd, Image image,
        ImageLayout currentLayout, ImageLayout newLayout) {
        var imageBarrier = new ImageMemoryBarrier2 {
            SType = StructureType.ImageMemoryBarrier2,
            PNext = null,
            SrcStageMask = PipelineStageFlags2.AllCommandsBit,
            SrcAccessMask = AccessFlags2.MemoryWriteBit,
            DstStageMask = PipelineStageFlags2.AllCommandsBit,
            DstAccessMask = AccessFlags2.MemoryWriteBit | AccessFlags2.MemoryReadBit,
            OldLayout = currentLayout,
            NewLayout = newLayout,
            SubresourceRange = ImageSubresourceRange(
                newLayout == ImageLayout.DepthAttachmentOptimal
                    ? ImageAspectFlags.DepthBit
                    : ImageAspectFlags.ColorBit),
            Image = image
        };

        var depInfo = new DependencyInfo {
            SType = StructureType.DependencyInfo,
            PNext = null,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &imageBarrier
        };

        vk.CmdPipelineBarrier2(cmd, &depInfo);
    }

    /// <summary>
    /// Create a default image subresource range covering all mip levels and array layers.
    /// </summary>
    /// <param name="aspectMask">The aspect of the image (Color or Depth).</param>
    /// <returns>A subresource range covering the entire image.</returns>
    public static ImageSubresourceRange ImageSubresourceRange(ImageAspectFlags aspectMask) {
        return new ImageSubresourceRange {
            AspectMask = aspectMask,
            BaseMipLevel = 0,
            LevelCount = Vk.RemainingMipLevels,
            BaseArrayLayer = 0,
            LayerCount = Vk.RemainingArrayLayers
        };
    }
}
