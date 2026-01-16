using Silk.NET.Core;
using Silk.NET.Core.Contexts;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;

namespace Shiron.VulkanDumpster;

public class Program {
    private static IWindow _window = null!;

    // Vulkan handles (mapped from C++ types)
    private static Vk _vk = null!;                          // Vulkan API
    private static Instance _instance;                       // VkInstance - Vulkan library handle
    private static InstanceBuilder _instanceBuilder = null!; // Manages instance + debug messenger (VkDebugUtilsMessengerEXT)
    private static PhysicalDevice _chosenGPU;               // VkPhysicalDevice - GPU chosen as the default device
    private static Device _device;                           // VkDevice - Vulkan device for commands
    private static SurfaceKHR _surface;                      // VkSurfaceKHR - Vulkan window surface

    // Builders (for proper disposal)
    private static PhysicalDeviceSelector _physicalDeviceSelector = null!;
    private static LogicalDeviceBuilder _logicalDeviceBuilder = null!;

    // Surface extension
    private static KhrSurface _khrSurface = null!;

    // Queues
    private static Queue _graphicsQueue;
    private static Queue _presentQueue;

    public static unsafe void Main(string[] args) {
        var options = WindowOptions.DefaultVulkan;
        options.Title = "Vulkan Dumpster Project";
        options.Size = new Vector2D<int>(1920, 1080);

        _window = Window.Create(options);
        _window.Initialize();

        _vk = Vk.GetApi();

        // Create Vulkan instance with validation layers
        _instanceBuilder = new InstanceBuilder(_vk)
            .WithApp("VulkanDumpster", new Version32(1, 0, 0))
            .WithEngine("NoEngine", new Version32(1, 0, 0))
            .WithApiVersion(Vk.Version13)
            .AddExtensions(GetRequiredExtensions())
            .EnableValidationLayers(enable: true);
        _instance = _instanceBuilder.Build();
        PrintInstanceInfo();

        // Create window surface
        _surface = _window.VkSurface!.Create<AllocationCallbacks>(_instance.ToHandle(), null).ToSurface();

        // Get surface extension for present support checking
        if (!_vk.TryGetInstanceExtension(_instance, out _khrSurface))
            throw new Exception("Failed to get KHR_surface extension.");

        // Select physical device (GPU)
        _physicalDeviceSelector = new PhysicalDeviceSelector(_vk, _instance)
            .PreferDeviceType(PhysicalDeviceType.DiscreteGpu)
            .AddRequiredExtensions(KhrSwapchain.ExtensionName)
            .RequireGraphicsQueue()
            .RequirePresentQueue(_surface, CheckPresentSupport);
        _chosenGPU = _physicalDeviceSelector.Select();
        PrintPhysicalDeviceInfo();

        // Create logical device
        _logicalDeviceBuilder = new LogicalDeviceBuilder(_vk, _physicalDeviceSelector)
            .AddExtensions(KhrSwapchain.ExtensionName)
            .AddGraphicsQueue()
            .AddPresentQueue();
        _device = _logicalDeviceBuilder.Build();

        // Get queues
        _graphicsQueue = _logicalDeviceBuilder.GetGraphicsQueue();
        _presentQueue = _logicalDeviceBuilder.GetPresentQueue();
        PrintLogicalDeviceInfo();

        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("  ✓ Vulkan initialized successfully!");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        _window.Load += OnLoad;
        _window.Render += Render;

        _window.Run();

        Cleanup();
    }

    private static unsafe string[] GetRequiredExtensions() {
        // Get extensions required by the windowing system
        var windowExtensions = _window.VkSurface!.GetRequiredExtensions(out var count);
        var extensions = new string[count];
        for (var i = 0; i < count; i++) {
            extensions[i] = System.Runtime.InteropServices.Marshal.PtrToStringAnsi((nint) windowExtensions[i]) ?? "";
        }
        return extensions;
    }

    private static unsafe bool CheckPresentSupport(PhysicalDevice device, uint queueFamilyIndex) {
        _khrSurface.GetPhysicalDeviceSurfaceSupport(device, queueFamilyIndex, _surface, out var supported);
        return supported;
    }

    private static void OnLoad() {
        Console.WriteLine("Window loaded");
        var inputContext = _window.CreateInput();

        foreach (var kb in inputContext.Keyboards) {
            kb.KeyDown += KeyDown;
        }
    }

