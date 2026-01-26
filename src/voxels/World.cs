using System.Collections.Concurrent;
using Shiron.VulkanDumpster.Vulkan;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Shiron.VulkanDumpster;
namespace Shiron.VulkanDumpster.Voxels;

public class World : IDisposable {
    public int RenderDistance { get; set; } = 16;
    public int ChunkCount => _chunks.Count;
    private readonly ConcurrentDictionary<Vector2D<int>, YChunk> _chunks = new();
    private readonly VulkanContext _ctx;
    // Thread-safe queues for future async handling
    private readonly ConcurrentQueue<Vector2D<int>> _loadQueue = new();
    private readonly ConcurrentQueue<Vector2D<int>> _unloadQueue = new();
    private Vector2D<int>? _lastCenterChunk;
    public World(VulkanContext ctx) {
        _ctx = ctx;
        // Initial generation will happen on first Update based on camera pos
    }
    public void Update(Vector3D<float> cameraPos) {
        var centerChunk = GetChunkPos((int) cameraPos.X, (int) cameraPos.Z);
        
        if (_lastCenterChunk != centerChunk) {
            _lastCenterChunk = centerChunk;
            // 1. Identify chunks to load (Square area)
            for (int x = -RenderDistance; x <= RenderDistance; x++) {
                for (int z = -RenderDistance; z <= RenderDistance; z++) {
                    var pos = new Vector2D<int>(centerChunk.X + x, centerChunk.Y + z);
                    if (!_chunks.ContainsKey(pos)) {
                        GenerateChunk(pos);
                    }
                }
            }
            // 2. Identify chunks to unload
            foreach (var chunkPos in _chunks.Keys) {
                if (!IsInSquare(chunkPos, centerChunk, RenderDistance + 1)) { // +1 hysteresis
                    _unloadQueue.Enqueue(chunkPos);
                }
            }
            // 3. Process Unload Queue
            while (_unloadQueue.TryDequeue(out var pos)) {
                if (_chunks.TryRemove(pos, out var chunk)) {
                    chunk.Dispose();
                }
            }
        }

        // 4. Update active chunks
        foreach (var chunk in _chunks.Values) {
            chunk.Update();
        }
    }
    private bool IsInSquare(Vector2D<int> pos, Vector2D<int> center, int radius) {
        return Math.Abs(pos.X - center.X) <= radius &&
               Math.Abs(pos.Y - center.Y) <= radius;
    }
    private void GenerateChunk(Vector2D<int> chunkPos) {
        // In a real threaded scenario, this constructor and setblock logic would run on a worker.
        // The mesh generation (which touches Vulkan) would happen on main thread or via a command buffer.
        // For now, we do it all sync.
        var chunk = new YChunk(_ctx, this, chunkPos);
        GenerateTerrain(chunk, chunkPos);
        if (_chunks.TryAdd(chunkPos, chunk)) {
            // Mark neighbors dirty so they can re-mesh against this new chunk
            UpdateNeighbor(chunkPos.X + 1, chunkPos.Y);
            UpdateNeighbor(chunkPos.X - 1, chunkPos.Y);
            UpdateNeighbor(chunkPos.X, chunkPos.Y + 1);
            UpdateNeighbor(chunkPos.X, chunkPos.Y - 1);
        }
    }
    private void UpdateNeighbor(int x, int z) {
        if (_chunks.TryGetValue(new Vector2D<int>(x, z), out var neighbor)) {
            neighbor.MarkDirty();
        }
    }
    private void GenerateTerrain(YChunk chunk, Vector2D<int> chunkPos) {
        int baseX = chunkPos.X * Chunk.Size;
        int baseZ = chunkPos.Y * Chunk.Size;
        for (int x = 0; x < Chunk.Size; x++) {
            for (int z = 0; z < Chunk.Size; z++) {
                int worldX = baseX + x;
                int worldZ = baseZ + z;
                float noise = MathF.Sin(worldX * 0.05f) * 10 + MathF.Cos(worldZ * 0.05f) * 10 + 64;
                int height = Math.Clamp((int) noise, 1, YChunk.TotalHeight - 1);
                for (int y = 0; y < height; y++) {
                    BlockType type = BlockType.Stone;
                    if (y == height - 1) type = BlockType.Grass;
                    else if (y > height - 5) type = BlockType.Dirt;
                    // SetBlock is local to YChunk, so we pass local x,z but global y
                    chunk.SetBlock(x, y, z, type);
                }
            }
        }
    }
    public void SetBlock(int x, int y, int z, BlockType type) {
        if (y < 0 || y >= YChunk.TotalHeight) return;
        var chunkPos = GetChunkPos(x, z);
        if (_chunks.TryGetValue(chunkPos, out var chunk)) {
            int localX = Mod(x, Chunk.Size);
            int localZ = Mod(z, Chunk.Size);
            chunk.SetBlock(localX, y, localZ, type);
        }
    }
    public BlockType GetBlock(int x, int y, int z) {
        if (y < 0 || y >= YChunk.TotalHeight) return BlockType.Air;
        var chunkPos = GetChunkPos(x, z);
        if (_chunks.TryGetValue(chunkPos, out var chunk)) {
            int localX = Mod(x, Chunk.Size);
            int localZ = Mod(z, Chunk.Size);
            return chunk.GetBlock(localX, y, localZ);
        }
        return BlockType.Air;
    }
    private Vector2D<int> GetChunkPos(int x, int z) {
        return new Vector2D<int>(
            (int) MathF.Floor((float) x / Chunk.Size),
            (int) MathF.Floor((float) z / Chunk.Size)
        );
    }
    private int Mod(int n, int m) {
        return ((n % m) + m) % m;
    }
    // Removed simple Update() in favor of Update(cameraPos) above
    public void Render(VulkanCommandBuffer cmd, VulkanPipeline pipeline, DescriptorSet descriptorSet, Frustum frustum) {
        foreach (var chunk in _chunks.Values) {
            chunk.Render(cmd, pipeline, descriptorSet, frustum);
        }
    }
    public void Dispose() {
        foreach (var chunk in _chunks.Values) {
            chunk.Dispose();
        }
    }
}
