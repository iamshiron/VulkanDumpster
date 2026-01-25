using System;
using Silk.NET.Vulkan;

namespace Shiron.VulkanDumpster.Vulkan;

public class TextureArray : IDisposable {
    private readonly VulkanContext _ctx;
    public VulkanImage Image { get; private set; }
    public VulkanSampler Sampler { get; private set; }

    public TextureArray(VulkanContext ctx, uint width, uint height, byte[][] pixelsList, Filter magFilter = Filter.Nearest, Filter minFilter = Filter.Nearest) {
        _ctx = ctx;
        uint layerCount = (uint)pixelsList.Length;
        ulong layerSize = (ulong)(width * height * 4);
        ulong totalSize = layerSize * layerCount;

        // One big staging buffer for all layers
        using var stagingBuffer = new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
            totalSize, BufferUsageFlags.TransferSrcBit,
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

        unsafe {
            byte* pStart = (byte*)stagingBuffer.MappedData;
            for (int i = 0; i < layerCount; i++) {
                fixed (byte* pData = pixelsList[i]) {
                    System.Buffer.MemoryCopy(pData, pStart + ((long)i * (long)layerSize), layerSize, layerSize);
                }
            }
        }

        Image = new VulkanImage(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
            width, height, layerCount, Format.R8G8B8A8Srgb,
            ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            MemoryPropertyFlags.DeviceLocalBit, ImageAspectFlags.ColorBit);

        TransitionAndCopy(stagingBuffer.Handle, width, height, layerCount);
        Sampler = new VulkanSampler(_ctx.Vk, _ctx.Device, magFilter, minFilter);
    }

    private unsafe void TransitionAndCopy(Silk.NET.Vulkan.Buffer stagingBuffer, uint width, uint height, uint layerCount) {
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

        ImageUtils.TransitionImage(_ctx.Vk, commandBuffer, Image.Handle, 
            ImageLayout.Undefined, ImageLayout.TransferDstOptimal);

        var region = new BufferImageCopy {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, layerCount),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(width, height, 1)
        };

        _ctx.Vk.CmdCopyBufferToImage(commandBuffer, stagingBuffer, Image.Handle, 
            ImageLayout.TransferDstOptimal, 1, &region);

        ImageUtils.TransitionImage(_ctx.Vk, commandBuffer, Image.Handle, 
            ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal);

        _ctx.Vk.EndCommandBuffer(commandBuffer);

        var submitInfo = new SubmitInfo {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        _ctx.Vk.QueueSubmit(_ctx.GraphicsQueue, 1, &submitInfo, default);
        _ctx.Vk.QueueWaitIdle(_ctx.GraphicsQueue);

        _ctx.Vk.FreeCommandBuffers(_ctx.Device, _ctx.CommandPool, 1, &commandBuffer);
    }

    public void Dispose() {
        Image.Dispose();
        Sampler.Dispose();
    }
}
