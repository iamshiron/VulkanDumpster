using System;
using System.IO;
using Silk.NET.Vulkan;
using StbImageSharp;
namespace Shiron.VulkanDumpster.Vulkan;

public class Texture : IDisposable {
    private readonly VulkanContext _ctx;
    public VulkanImage Image { get; private set; }
    public VulkanSampler Sampler { get; private set; }
    public Texture(VulkanContext ctx, uint width, uint height, byte[] pixels, Filter magFilter = Filter.Linear, Filter minFilter = Filter.Linear) {
        _ctx = ctx;
        ulong imageSize = (ulong) (width * height * 4);
        var stagingBuffer = new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
            imageSize, BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        unsafe {
            fixed (byte* pData = pixels) {
                System.Buffer.MemoryCopy(pData, stagingBuffer.MappedData, imageSize, imageSize);
            }
        }
        Image = new VulkanImage(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
            width, height, 1, Format.R8G8B8A8Srgb,
            ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.ColorBit);
        TransitionAndCopy(stagingBuffer.Handle, width, height);
        _ctx.EnqueueDispose(() => stagingBuffer.Dispose());
        Sampler = new VulkanSampler(_ctx.Vk, _ctx.Device, magFilter, minFilter);
    }
    public Texture(VulkanContext ctx, string filePath, Filter magFilter = Filter.Linear, Filter minFilter = Filter.Linear) {
        _ctx = ctx;
        // Vulkan expects (0,0) at top-left, but many image formats/conventions use bottom-left.
        // Flipping on load is a common practice to ensure "up" is actually "up".
        StbImage.stbi_set_flip_vertically_on_load(1);
        // Load image from file
        ImageResult image = ImageResult.FromStream(File.OpenRead(filePath), ColorComponents.RedGreenBlueAlpha);
        ulong imageSize = (ulong) (image.Width * image.Height * 4);
        // Create staging buffer
        var stagingBuffer = new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
            imageSize, BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        unsafe {
            fixed (byte* pData = image.Data) {
                System.Buffer.MemoryCopy(pData, stagingBuffer.MappedData, imageSize, imageSize);
            }
        }
        // Create GPU image
        Image = new VulkanImage(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
            (uint) image.Width, (uint) image.Height, 1, Format.R8G8B8A8Srgb,
            ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.ColorBit);
        // Transition layout and copy
        TransitionAndCopy(stagingBuffer.Handle, (uint) image.Width, (uint) image.Height);
        _ctx.EnqueueDispose(() => stagingBuffer.Dispose());
        // Create sampler
        Sampler = new VulkanSampler(_ctx.Vk, _ctx.Device, magFilter, minFilter);
    }
    private unsafe void TransitionAndCopy(Silk.NET.Vulkan.Buffer stagingBuffer, uint width, uint height) {
        var allocInfo = new CommandBufferAllocateInfo {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = _ctx.CommandPool,
            CommandBufferCount = 1
        };
        _ctx.Vk.AllocateCommandBuffers(_ctx.Device, &allocInfo, out var commandBuffer);
        var beginInfo = new CommandBufferBeginInfo {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        _ctx.Vk.BeginCommandBuffer(commandBuffer, &beginInfo);
        // Transition to Transfer Dst
        ImageUtils.TransitionImage(_ctx.Vk, commandBuffer, Image.Handle,
            ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
        var region = new BufferImageCopy {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(width, height, 1)
        };
        _ctx.Vk.CmdCopyBufferToImage(commandBuffer, stagingBuffer, Image.Handle,
            ImageLayout.TransferDstOptimal, 1, &region);
        // Transition to Shader Read
        ImageUtils.TransitionImage(_ctx.Vk, commandBuffer, Image.Handle,
            ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);
        _ctx.Vk.EndCommandBuffer(commandBuffer);
        var submitInfo = new SubmitInfo {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };
        _ctx.Vk.QueueSubmit(_ctx.GraphicsQueue, 1, &submitInfo, default);
        var cmdBuffers = new[] { commandBuffer };
        _ctx.EnqueueDispose(() => {
            fixed (CommandBuffer* pCmd = cmdBuffers) {
                _ctx.Vk.FreeCommandBuffers(_ctx.Device, _ctx.CommandPool, 1, pCmd);
            }
        });
    }
    public void Dispose() {
        var img = Image;
        var smp = Sampler;
        _ctx.EnqueueDispose(() => {
            img?.Dispose();
            smp?.Dispose();
        });
    }
}
