using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

namespace Shiron.VulkanDumpster.Vulkan;

/// <summary>
/// Manages the core Vulkan context: Instance, Device, Surface, and Queues.
/// </summary>
public unsafe class VulkanContext : IDisposable {
    public Vk Vk { get; private set; }
    public Instance Instance { get; private set; }
    public Device Device { get; private set; }
    public PhysicalDevice PhysicalDevice { get; private set; }
    public SurfaceKHR Surface { get; private set; }
    public KhrSurface KhrSurface { get; private set; } = null!;
    public KhrSwapchain KhrSwapchain { get; private set; } = null!;

    public Queue GraphicsQueue { get; private set; }
    public Queue PresentQueue { get; private set; }
    public uint GraphicsQueueFamily { get; private set; }
    public QueueFamilyIndices QueueFamilies { get; private set; }

    public CommandPool CommandPool { get; private set; }

    private InstanceBuilder _instanceBuilder = null!;
    private PhysicalDeviceSelector _physicalDeviceSelector = null!;
    private LogicalDeviceBuilder _logicalDeviceBuilder = null!;
    private IWindow _window;

    public VulkanContext(IWindow window) {
        _window = window;
        Vk = Vk.GetApi();
        
        InitInstance();
        InitSurface();
        InitDevice();
        InitCommandPool();
    }

    private void InitInstance() {
        _instanceBuilder = new InstanceBuilder(Vk)
            .WithApp("VulkanDumpster", new Version32(1, 0, 0))
            .WithApiVersion(Vk.Version13)
            .EnableValidationLayers(enable: true);
        
        var windowExtensions = _window.VkSurface!.GetRequiredExtensions(out var extCount);
        var extensions = new string[extCount];
        for (var i = 0; i < extCount; i++) {
            extensions[i] = System.Runtime.InteropServices.Marshal.PtrToStringAnsi((nint)windowExtensions[i]);
        }
        _instanceBuilder.AddExtensions(extensions);

        Instance = _instanceBuilder.Build();
    }

    private void InitSurface() {
        Surface = _window.VkSurface!.Create<AllocationCallbacks>(Instance.ToHandle(), null).ToSurface();
        KhrSurface khrSurface;
        if (!Vk.TryGetInstanceExtension(Instance, out khrSurface)) {
            throw new Exception("Failed to get KHR_surface extension.");
        }
        KhrSurface = khrSurface;
    }

    private void InitDevice() {
        _physicalDeviceSelector = new PhysicalDeviceSelector(Vk, Instance)
            .PreferDeviceType(PhysicalDeviceType.DiscreteGpu)
            .AddRequiredExtensions(KhrSwapchain.ExtensionName)
            .RequireGraphicsQueue()
            .RequirePresentQueue(Surface, (device, queueFamilyIndex) => {
                KhrSurface.GetPhysicalDeviceSurfaceSupport(device, queueFamilyIndex, Surface, out var supported);
                return supported;
            });

        PhysicalDevice = _physicalDeviceSelector.Select();
        QueueFamilies = _physicalDeviceSelector.QueueFamilies;

        _logicalDeviceBuilder = new LogicalDeviceBuilder(Vk, _physicalDeviceSelector)
            .AddExtensions(KhrSwapchain.ExtensionName)
            .AddGraphicsQueue()
            .AddPresentQueue();

        Device = _logicalDeviceBuilder.Build();
        GraphicsQueue = _logicalDeviceBuilder.GetGraphicsQueue();
        PresentQueue = _logicalDeviceBuilder.GetPresentQueue();
        GraphicsQueueFamily = _physicalDeviceSelector.QueueFamilies.GraphicsFamily!.Value;

        KhrSwapchain khrSwapchain;
        if (!Vk.TryGetDeviceExtension(Instance, Device, out khrSwapchain)) {
            throw new Exception("Failed to get KHR_swapchain extension.");
        }
        KhrSwapchain = khrSwapchain;
    }

    private void InitCommandPool() {
        var poolInfo = new CommandPoolCreateInfo {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = GraphicsQueueFamily
        };

        if (Vk.CreateCommandPool(Device, &poolInfo, null, out var pool) != Result.Success) {
            throw new Exception("Failed to create command pool.");
        }
        CommandPool = pool;
    }

    public void Dispose() {
        if (CommandPool.Handle != 0) {
            Vk.DestroyCommandPool(Device, CommandPool, null);
        }

        _logicalDeviceBuilder?.Dispose();
        
        if (Surface.Handle != 0) {
            KhrSurface.DestroySurface(Instance, Surface, null);
        }

        _instanceBuilder?.Dispose();
        Vk.Dispose();
    }
}