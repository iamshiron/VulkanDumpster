using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
namespace Shiron.VulkanDumpster.Vulkan;
/// <summary>
/// Utility class for selecting a suitable physical device (GPU) based on specified criteria.
/// </summary>
public sealed unsafe class PhysicalDeviceSelector {
    private readonly Vk _vk;
    private readonly Instance _instance;
    private PhysicalDeviceType _preferredType = PhysicalDeviceType.DiscreteGpu;
    private readonly List<string> _requiredExtensions = new();
    private bool _requireGraphicsQueue = true;
    private bool _requireComputeQueue;
    private bool _requireTransferQueue;
    private bool _requirePresentQueue;
    private SurfaceKHR _surface;
    private Func<PhysicalDevice, uint, bool>? _presentSupportChecker;
    private PhysicalDevice _selectedDevice;
    private PhysicalDeviceProperties _deviceProperties;
    private PhysicalDeviceFeatures _deviceFeatures;
    private PhysicalDeviceMemoryProperties _memoryProperties;
    private QueueFamilyIndices _queueFamilyIndices;
    private bool _selected;
    public PhysicalDeviceSelector(Vk vk, Instance instance) {
        _vk = vk ?? throw new ArgumentNullException(nameof(vk));
        if (instance.Handle == 0) throw new ArgumentException("Invalid Vulkan instance.", nameof(instance));
        _instance = instance;
    }
    public PhysicalDevice PhysicalDevice => _selectedDevice;
    public PhysicalDeviceProperties Properties => _deviceProperties;
    public PhysicalDeviceFeatures Features => _deviceFeatures;
    public PhysicalDeviceMemoryProperties MemoryProperties => _memoryProperties;
    public QueueFamilyIndices QueueFamilies => _queueFamilyIndices;
    /// <summary>
    /// Set the preferred physical device type. Defaults to DiscreteGpu.
    /// </summary>
    public PhysicalDeviceSelector PreferDeviceType(PhysicalDeviceType type) {
        _preferredType = type;
        return this;
    }
    /// <summary>
    /// Add required device extensions (e.g. VK_KHR_swapchain).
    /// </summary>
    public PhysicalDeviceSelector AddRequiredExtensions(params string[] extensions) {
        foreach (var e in extensions.Where(s => !string.IsNullOrWhiteSpace(s)))
            if (!_requiredExtensions.Contains(e)) _requiredExtensions.Add(e);
        return this;
    }
    /// <summary>
    /// Require graphics queue family support. Enabled by default.
    /// </summary>
    public PhysicalDeviceSelector RequireGraphicsQueue(bool require = true) {
        _requireGraphicsQueue = require;
        return this;
    }
    /// <summary>
    /// Require dedicated compute queue family support.
    /// </summary>
    public PhysicalDeviceSelector RequireComputeQueue(bool require = true) {
        _requireComputeQueue = require;
        return this;
    }
    /// <summary>
    /// Require dedicated transfer queue family support.
    /// </summary>
    public PhysicalDeviceSelector RequireTransferQueue(bool require = true) {
        _requireTransferQueue = require;
        return this;
    }
    /// <summary>
    /// Require present queue family support for the given surface.
    /// Provide a callback that checks surface support (typically using KhrSurface.GetPhysicalDeviceSurfaceSupport).
    /// </summary>
    /// <param name="surface">The surface to check presentation support for.</param>
    /// <param name="presentSupportChecker">Callback that takes (physicalDevice, queueFamilyIndex) and returns true if presentation is supported.</param>
    public PhysicalDeviceSelector RequirePresentQueue(SurfaceKHR surface, Func<PhysicalDevice, uint, bool> presentSupportChecker) {
        _requirePresentQueue = true;
        _surface = surface;
        _presentSupportChecker = presentSupportChecker;
        return this;
    }
    /// <summary>
    /// Require present queue family support. Uses a simple heuristic (assumes graphics queues support presentation).
    /// For accurate detection, use the overload with presentSupportChecker callback.
    /// </summary>
    public PhysicalDeviceSelector RequirePresentQueue(bool require = true) {
        _requirePresentQueue = require;
        return this;
    }
    /// <summary>
    /// Select the best matching physical device based on the specified criteria.
    /// </summary>
    public PhysicalDevice Select() {
        if (_selected) throw new InvalidOperationException("PhysicalDeviceSelector.Select() can only be called once.");
        _selected = true;
        uint deviceCount = 0;
        _vk.EnumeratePhysicalDevices(_instance, &deviceCount, null);
        if (deviceCount == 0)
            throw new VulkanException("No Vulkan-capable physical devices found.", Result.ErrorInitializationFailed);
        var devices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* pDevices = devices) {
            _vk.EnumeratePhysicalDevices(_instance, &deviceCount, pDevices);
        }
        var scoredDevices = new List<(PhysicalDevice device, int score, QueueFamilyIndices indices)>();
        foreach (var device in devices) {
            if (IsDeviceSuitable(device, out var indices, out var score)) {
                scoredDevices.Add((device, score, indices));
            }
        }
        if (scoredDevices.Count == 0)
            throw new VulkanException("No suitable physical device found matching the requirements.", Result.ErrorInitializationFailed);
        // Select the device with highest score
        var best = scoredDevices.OrderByDescending(d => d.score).First();
        _selectedDevice = best.device;
        _queueFamilyIndices = best.indices;
        // Cache device properties
        _vk.GetPhysicalDeviceProperties(_selectedDevice, out _deviceProperties);
        _vk.GetPhysicalDeviceFeatures(_selectedDevice, out _deviceFeatures);
        _vk.GetPhysicalDeviceMemoryProperties(_selectedDevice, out _memoryProperties);
        return _selectedDevice;
    }
    private bool IsDeviceSuitable(PhysicalDevice device, out QueueFamilyIndices indices, out int score) {
        indices = default;
        score = 0;
        _vk.GetPhysicalDeviceProperties(device, out var properties);
        _vk.GetPhysicalDeviceFeatures(device, out var features);
        // Check required extensions
        if (!CheckDeviceExtensionSupport(device))
            return false;
        // Find queue families
        indices = FindQueueFamilies(device);
        if (_requireGraphicsQueue && !indices.GraphicsFamily.HasValue)
            return false;
        if (_requireComputeQueue && !indices.ComputeFamily.HasValue)
            return false;
        if (_requireTransferQueue && !indices.TransferFamily.HasValue)
            return false;
        if (_requirePresentQueue && !indices.PresentFamily.HasValue)
            return false;
        // Score the device
        score = ScoreDevice(properties, features);
        return score > 0;
    }
    private bool CheckDeviceExtensionSupport(PhysicalDevice device) {
        if (_requiredExtensions.Count == 0) return true;
        uint extensionCount = 0;
        _vk.EnumerateDeviceExtensionProperties(device, (byte*) null, &extensionCount, null);
        var availableExtensions = new ExtensionProperties[extensionCount];
        fixed (ExtensionProperties* pExtensions = availableExtensions) {
            _vk.EnumerateDeviceExtensionProperties(device, (byte*) null, &extensionCount, pExtensions);
        }
        var availableNames = availableExtensions
            .Select(e => SilkMarshal.PtrToString((nint) e.ExtensionName, NativeStringEncoding.UTF8))
            .ToHashSet();
        return _requiredExtensions.All(ext => availableNames.Contains(ext));
    }
    private QueueFamilyIndices FindQueueFamilies(PhysicalDevice device) {
        var indices = new QueueFamilyIndices();
        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, null);
        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* pQueueFamilies = queueFamilies) {
            _vk.GetPhysicalDeviceQueueFamilyProperties(device, &queueFamilyCount, pQueueFamilies);
        }
        for (uint i = 0; i < queueFamilyCount; i++) {
            var queueFamily = queueFamilies[i];
            // Graphics queue
            if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit) && !indices.GraphicsFamily.HasValue) {
                indices.GraphicsFamily = i;
            }
            // Compute queue (prefer dedicated)
            if (queueFamily.QueueFlags.HasFlag(QueueFlags.ComputeBit)) {
                if (!indices.ComputeFamily.HasValue ||
                    !queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit)) {
                    indices.ComputeFamily = i;
                }
            }
            // Transfer queue (prefer dedicated)
            if (queueFamily.QueueFlags.HasFlag(QueueFlags.TransferBit)) {
                if (!indices.TransferFamily.HasValue ||
                    (!queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit) &&
                     !queueFamily.QueueFlags.HasFlag(QueueFlags.ComputeBit))) {
                    indices.TransferFamily = i;
                }
            }
            // Present queue
            if (_requirePresentQueue && !indices.PresentFamily.HasValue) {
                if (_presentSupportChecker != null) {
                    // Use provided callback to check surface support
                    if (_presentSupportChecker(device, i)) {
                        indices.PresentFamily = i;
                    }
                } else {
                    // Heuristic: assume graphics queues support presentation (common case)
                    if (queueFamily.QueueFlags.HasFlag(QueueFlags.GraphicsBit)) {
                        indices.PresentFamily = i;
                    }
                }
            }
        }
        return indices;
    }
    private int ScoreDevice(PhysicalDeviceProperties properties, PhysicalDeviceFeatures features) {
        int score = 0;
        // Prefer the specified device type
        if (properties.DeviceType == _preferredType)
            score += 10000;
        // Base score by device type
        score += properties.DeviceType switch {
            PhysicalDeviceType.DiscreteGpu => 1000,
            PhysicalDeviceType.IntegratedGpu => 500,
            PhysicalDeviceType.VirtualGpu => 200,
            PhysicalDeviceType.Cpu => 100,
            _ => 50
        };
        // Bonus for max image dimensions (indicates capability)
        score += (int) properties.Limits.MaxImageDimension2D / 1000;
        return score;
    }
    /// <summary>
    /// Get the device name of the selected physical device.
    /// </summary>
    public string GetDeviceName() {
        if (!_selected)
            throw new InvalidOperationException("No device selected. Call Select() first.");
        fixed (byte* pName = _deviceProperties.DeviceName) {
            return SilkMarshal.PtrToString((nint) pName, NativeStringEncoding.UTF8) ?? "Unknown";
        }
    }
    /// <summary>
    /// Get available device extensions for the selected physical device.
    /// </summary>
    public string[] GetAvailableExtensions() {
        if (!_selected)
            throw new InvalidOperationException("No device selected. Call Select() first.");
        uint extensionCount = 0;
        _vk.EnumerateDeviceExtensionProperties(_selectedDevice, (byte*) null, &extensionCount, null);
        var extensions = new ExtensionProperties[extensionCount];
        fixed (ExtensionProperties* pExtensions = extensions) {
            _vk.EnumerateDeviceExtensionProperties(_selectedDevice, (byte*) null, &extensionCount, pExtensions);
        }
        return extensions
            .Select(e => SilkMarshal.PtrToString((nint) e.ExtensionName, NativeStringEncoding.UTF8) ?? "")
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }
}
/// <summary>
/// Stores the indices of queue families found on a physical device.
/// </summary>
public struct QueueFamilyIndices {
    /// <summary>
    /// Queue family that supports graphics commands (draw calls, pipeline binding, etc.).
    /// </summary>
    public uint? GraphicsFamily;
    /// <summary>
    /// Queue family that supports compute dispatches.
    /// </summary>
    public uint? ComputeFamily;
    /// <summary>
    /// Queue family that supports transfer (copy) commands.
    /// </summary>
    public uint? TransferFamily;
    /// <summary>
    /// Queue family that can present images to a window surface.
    /// </summary>
    public uint? PresentFamily;
    /// <summary>
    /// Returns true if required queues are available for the current app needs.
    /// </summary>
    public readonly bool IsComplete(bool requirePresent = false) {
        return GraphicsFamily.HasValue &&
               (!requirePresent || PresentFamily.HasValue);
    }
    /// <summary>
    /// Get all unique queue family indices.
    /// </summary>
    public readonly uint[] GetUniqueIndices() {
        var indices = new HashSet<uint>();
        if (GraphicsFamily.HasValue) indices.Add(GraphicsFamily.Value);
        if (ComputeFamily.HasValue) indices.Add(ComputeFamily.Value);
        if (TransferFamily.HasValue) indices.Add(TransferFamily.Value);
        if (PresentFamily.HasValue) indices.Add(PresentFamily.Value);
        return indices.ToArray();
    }
}
