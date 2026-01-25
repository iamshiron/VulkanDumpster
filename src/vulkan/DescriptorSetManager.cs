using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Shiron.VulkanDumpster.Vulkan;

public unsafe class DescriptorSetManager : IDisposable {
    private readonly Vk _vk;
    private readonly Device _device;
    private DescriptorPool _pool;

    public DescriptorSetManager(Vk vk, Device device, uint maxSets, params DescriptorPoolSize[] poolSizes) {
        _vk = vk;
        _device = device;

        fixed (DescriptorPoolSize* pPoolSizes = poolSizes) {
            var poolInfo = new DescriptorPoolCreateInfo {
                SType = StructureType.DescriptorPoolCreateInfo,
                PoolSizeCount = (uint)poolSizes.Length,
                PPoolSizes = pPoolSizes,
                MaxSets = maxSets
            };

            if (_vk.CreateDescriptorPool(_device, &poolInfo, null, out _pool) != Result.Success) {
                throw new Exception("Failed to create descriptor pool.");
            }
        }
    }

    public DescriptorSet Allocate(DescriptorSetLayout layout) {
        var allocInfo = new DescriptorSetAllocateInfo {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _pool,
            DescriptorSetCount = 1,
            PSetLayouts = &layout
        };

        if (_vk.AllocateDescriptorSets(_device, &allocInfo, out var set) != Result.Success) {
            throw new Exception("Failed to allocate descriptor set.");
        }

        return set;
    }

    public void UpdateBuffer(DescriptorSet set, uint binding, DescriptorType type, Buffer buffer, ulong range, ulong offset = 0) {
        var bufferInfo = new DescriptorBufferInfo {
            Buffer = buffer,
            Offset = offset,
            Range = range
        };

        var write = new WriteDescriptorSet {
            SType = StructureType.WriteDescriptorSet,
            DstSet = set,
            DstBinding = binding,
            DstArrayElement = 0,
            DescriptorType = type,
            DescriptorCount = 1,
            PBufferInfo = &bufferInfo
        };

        _vk.UpdateDescriptorSets(_device, 1, &write, 0, null);
    }

    public void Dispose() {
        if (_pool.Handle != 0) {
            _vk.DestroyDescriptorPool(_device, _pool, null);
        }
    }
}
