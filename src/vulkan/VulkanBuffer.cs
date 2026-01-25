using System;
using System.Runtime.CompilerServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Shiron.VulkanDumpster.Vulkan;

/// <summary>
/// Encapsulates a Vulkan buffer and its backing memory.
/// Supports high-performance transfer using staging buffers.
/// </summary>
public unsafe class VulkanBuffer : IDisposable {
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;

    public Buffer Handle { get; private set; }
    public DeviceMemory Memory { get; private set; }
    public ulong Size { get; private set; }
    public void* MappedData { get; private set; }

    public VulkanBuffer(Vk vk, Device device, PhysicalDevice physicalDevice, ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties) {
        _vk = vk;
        _device = device;
        _physicalDevice = physicalDevice;
        Size = size;

        CreateBuffer(size, usage, properties, out var buffer, out var memory);
        Handle = buffer;
        Memory = memory;

        if (properties.HasFlag(MemoryPropertyFlags.HostVisibleBit)) {
            void* data;
            _vk.MapMemory(_device, Memory, 0, size, 0, &data);
            MappedData = data;
        }
    }

    private void CreateBuffer(ulong size, BufferUsageFlags usage, MemoryPropertyFlags properties, out Buffer buffer, out DeviceMemory memory) {
        var bufferInfo = new BufferCreateInfo {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = usage,
            SharingMode = SharingMode.Exclusive
        };

        if (_vk.CreateBuffer(_device, &bufferInfo, null, out buffer) != Result.Success) {
            throw new Exception("failed to create buffer!");
        }

        _vk.GetBufferMemoryRequirements(_device, buffer, out var memRequirements);

        var allocInfo = new MemoryAllocateInfo {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = memRequirements.Size,
            MemoryTypeIndex = FindMemoryType(memRequirements.MemoryTypeBits, properties)
        };

        if (_vk.AllocateMemory(_device, &allocInfo, null, out memory) != Result.Success) {
            throw new Exception("failed to allocate buffer memory!");
        }

        _vk.BindBufferMemory(_device, buffer, memory, 0);
    }

    private uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties) {
        _vk.GetPhysicalDeviceMemoryProperties(_physicalDevice, out var memProperties);

        for (int i = 0; i < memProperties.MemoryTypeCount; i++) {
            if ((typeFilter & (1 << i)) != 0 &&
                (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties) {
                return (uint) i;
            }
        }

        throw new Exception("failed to find suitable memory type!");
    }

    /// <summary>
    /// Uploads data to the buffer.
    /// If the buffer is host-visible, it maps and copies directly.
    /// If the buffer is device-local, it uses a staging buffer (requires a command pool and queue).
    /// </summary>
    public void UploadData<T>(T[] data, CommandPool commandPool, Queue queue) where T : unmanaged {
        ulong dataSize = (ulong)(sizeof(T) * data.Length);

        if (MappedData != null) {
            // Host visible - direct copy
            data.AsSpan().CopyTo(new Span<T>(MappedData, data.Length));
        } else {
            // Device local - use staging buffer
            using var stagingBuffer = new VulkanBuffer(_vk, _device, _physicalDevice, dataSize,
                BufferUsageFlags.TransferSrcBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);

            stagingBuffer.UploadData(data, commandPool, queue); // Recursive call, but hits the if branch

            CopyBuffer(stagingBuffer.Handle, Handle, dataSize, commandPool, queue);
        }
    }

    private void CopyBuffer(Buffer srcBuffer, Buffer dstBuffer, ulong size, CommandPool commandPool, Queue queue) {
        var allocInfo = new CommandBufferAllocateInfo {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = commandPool,
            CommandBufferCount = 1
        };

        _vk.AllocateCommandBuffers(_device, &allocInfo, out var commandBuffer);

        var beginInfo = new CommandBufferBeginInfo {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        _vk.BeginCommandBuffer(commandBuffer, &beginInfo);

        var copyRegion = new BufferCopy {
            SrcOffset = 0,
            DstOffset = 0,
            Size = size
        };

        _vk.CmdCopyBuffer(commandBuffer, srcBuffer, dstBuffer, 1, &copyRegion);

        _vk.EndCommandBuffer(commandBuffer);

        var submitInfo = new SubmitInfo {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer
        };

        _vk.QueueSubmit(queue, 1, &submitInfo, default);
        _vk.QueueWaitIdle(queue);

        _vk.FreeCommandBuffers(_device, commandPool, 1, &commandBuffer);
    }

    public void Dispose() {
        if (Handle.Handle != 0) {
            _vk.DestroyBuffer(_device, Handle, null);
            Handle = default;
        }
        if (Memory.Handle != 0) {
            _vk.FreeMemory(_device, Memory, null);
            Memory = default;
        }
    }
}
