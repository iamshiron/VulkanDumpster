using Shiron.VulkanDumpster.Vulkan;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Shiron.VulkanDumpster;
namespace Shiron.VulkanDumpster.Voxels;
/// <summary>
/// Represents a vertical stack of chunks (a chunk column).
/// </summary>
public class YChunk : IDisposable {
    public const int HeightInChunks = 8; // Total height: 8 * 32 = 256 blocks
    public const int TotalHeight = HeightInChunks * Chunk.Size;
    private readonly Chunk[] _chunks = new Chunk[HeightInChunks];
    private readonly Vector2D<int> _chunkPos; // X, Z position in chunk coordinates
    public YChunk(VulkanContext ctx, World world, Vector2D<int> chunkPos) {
        _chunkPos = chunkPos;
        for (int y = 0; y < HeightInChunks; y++) {
            var worldPos = new Vector3D<float>(chunkPos.X * Chunk.Size, y * Chunk.Size, chunkPos.Y * Chunk.Size);
            _chunks[y] = new Chunk(ctx, world, worldPos);
        }
    }
    public void SetBlock(int x, int y, int z, BlockType type) {
        if (y < 0 || y >= TotalHeight) return;
        int chunkY = y / Chunk.Size;
        int localY = y % Chunk.Size;
        _chunks[chunkY].SetBlock(x, localY, z, type);
    }
    public BlockType GetBlock(int x, int y, int z) {
        if (y < 0 || y >= TotalHeight) return BlockType.Air;
        int chunkY = y / Chunk.Size;
        int localY = y % Chunk.Size;
        return _chunks[chunkY].GetBlock(x, localY, z);
    }
    public void MarkDirty() {
        for (int i = 0; i < HeightInChunks; i++) {
            _chunks[i].MarkDirty();
        }
    }
    public void Update() {
        // if (!_isDirty) return; // Optimization removed: Chunks need to poll for async task completion
        for (int i = 0; i < HeightInChunks; i++) {
            _chunks[i].Update();
        }
    }
    public void Render(VulkanCommandBuffer cmd, VulkanPipeline pipeline, DescriptorSet descriptorSet, Frustum frustum) {
        // 1. Cull the entire column first
        float minX = _chunkPos.X * Chunk.Size;
        float minZ = _chunkPos.Y * Chunk.Size;
        var colMin = new Vector3D<float>(minX, 0, minZ);
        var colMax = new Vector3D<float>(minX + Chunk.Size, TotalHeight, minZ + Chunk.Size);
        
        if (!frustum.IsBoxVisible(colMin, colMax)) return;

        var sizeVec = new Vector3D<float>(Chunk.Size, Chunk.Size, Chunk.Size);
        for (int i = 0; i < HeightInChunks; i++) {
            var chunk = _chunks[i];
            if (chunk.Mesh.IndexCount == 0) continue;
            
            // Culling per sub-chunk
            if (!frustum.IsBoxVisible(chunk.Position, chunk.Position + sizeVec)) continue;

            // Push the specific chunk position
            var pc = new PushConstants {
                Model = Matrix4X4.CreateTranslation(chunk.Position)
            };
            cmd.PushConstants(pipeline, ShaderStageFlags.VertexBit, pc);
            chunk.Mesh.Bind(cmd);
            cmd.DrawIndexed((uint) chunk.Mesh.IndexCount);
        }
    }
    public void Dispose() {
        foreach (var chunk in _chunks) {
            chunk.Mesh.Dispose();
        }
    }
    // Matching the Program.cs PushConstants struct for internal usage
    private struct PushConstants {
        public Matrix4X4<float> Model;
    }
}
