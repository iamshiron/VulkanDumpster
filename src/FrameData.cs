using Silk.NET.Vulkan;

namespace Shiron.VulkanDumpster;

/// <summary>
/// Holds the structures needed to render a single frame.
/// Used for double/triple buffering - GPU can work on one frame while CPU prepares another.
/// </summary>
public struct FrameData {
    /// <summary>
    /// Command pool for allocating command buffers for this frame.
    /// </summary>
    public CommandPool CommandPool;

    /// <summary>
    /// The main command buffer used for rendering this frame.
    /// </summary>
    public CommandBuffer MainCommandBuffer;

    /// <summary>
    /// Semaphore signaled when the swapchain image is ready to be rendered to.
    /// Used to synchronize rendering with swapchain image acquisition.
    /// </summary>
    public Silk.NET.Vulkan.Semaphore SwapchainSemaphore;

    /// <summary>
    /// Semaphore signaled when rendering has finished.
    /// Used to synchronize presentation with rendering completion.
    /// </summary>
    public Silk.NET.Vulkan.Semaphore RenderSemaphore;

    /// <summary>
    /// Fence to wait for GPU to finish rendering this frame.
    /// Used to synchronize CPU with GPU for this frame's resources.
    /// </summary>
    public Fence RenderFence;
}
