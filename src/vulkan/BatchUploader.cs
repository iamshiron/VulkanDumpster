using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Shiron.VulkanDumpster.Vulkan;

public unsafe class BatchUploader : IDisposable {
    private readonly VulkanContext _ctx;
    private readonly Renderer _renderer;
    private readonly VulkanBuffer[] _stagingBuffers;
    private const ulong DefaultStagingSize = 32 * 1024 * 1024; // 32 MB
    
    private byte* _mappedPtr;
    private ulong _currentOffset;
    private CommandPool _cmdPool;

    private struct CopyCmd {
        public Buffer Src;
        public Buffer Dst;
        public BufferCopy Region;
    }
    private readonly List<CopyCmd> _copyCmds = new();

    public BatchUploader(VulkanContext ctx, Renderer renderer) {
        _ctx = ctx;
        _renderer = renderer;
        _stagingBuffers = new VulkanBuffer[3];
        for (int i = 0; i < 3; i++) {
            _stagingBuffers[i] = new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
                DefaultStagingSize, BufferUsageFlags.TransferSrcBit, 
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }
        
        var poolInfo = new CommandPoolCreateInfo {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.TransientBit,
            QueueFamilyIndex = _ctx.QueueFamilies.GraphicsFamily!.Value
        };
        _ctx.Vk.CreateCommandPool(_ctx.Device, &poolInfo, null, out _cmdPool);
    }

    public void Begin() {
        _currentOffset = 0;
        int frameIndex = _renderer.CurrentFrameIndex;
        _mappedPtr = (byte*)_stagingBuffers[frameIndex].MappedData;
        _copyCmds.Clear();
    }

    public bool Upload<T>(ReadOnlySpan<T> data, VulkanBuffer targetBuffer, ulong targetOffset) where T : unmanaged {
        ulong size = (ulong)(data.Length * sizeof(T));
        
        ulong padding = (4 - (_currentOffset % 4)) % 4;
        int frameIndex = _renderer.CurrentFrameIndex;
        if (_currentOffset + padding + size > _stagingBuffers[frameIndex].Size) {
            return false;
        }
        
        _currentOffset += padding;
        
        fixed (void* pData = data) {
            System.Buffer.MemoryCopy(pData, _mappedPtr + _currentOffset, size, size);
        }

        _copyCmds.Add(new CopyCmd {
            Src = _stagingBuffers[frameIndex].Handle,
            Dst = targetBuffer.Handle,
            Region = new BufferCopy {
                SrcOffset = _currentOffset,
                DstOffset = targetOffset,
                Size = size
            }
        });

        _currentOffset += size;
        return true;
    }

    public void Flush() {
        if (_copyCmds.Count == 0) return;

        var allocInfo = new CommandBufferAllocateInfo {
            SType = StructureType.CommandBufferAllocateInfo,
            Level = CommandBufferLevel.Primary,
            CommandPool = _cmdPool,
            CommandBufferCount = 1
        };
        
        _ctx.Vk.AllocateCommandBuffers(_ctx.Device, &allocInfo, out var cmd);
        
        var begin = new CommandBufferBeginInfo {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        
        _ctx.Vk.BeginCommandBuffer(cmd, &begin);
        
        foreach (var copy in _copyCmds) {
            var region = copy.Region;
            _ctx.Vk.CmdCopyBuffer(cmd, copy.Src, copy.Dst, 1, &region);
        }

        // Single barrier for all copies
        var barrier = new BufferMemoryBarrier {
            SType = StructureType.BufferMemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.VertexAttributeReadBit | AccessFlags.IndexReadBit,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Buffer = default, 
            Offset = 0,
            Size = Vk.WholeSize
        };
        
        // Actually, a global memory barrier or just multiple barriers with no buffer handle is often faster
        // but for correctness let's just use one broad barrier here.
        _ctx.Vk.CmdPipelineBarrier(cmd, PipelineStageFlags.TransferBit, PipelineStageFlags.VertexInputBit, 0, 0, null, 0, null, 0, null);
        
        _ctx.Vk.EndCommandBuffer(cmd);
        
        var submit = new SubmitInfo {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd
        };
        
        _ctx.Vk.QueueSubmit(_ctx.GraphicsQueue, 1, &submit, default);
        
        var c = cmd;
        _ctx.EnqueueDispose(() => {
            var cmdToFree = c;
            _ctx.Vk.FreeCommandBuffers(_ctx.Device, _cmdPool, 1, &cmdToFree);
        });
    }

    public void Dispose() {
        _ctx.Vk.DestroyCommandPool(_ctx.Device, _cmdPool, null);
        foreach (var sb in _stagingBuffers) sb?.Dispose();
    }
}