    private static unsafe void Cleanup() {
        // Wait for device to finish before cleanup
        _vk.DeviceWaitIdle(_device);

        _logicalDeviceBuilder.Dispose();
        _khrSurface.DestroySurface(_instance, _surface, null);
        _instanceBuilder.Dispose();
        _window.Dispose();
    }

    private static void KeyDown(IKeyboard keyboard, Key key, int arg3) {
        if (key == Key.Escape) {
            _window.Close();
        }
    }

    private static void Render(double delta) {
    }

    #region Debug Print Methods

    private static unsafe void PrintInstanceInfo() {
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("                    VULKAN INSTANCE INFO");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        // Get instance version
        uint apiVersion = 0;
        _vk.EnumerateInstanceVersion(ref apiVersion);
        var major = apiVersion >> 22;
        var minor = (apiVersion >> 12) & 0x3FF;
        var patch = apiVersion & 0xFFF;
        Console.WriteLine($"  Instance API Version: {major}.{minor}.{patch}");

        // Requested API version
        Console.WriteLine($"  Requested API Version: 1.3 (Vk.Version13)");

        // Get enabled extensions from window surface requirements
        var extensions = GetRequiredExtensions();
        Console.WriteLine($"  Enabled Extensions ({extensions.Length + 1}):");
        foreach (var ext in extensions) {
            Console.WriteLine($"    • {ext}");
        }
        Console.WriteLine($"    • VK_EXT_debug_utils (validation)");

        Console.WriteLine($"  Validation Layers: Enabled");
        Console.WriteLine($"    • VK_LAYER_KHRONOS_validation");
        Console.WriteLine();
    }

