using Silk.NET.Vulkan;

namespace Shiron.VulkanDumpster;

/// <summary>
/// Provides utility functions for querying and retrieving queue family information
/// from a Vulkan physical device.
/// </summary>
public static unsafe class QueueFamilyUtils {
    /// <summary>
    /// Get all queue family properties from a physical device.
    /// </summary>
    /// <param name="vk">The Vulkan API instance.</param>
    /// <param name="physicalDevice">The physical device to query.</param>
    /// <returns>Array of queue family properties.</returns>
    public static QueueFamilyProperties[] GetAllQueueFamilies(Vk vk, PhysicalDevice physicalDevice) {
        uint queueFamilyCount = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, null);

        if (queueFamilyCount == 0)
            return [];

        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* pQueueFamilies = queueFamilies) {
            vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, pQueueFamilies);
        }

        return queueFamilies;
    }

    /// <summary>
    /// Get detailed information about all queue families including their indices.
    /// </summary>
    /// <param name="vk">The Vulkan API instance.</param>
    /// <param name="physicalDevice">The physical device to query.</param>
    /// <returns>Array of QueueFamilyInfo structs with index and properties.</returns>
    public static QueueFamilyInfo[] GetQueueFamilyInfos(Vk vk, PhysicalDevice physicalDevice) {
        var properties = GetAllQueueFamilies(vk, physicalDevice);
        var infos = new QueueFamilyInfo[properties.Length];

        for (uint i = 0; i < properties.Length; i++) {
            infos[i] = new QueueFamilyInfo {
                Index = i,
                Properties = properties[i]
            };
        }

        return infos;
    }

    /// <summary>
    /// Find the first queue family that supports the specified flags.
    /// </summary>
    /// <param name="vk">The Vulkan API instance.</param>
    /// <param name="physicalDevice">The physical device to query.</param>
    /// <param name="requiredFlags">The required queue flags.</param>
    /// <returns>The queue family index, or null if not found.</returns>
    public static uint? FindQueueFamily(Vk vk, PhysicalDevice physicalDevice, QueueFlags requiredFlags) {
        var families = GetAllQueueFamilies(vk, physicalDevice);

        for (uint i = 0; i < families.Length; i++) {
            if ((families[i].QueueFlags & requiredFlags) == requiredFlags) {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// Find all queue families that support the specified flags.
    /// </summary>
    /// <param name="vk">The Vulkan API instance.</param>
    /// <param name="physicalDevice">The physical device to query.</param>
    /// <param name="requiredFlags">The required queue flags.</param>
    /// <returns>Array of queue family indices that support the required flags.</returns>
    public static uint[] FindAllQueueFamilies(Vk vk, PhysicalDevice physicalDevice, QueueFlags requiredFlags) {
        var families = GetAllQueueFamilies(vk, physicalDevice);
        var results = new List<uint>();

        for (uint i = 0; i < families.Length; i++) {
            if ((families[i].QueueFlags & requiredFlags) == requiredFlags) {
                results.Add(i);
            }
        }

        return results.ToArray();
    }

    /// <summary>
    /// Find a dedicated queue family that supports ONLY the specified flags (no graphics).
    /// Useful for finding dedicated compute or transfer queues.
    /// </summary>
    /// <param name="vk">The Vulkan API instance.</param>
    /// <param name="physicalDevice">The physical device to query.</param>
    /// <param name="requiredFlags">The required queue flags.</param>
    /// <param name="excludeFlags">Flags that should NOT be present.</param>
    /// <returns>The queue family index, or null if not found.</returns>
    public static uint? FindDedicatedQueueFamily(Vk vk, PhysicalDevice physicalDevice,
        QueueFlags requiredFlags, QueueFlags excludeFlags = QueueFlags.None) {
        var families = GetAllQueueFamilies(vk, physicalDevice);

        for (uint i = 0; i < families.Length; i++) {
            var flags = families[i].QueueFlags;
            if ((flags & requiredFlags) == requiredFlags && (flags & excludeFlags) == 0) {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// Find a graphics queue family.
    /// </summary>
    public static uint? FindGraphicsQueueFamily(Vk vk, PhysicalDevice physicalDevice) {
        return FindQueueFamily(vk, physicalDevice, QueueFlags.GraphicsBit);
    }

    /// <summary>
    /// Find a compute queue family. Prefers dedicated compute queue (without graphics).
    /// </summary>
    /// <param name="vk">The Vulkan API instance.</param>
    /// <param name="physicalDevice">The physical device to query.</param>
    /// <param name="preferDedicated">If true, prefer a queue without graphics support.</param>
    public static uint? FindComputeQueueFamily(Vk vk, PhysicalDevice physicalDevice, bool preferDedicated = true) {
        if (preferDedicated) {
            // First try to find a dedicated compute queue (no graphics)
            var dedicated = FindDedicatedQueueFamily(vk, physicalDevice,
                QueueFlags.ComputeBit, QueueFlags.GraphicsBit);
            if (dedicated.HasValue) return dedicated;
        }

        // Fall back to any queue with compute support
        return FindQueueFamily(vk, physicalDevice, QueueFlags.ComputeBit);
    }

    /// <summary>
    /// Find a transfer queue family. Prefers dedicated transfer queue (without graphics or compute).
    /// </summary>
    /// <param name="vk">The Vulkan API instance.</param>
    /// <param name="physicalDevice">The physical device to query.</param>
    /// <param name="preferDedicated">If true, prefer a queue without graphics/compute support.</param>
    public static uint? FindTransferQueueFamily(Vk vk, PhysicalDevice physicalDevice, bool preferDedicated = true) {
        if (preferDedicated) {
            // First try to find a dedicated transfer queue (no graphics or compute)
            var dedicated = FindDedicatedQueueFamily(vk, physicalDevice,
                QueueFlags.TransferBit, QueueFlags.GraphicsBit | QueueFlags.ComputeBit);
            if (dedicated.HasValue) return dedicated;
        }

        // Fall back to any queue with transfer support
        return FindQueueFamily(vk, physicalDevice, QueueFlags.TransferBit);
    }

    /// <summary>
    /// Find a queue family that supports presentation to the given surface.
    /// </summary>
    /// <param name="vk">The Vulkan API instance.</param>
    /// <param name="physicalDevice">The physical device to query.</param>
    /// <param name="khrSurface">The KHR surface extension.</param>
    /// <param name="surface">The surface to check presentation support for.</param>
    /// <returns>The queue family index, or null if not found.</returns>
    public static uint? FindPresentQueueFamily(Vk vk, PhysicalDevice physicalDevice,
        Silk.NET.Vulkan.Extensions.KHR.KhrSurface khrSurface, SurfaceKHR surface) {
        var families = GetAllQueueFamilies(vk, physicalDevice);

        for (uint i = 0; i < families.Length; i++) {
            khrSurface.GetPhysicalDeviceSurfaceSupport(physicalDevice, i, surface, out var supported);
            if (supported) {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// Find a queue family that supports both graphics and presentation (common optimization).
    /// </summary>
    /// <param name="vk">The Vulkan API instance.</param>
    /// <param name="physicalDevice">The physical device to query.</param>
    /// <param name="khrSurface">The KHR surface extension.</param>
    /// <param name="surface">The surface to check presentation support for.</param>
    /// <returns>The queue family index, or null if not found.</returns>
    public static uint? FindGraphicsAndPresentQueueFamily(Vk vk, PhysicalDevice physicalDevice,
        Silk.NET.Vulkan.Extensions.KHR.KhrSurface khrSurface, SurfaceKHR surface) {
        var families = GetAllQueueFamilies(vk, physicalDevice);

        for (uint i = 0; i < families.Length; i++) {
            if (!families[i].QueueFlags.HasFlag(QueueFlags.GraphicsBit))
                continue;

            khrSurface.GetPhysicalDeviceSurfaceSupport(physicalDevice, i, surface, out var supported);
            if (supported) {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// Get the properties of a specific queue family by index.
    /// </summary>
    /// <param name="vk">The Vulkan API instance.</param>
    /// <param name="physicalDevice">The physical device to query.</param>
    /// <param name="familyIndex">The queue family index.</param>
    /// <returns>The queue family properties, or null if index is out of range.</returns>
    public static QueueFamilyProperties? GetQueueFamilyProperties(Vk vk, PhysicalDevice physicalDevice, uint familyIndex) {
        var families = GetAllQueueFamilies(vk, physicalDevice);

        if (familyIndex >= families.Length)
            return null;

        return families[familyIndex];
    }

    /// <summary>
    /// Get the number of queues available in a specific queue family.
    /// </summary>
    /// <param name="vk">The Vulkan API instance.</param>
    /// <param name="physicalDevice">The physical device to query.</param>
    /// <param name="familyIndex">The queue family index.</param>
    /// <returns>The number of queues, or 0 if index is out of range.</returns>
    public static uint GetQueueCount(Vk vk, PhysicalDevice physicalDevice, uint familyIndex) {
        var props = GetQueueFamilyProperties(vk, physicalDevice, familyIndex);
        return props?.QueueCount ?? 0;
    }

    /// <summary>
    /// Check if a queue family supports the specified flags.
    /// </summary>
    /// <param name="vk">The Vulkan API instance.</param>
    /// <param name="physicalDevice">The physical device to query.</param>
    /// <param name="familyIndex">The queue family index.</param>
    /// <param name="flags">The flags to check for.</param>
    /// <returns>True if the queue family supports all specified flags.</returns>
    public static bool QueueFamilySupports(Vk vk, PhysicalDevice physicalDevice, uint familyIndex, QueueFlags flags) {
        var props = GetQueueFamilyProperties(vk, physicalDevice, familyIndex);
        if (!props.HasValue) return false;
        return (props.Value.QueueFlags & flags) == flags;
    }

    /// <summary>
    /// Get a human-readable description of a queue family's capabilities.
    /// </summary>
    /// <param name="properties">The queue family properties.</param>
    /// <returns>A string describing the queue family capabilities.</returns>
    public static string GetQueueFamilyDescription(QueueFamilyProperties properties) {
        var capabilities = new List<string>();

        if (properties.QueueFlags.HasFlag(QueueFlags.GraphicsBit)) capabilities.Add("Graphics");
        if (properties.QueueFlags.HasFlag(QueueFlags.ComputeBit)) capabilities.Add("Compute");
        if (properties.QueueFlags.HasFlag(QueueFlags.TransferBit)) capabilities.Add("Transfer");
        if (properties.QueueFlags.HasFlag(QueueFlags.SparseBindingBit)) capabilities.Add("SparseBinding");
        if (properties.QueueFlags.HasFlag(QueueFlags.ProtectedBit)) capabilities.Add("Protected");
        if (properties.QueueFlags.HasFlag(QueueFlags.VideoDecodeBitKhr)) capabilities.Add("VideoDecode");
        if (properties.QueueFlags.HasFlag(QueueFlags.VideoEncodeBitKhr)) capabilities.Add("VideoEncode");
        if (properties.QueueFlags.HasFlag(QueueFlags.OpticalFlowBitNV)) capabilities.Add("OpticalFlow");

        if (capabilities.Count == 0)
            return "None";

        return string.Join(", ", capabilities);
    }

    /// <summary>
    /// Get a human-readable description of a queue family with its index.
    /// </summary>
    public static string GetQueueFamilyDescription(uint index, QueueFamilyProperties properties) {
        return $"Family {index}: {GetQueueFamilyDescription(properties)} ({properties.QueueCount} queue(s))";
    }

    /// <summary>
    /// Print all queue families to the console for debugging.
    /// </summary>
    public static void PrintQueueFamilies(Vk vk, PhysicalDevice physicalDevice) {
        var families = GetAllQueueFamilies(vk, physicalDevice);

        Console.WriteLine($"Queue Families ({families.Length} total):");
        for (uint i = 0; i < families.Length; i++) {
            Console.WriteLine($"  {GetQueueFamilyDescription(i, families[i])}");
        }
    }
}

/// <summary>
/// Contains information about a queue family including its index and properties.
/// </summary>
public struct QueueFamilyInfo {
    /// <summary>
    /// The index of this queue family.
    /// </summary>
    public uint Index;

    /// <summary>
    /// The properties of this queue family.
    /// </summary>
    public QueueFamilyProperties Properties;

    /// <summary>
    /// The number of queues available in this family.
    /// </summary>
    public readonly uint QueueCount => Properties.QueueCount;

    /// <summary>
    /// The capabilities of this queue family.
    /// </summary>
    public readonly QueueFlags Flags => Properties.QueueFlags;

    /// <summary>
    /// Check if this family supports graphics operations.
    /// </summary>
    public readonly bool SupportsGraphics => Properties.QueueFlags.HasFlag(QueueFlags.GraphicsBit);

    /// <summary>
    /// Check if this family supports compute operations.
    /// </summary>
    public readonly bool SupportsCompute => Properties.QueueFlags.HasFlag(QueueFlags.ComputeBit);

    /// <summary>
    /// Check if this family supports transfer operations.
    /// </summary>
    public readonly bool SupportsTransfer => Properties.QueueFlags.HasFlag(QueueFlags.TransferBit);

    /// <summary>
    /// Check if this family supports sparse binding operations.
    /// </summary>
    public readonly bool SupportsSparseBinding => Properties.QueueFlags.HasFlag(QueueFlags.SparseBindingBit);

    /// <summary>
    /// Get a human-readable description of this queue family.
    /// </summary>
    public readonly string Description => QueueFamilyUtils.GetQueueFamilyDescription(Index, Properties);

    public override readonly string ToString() => Description;
}
