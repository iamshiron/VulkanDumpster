using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Shiron.VulkanDumpster.Vulkan;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Shiron.VulkanDumpster;
namespace Shiron.VulkanDumpster.Voxels;

public class World : IDisposable {
    public int RenderDistance { get; set; } = 16;
    public int ChunkCount => _chunks.Count;
    public int RenderedChunksCount { get; private set; }
    public int LastFrameUpdates { get; private set; }

    private readonly ConcurrentDictionary<Vector2D<int>, YChunk> _chunks = new();
    private readonly VulkanContext _ctx;
    private readonly ConcurrentQueue<Vector2D<int>> _loadQueue = new();
    private readonly ConcurrentQueue<Vector2D<int>> _unloadQueue = new();
    private Vector2D<int>? _lastCenterChunk;
    private readonly BatchUploader _batchUploader;

    public World(VulkanContext ctx, Renderer renderer) {
        _ctx = ctx;
        _batchUploader = new BatchUploader(_ctx, renderer);
    }
    public void Update(Vector3D<float> cameraPos) {
        var centerChunk = GetChunkPos((int) cameraPos.X, (int) cameraPos.Z);
        
        if (_lastCenterChunk != centerChunk) {
            _lastCenterChunk = centerChunk;
            for (int x = -RenderDistance; x <= RenderDistance; x++) {
                for (int z = -RenderDistance; z <= RenderDistance; z++) {
                    var pos = new Vector2D<int>(centerChunk.X + x, centerChunk.Y + z);
                    if (!_chunks.ContainsKey(pos)) {
                        GenerateChunk(pos);
                    }
                }
            }
            foreach (var chunkPos in _chunks.Keys) {
                if (!IsInSquare(chunkPos, centerChunk, RenderDistance + 1)) {
                    _unloadQueue.Enqueue(chunkPos);
                }
            }
            while (_unloadQueue.TryDequeue(out var pos)) {
                if (_chunks.TryRemove(pos, out var chunk)) {
                    chunk.Dispose();
                }
            }
        }

        int uploadedCount = 0;
        const int MaxUploads = 32;

        _batchUploader.Begin();
        foreach (var chunk in _chunks.Values) {
            chunk.Update();
            chunk.UploadPendingMeshes(_batchUploader, ref uploadedCount, MaxUploads);
        }
        _batchUploader.Flush();
        LastFrameUpdates = uploadedCount;
    }
    private bool IsInSquare(Vector2D<int> pos, Vector2D<int> center, int radius) {
        return Math.Abs(pos.X - center.X) <= radius &&
               Math.Abs(pos.Y - center.Y) <= radius;
    }
    private void GenerateChunk(Vector2D<int> chunkPos) {
        var chunk = new YChunk(_ctx, this, chunkPos);
        if (_chunks.TryAdd(chunkPos, chunk)) {
            Task.Run(() => GenerateTerrain(chunk, chunkPos));
        } else {
            chunk.Dispose();
        }
    }

    private void OnChunkGenerated(Vector2D<int> chunkPos) {
        UpdateNeighbor(chunkPos.X + 1, chunkPos.Y);
        UpdateNeighbor(chunkPos.X - 1, chunkPos.Y);
        UpdateNeighbor(chunkPos.X, chunkPos.Y + 1);
        UpdateNeighbor(chunkPos.X, chunkPos.Y - 1);
        if (_chunks.TryGetValue(chunkPos, out var chunk)) {
            chunk.MarkDirty();
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
        BlockType[][] subChunkBlocks = new BlockType[YChunk.HeightInChunks][];
        for (int i = 0; i < YChunk.HeightInChunks; i++) {
            subChunkBlocks[i] = ArrayPool<BlockType>.Shared.Rent(Chunk.Size * Chunk.Size * Chunk.Size);
            Array.Clear(subChunkBlocks[i], 0, Chunk.Size * Chunk.Size * Chunk.Size);
        }

        try {
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
                        int chunkY = y / Chunk.Size;
                        int localY = y % Chunk.Size;
                        int idx = x + (localY * Chunk.Size) + (z * Chunk.Size * Chunk.Size);
                        subChunkBlocks[chunkY][idx] = type;
                    }
                }
            }
            for (int i = 0; i < YChunk.HeightInChunks; i++) {
                chunk.SetChunkBlocks(i, subChunkBlocks[i]);
            }
        } finally {
            for (int i = 0; i < YChunk.HeightInChunks; i++) {
                if (subChunkBlocks[i] != null) 
                    ArrayPool<BlockType>.Shared.Return(subChunkBlocks[i]);
            }
        }
        OnChunkGenerated(chunkPos);
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
    public void Render(VulkanCommandBuffer cmd, VulkanPipeline pipeline, DescriptorSet descriptorSet, Frustum frustum) {
        int rendered = 0;
        foreach (var chunk in _chunks.Values) {
            chunk.Render(cmd, pipeline, descriptorSet, frustum, ref rendered);
        }
        RenderedChunksCount = rendered;
    }
    public void Dispose() {
        foreach (var chunk in _chunks.Values) {
            chunk.Dispose();
        }
        _batchUploader?.Dispose();
    }
}