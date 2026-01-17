
using Silk.NET.Vulkan;

namespace Shiron.VulkanDumpster;

/// <summary>
/// Exception that carries a Vulkan <see cref="Result"/> error code for easier debugging.
/// </summary>
public sealed class VulkanException : Exception {
    public Result Result { get; }

    public VulkanException(string message, Result result) : base(message)
        => Result = result;
}