    private static unsafe void PrintPhysicalDeviceInfo() {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("                   PHYSICAL DEVICE INFO");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        var props = _physicalDeviceSelector.Properties;
        var memProps = _physicalDeviceSelector.MemoryProperties;
        var features = _physicalDeviceSelector.Features;
        var queueFamilies = _physicalDeviceSelector.QueueFamilies;

        // Basic info
        Console.WriteLine($"  Device Name: {_physicalDeviceSelector.GetDeviceName()}");
        Console.WriteLine($"  Device Type: {props.DeviceType}");

        // API version supported
        var apiVersion = props.ApiVersion;
        var major = apiVersion >> 22;
        var minor = (apiVersion >> 12) & 0x3FF;
        var patch = apiVersion & 0xFFF;
        Console.WriteLine($"  API Version: {major}.{minor}.{patch}");

        // Driver version (vendor-specific encoding)
        var driverVersion = props.DriverVersion;
        Console.WriteLine($"  Driver Version: {driverVersion >> 22}.{(driverVersion >> 12) & 0x3FF}.{driverVersion & 0xFFF}");

        // Vendor ID
        var vendorName = props.VendorID switch {
            0x1002 => "AMD",
            0x1010 => "ImgTec",
            0x10DE => "NVIDIA",
            0x13B5 => "ARM",
            0x5143 => "Qualcomm",
            0x8086 => "Intel",
            _ => $"Unknown (0x{props.VendorID:X4})"
        };
        Console.WriteLine($"  Vendor: {vendorName}");
        Console.WriteLine($"  Device ID: 0x{props.DeviceID:X4}");

        // Limits
        Console.WriteLine();
        Console.WriteLine("  Key Limits:");
        Console.WriteLine($"    Max Image Dimension 2D: {props.Limits.MaxImageDimension2D}");
        Console.WriteLine($"    Max Uniform Buffer Range: {props.Limits.MaxUniformBufferRange / 1024} KB");
        Console.WriteLine($"    Max Storage Buffer Range: {props.Limits.MaxStorageBufferRange / (1024 * 1024)} MB");
        Console.WriteLine($"    Max Push Constants Size: {props.Limits.MaxPushConstantsSize} bytes");
        Console.WriteLine($"    Max Memory Allocation Count: {props.Limits.MaxMemoryAllocationCount}");
        Console.WriteLine($"    Max Bound Descriptor Sets: {props.Limits.MaxBoundDescriptorSets}");
        Console.WriteLine($"    Max Vertex Input Attributes: {props.Limits.MaxVertexInputAttributes}");
        Console.WriteLine($"    Max Framebuffer Width: {props.Limits.MaxFramebufferWidth}");
        Console.WriteLine($"    Max Framebuffer Height: {props.Limits.MaxFramebufferHeight}");

        // Memory info
        Console.WriteLine();
        Console.WriteLine($"  Memory Heaps ({memProps.MemoryHeapCount}):");
        for (uint i = 0; i < memProps.MemoryHeapCount; i++) {
            var heap = memProps.MemoryHeaps[(int) i];
            var sizeMB = heap.Size / (1024 * 1024);
            var sizeGB = sizeMB / 1024.0;
            var flags = heap.Flags.HasFlag(MemoryHeapFlags.DeviceLocalBit) ? "Device Local" : "Host";
            Console.WriteLine($"    Heap {i}: {(sizeGB >= 1 ? $"{sizeGB:F2} GB" : $"{sizeMB} MB")} [{flags}]");
        }

        Console.WriteLine();
        Console.WriteLine($"  Memory Types ({memProps.MemoryTypeCount}):");
        for (uint i = 0; i < memProps.MemoryTypeCount; i++) {
            var memType = memProps.MemoryTypes[(int) i];
            var flagsList = new List<string>();
            if (memType.PropertyFlags.HasFlag(MemoryPropertyFlags.DeviceLocalBit)) flagsList.Add("DeviceLocal");
            if (memType.PropertyFlags.HasFlag(MemoryPropertyFlags.HostVisibleBit)) flagsList.Add("HostVisible");
            if (memType.PropertyFlags.HasFlag(MemoryPropertyFlags.HostCoherentBit)) flagsList.Add("HostCoherent");
            if (memType.PropertyFlags.HasFlag(MemoryPropertyFlags.HostCachedBit)) flagsList.Add("HostCached");
            Console.WriteLine($"    Type {i}: Heap {memType.HeapIndex} [{string.Join(", ", flagsList)}]");
        }

        // Key features
        Console.WriteLine();
        Console.WriteLine("  Key Features:");
        Console.WriteLine($"    Geometry Shader: {(features.GeometryShader ? "✓" : "✗")}");
        Console.WriteLine($"    Tessellation Shader: {(features.TessellationShader ? "✓" : "✗")}");
        Console.WriteLine($"    Sampler Anisotropy: {(features.SamplerAnisotropy ? "✓" : "✗")}");
        Console.WriteLine($"    Multi Viewport: {(features.MultiViewport ? "✓" : "✗")}");
        Console.WriteLine($"    Wide Lines: {(features.WideLines ? "✓" : "✗")}");
        Console.WriteLine($"    Fill Mode Non-Solid: {(features.FillModeNonSolid ? "✓" : "✗")}");
        Console.WriteLine($"    Multi Draw Indirect: {(features.MultiDrawIndirect ? "✓" : "✗")}");
        Console.WriteLine($"    Shader Float64: {(features.ShaderFloat64 ? "✓" : "✗")}");
        Console.WriteLine($"    Shader Int64: {(features.ShaderInt64 ? "✓" : "✗")}");

        // Queue families
        Console.WriteLine();
        Console.WriteLine("  Queue Families:");
        Console.WriteLine($"    Graphics: {(queueFamilies.GraphicsFamily.HasValue ? $"Family {queueFamilies.GraphicsFamily.Value}" : "Not found")}");
        Console.WriteLine($"    Compute:  {(queueFamilies.ComputeFamily.HasValue ? $"Family {queueFamilies.ComputeFamily.Value}" : "Not found")}");
        Console.WriteLine($"    Transfer: {(queueFamilies.TransferFamily.HasValue ? $"Family {queueFamilies.TransferFamily.Value}" : "Not found")}");
        Console.WriteLine($"    Present:  {(queueFamilies.PresentFamily.HasValue ? $"Family {queueFamilies.PresentFamily.Value}" : "Not found")}");
        Console.WriteLine();
    }

    private static void PrintLogicalDeviceInfo() {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("                    LOGICAL DEVICE INFO");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");

        var queueFamilies = _physicalDeviceSelector.QueueFamilies;

        Console.WriteLine($"  Device Handle: 0x{_device.Handle:X16}");
        Console.WriteLine();
        Console.WriteLine("  Enabled Extensions:");
        Console.WriteLine($"    • {KhrSwapchain.ExtensionName}");
        Console.WriteLine();
        Console.WriteLine("  Created Queues:");
        Console.WriteLine($"    Graphics Queue: Handle 0x{_graphicsQueue.Handle:X16} (Family {queueFamilies.GraphicsFamily})");
        Console.WriteLine($"    Present Queue:  Handle 0x{_presentQueue.Handle:X16} (Family {queueFamilies.PresentFamily})");

        if (queueFamilies.GraphicsFamily == queueFamilies.PresentFamily) {
            Console.WriteLine("    (Graphics and Present share the same queue family)");
        }
        Console.WriteLine();
    }

    #endregion
}
