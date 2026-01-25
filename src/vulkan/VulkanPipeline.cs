using Silk.NET.Vulkan;

namespace Shiron.VulkanDumpster.Vulkan;

public unsafe class VulkanPipeline : IDisposable {
    private readonly Vk _vk;
    private readonly Device _device;
    public Pipeline Handle { get; private set; }
    public PipelineLayout Layout { get; private set; }

    public VulkanPipeline(Vk vk, Device device, Pipeline handle, PipelineLayout layout) {
        _vk = vk;
        _device = device;
        Handle = handle;
        Layout = layout;
    }

    public void Dispose() {
        if (Handle.Handle != 0) {
            _vk.DestroyPipeline(_device, Handle, null);
        }
        if (Layout.Handle != 0) {
            _vk.DestroyPipelineLayout(_device, Layout, null);
        }
    }
}
