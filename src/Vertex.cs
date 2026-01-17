
using System.Numerics;
using Silk.NET.Vulkan;

namespace Shiron.VulkanDumpster;

public struct Vertex(Vector3 position, Vector3 color) {
    public Vector3 Position { get; set; } = position;
    public Vector3 Color { get; set; } = color;

    public static unsafe VertexInputBindingDescription GetBindingDescription() {
        return new VertexInputBindingDescription {
            Binding = 0,
            Stride = (uint) sizeof(Vertex),
            InputRate = VertexInputRate.Vertex
        };
    }

    public static unsafe VertexInputAttributeDescription[] GetAttributeDescriptions() {
        return new[] {
            new VertexInputAttributeDescription {
                Binding = 0,
                Location= 0,
                Format=Format.R32G32B32Sfloat,
                Offset=0
            },
            new VertexInputAttributeDescription {
                Binding = 0,
                Location =1,
                Format =Format.R32G32B32Sfloat,
                Offset= (uint) sizeof(Vector3)
            }
        };
    }
}
