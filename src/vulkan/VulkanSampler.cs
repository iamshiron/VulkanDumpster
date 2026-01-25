using System;
using Silk.NET.Vulkan;

namespace Shiron.VulkanDumpster.Vulkan;

public unsafe class VulkanSampler : IDisposable {
    private readonly Vk _vk;
    private readonly Device _device;
    public Sampler Handle { get; private set; }

    public VulkanSampler(Vk vk, Device device, Filter magFilter = Filter.Linear, Filter minFilter = Filter.Linear, SamplerAddressMode addressMode = SamplerAddressMode.Repeat) {
        _vk = vk;
        _device = device;

        var samplerInfo = new SamplerCreateInfo {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = magFilter,
            MinFilter = minFilter,
            AddressModeU = addressMode,
            AddressModeV = addressMode,
            AddressModeW = addressMode,
            AnisotropyEnable = true,
            MaxAnisotropy = 16,
            BorderColor = BorderColor.IntOpaqueBlack,
            UnnormalizedCoordinates = false,
            CompareEnable = false,
            CompareOp = CompareOp.Always,
            MipmapMode = SamplerMipmapMode.Linear,
            MipLodBias = 0,
            MinLod = 0,
            MaxLod = 0
        };

        if (_vk.CreateSampler(_device, &samplerInfo, null, out var sampler) != Result.Success) {
            throw new Exception("failed to create sampler!");
        }

        Handle = sampler;
    }

    public void Dispose() {
        if (Handle.Handle != 0) {
            _vk.DestroySampler(_device, Handle, null);
        }
    }
}
