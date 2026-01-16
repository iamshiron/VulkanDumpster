
using Silk.NET.Vulkan;

namespace Shiron.VulkanDumpster;

public sealed class VulkanException : Exception {
    public Result Result { get; }

    public VulkanException(string message, Result result) : base(message)
        => Result = result;
}
