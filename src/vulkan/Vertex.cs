using System.Numerics;
using Silk.NET.Vulkan;
using Silk.NET.Maths;

namespace Shiron.VulkanDumpster.Vulkan;

public struct Vertex {
    public Vector3D<float> Position;
    public Vector2D<float> TexCoord;

    public Vertex(Vector3D<float> position, Vector2D<float> texCoord) {
        Position = position;
        TexCoord = texCoord;
    }

    public static unsafe VertexInputBindingDescription GetBindingDescription() {
        return new VertexInputBindingDescription {
            Binding = 0,
            Stride = (uint)sizeof(Vertex),
            InputRate = VertexInputRate.Vertex
        };
    }

    public static unsafe VertexInputAttributeDescription[] GetAttributeDescriptions() {
        return new[] {
            new VertexInputAttributeDescription {
                Binding = 0,
                Location = 0,
                Format = Format.R32G32B32Sfloat,
                Offset = (uint)System.Runtime.InteropServices.Marshal.OffsetOf<Vertex>(nameof(Position))
            },
            new VertexInputAttributeDescription {
                Binding = 0,
                Location = 1,
                Format = Format.R32G32Sfloat,
                Offset = (uint)System.Runtime.InteropServices.Marshal.OffsetOf<Vertex>(nameof(TexCoord))
            }
        };
    }
}