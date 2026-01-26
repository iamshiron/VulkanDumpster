using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Silk.NET.Maths;
using Silk.NET.Vulkan;
using Shiron.VulkanDumpster.Vulkan;
using Shiron.VulkanDumpster;
namespace Shiron.VulkanDumpster.Voxels;

public class World : IDisposable {
    public int RenderDistance => _settings.RenderDistance;
    public int ChunkCount => _chunks.Count;
    public int RenderedChunksCount { get; private set; }
    public int TotalRegionsCount => _regions.Count;
    public int RenderedRegionsCount { get; private set; }
    public int LastFrameUpdates { get; private set; }

    private readonly ConcurrentDictionary<Vector2D<int>, YChunk> _chunks = new();
    private readonly ConcurrentDictionary<Vector2D<int>, Region> _regions = new();
    private readonly VulkanContext _ctx;
    private readonly Renderer _renderer;
    private readonly AppSettings _settings;
    private readonly ConcurrentQueue<Vector2D<int>> _loadQueue = new();
    private readonly ConcurrentQueue<Vector2D<int>> _unloadQueue = new();
    private Vector2D<int>? _lastCenterChunk;
    private readonly BatchUploader _batchUploader;

    private readonly ChunkHeap _chunkHeap;
    private readonly VulkanBuffer[] _indirectBuffers;
    private readonly List<DrawIndexedIndirectCommand> _indirectCommands = new();

    public World(VulkanContext ctx, Renderer renderer, AppSettings settings) {
        _ctx = ctx;
        _renderer = renderer;
        _settings = settings;
        _batchUploader = new BatchUploader(_ctx, renderer);

        // 256MB for vertices, 128MB for indices
        _chunkHeap = new ChunkHeap(_ctx, 256 * 1024 * 1024, 128 * 1024 * 1024);

        _indirectBuffers = new VulkanBuffer[3];
        for (int i = 0; i < 3; i++) {
            _indirectBuffers[i] = new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
                10000 * (ulong) Marshal.SizeOf<DrawIndexedIndirectCommand>(),
                BufferUsageFlags.IndirectBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }
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
            // Use _chunks directly here as this is a slow-path (boundary cross)
            foreach (var chunkPos in _chunks.Keys) {
                if (!IsInSquare(chunkPos, centerChunk, RenderDistance + 1)) {
                    _unloadQueue.Enqueue(chunkPos);
                }
            }
            while (_unloadQueue.TryDequeue(out var pos)) {
                if (_chunks.TryRemove(pos, out var chunk)) {
                    var regionPos = GetRegionPos(pos);
                    if (_regions.TryGetValue(regionPos, out var region)) {
                        region.RemoveChunk(chunk);
                        if (region.IsEmpty) {
                            _regions.TryRemove(regionPos, out _);
                        }
                    }
                    chunk.Dispose(_chunkHeap);
                }
            }
        }

        int uploadedCount = 0;
        const int MaxUploads = 32;

        _batchUploader.Begin();
        // HOT PATH: Iterate Regions instead of all chunks
        foreach (var region in _regions.Values) {
            region.Update(_batchUploader, _chunkHeap, ref uploadedCount, MaxUploads);
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
            var regionPos = GetRegionPos(chunkPos);
            var region = _regions.GetOrAdd(regionPos, pos => new Region(pos));
            region.AddChunk(chunk);

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

    private unsafe void GenerateTerrain(YChunk chunk, Vector2D<int> chunkPos) {
        int baseX = chunkPos.X * Chunk.Size;
        int baseZ = chunkPos.Y * Chunk.Size;
        
        int totalBlockCount = Chunk.Size * Chunk.Size * YChunk.TotalHeight;
        BlockType* columnBlocks = (BlockType*)UnmanagedPool.Rent((nuint)(totalBlockCount * sizeof(BlockType)));
        System.Runtime.CompilerServices.Unsafe.InitBlock(columnBlocks, 0, (uint)(totalBlockCount * sizeof(BlockType)));

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
                        int idx = x + (localY * Chunk.Size) + (z * Chunk.Size * Chunk.Size) + (chunkY * Chunk.BlockCount);
                        columnBlocks[idx] = type;
                    }
                }
            }
            
            for (int i = 0; i < YChunk.HeightInChunks; i++) {
                BlockType* subChunkPtr = columnBlocks + (i * Chunk.BlockCount);
                chunk.SetChunkBlocks(i, subChunkPtr);
            }
        } finally {
            UnmanagedPool.Return(columnBlocks, (nuint)(totalBlockCount * sizeof(BlockType)));
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
    private Vector2D<int> GetRegionPos(Vector2D<int> chunkPos) {
        return new Vector2D<int>(
            (int) MathF.Floor((float) chunkPos.X / Region.SizeInChunks),
            (int) MathF.Floor((float) chunkPos.Y / Region.SizeInChunks)
        );
    }
    private int Mod(int n, int m) {
        return ((n % m) + m) % m;
    }
    public unsafe void Render(VulkanCommandBuffer cmd, VulkanPipeline pipeline, DescriptorSet descriptorSet, Frustum frustum) {
        Profiler.Begin("World Render Loop");
        _indirectCommands.Clear();
        
        int rendered = 0;
        int renderedRegions = 0;
        // HOT PATH: Render via Regions (Hierarchical Culling)
        foreach (var region in _regions.Values) {
            float minX = region.RegionPos.X * Region.SizeInBlocks;
            float minZ = region.RegionPos.Y * Region.SizeInBlocks;
            var min = new Vector3D<float>(minX, 0, minZ);
            var max = new Vector3D<float>(minX + Region.SizeInBlocks, YChunk.TotalHeight, minZ + Region.SizeInBlocks);
            
            if (frustum.IsBoxVisible(min, max)) {
                renderedRegions++;
                region.Render(_indirectCommands, frustum, ref rendered);
            }
        }
        RenderedChunksCount = rendered;
        RenderedRegionsCount = renderedRegions;

        if (_indirectCommands.Count > 0) {
            int frameIndex = _renderer.CurrentFrameIndex;
            var indirectBuffer = _indirectBuffers[frameIndex];
            
            var span = CollectionsMarshal.AsSpan(_indirectCommands);
            ulong requiredSize = (ulong)(span.Length * Marshal.SizeOf<DrawIndexedIndirectCommand>());
            
            if (indirectBuffer.Size < requiredSize) {
                _ctx.EnqueueDispose(() => indirectBuffer.Dispose());
                _indirectBuffers[frameIndex] = new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
                    requiredSize * 2,
                    BufferUsageFlags.IndirectBufferBit | BufferUsageFlags.TransferDstBit,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
                indirectBuffer = _indirectBuffers[frameIndex];
            }

            fixed (void* pData = span) {
                System.Buffer.MemoryCopy(pData, indirectBuffer.MappedData, requiredSize, requiredSize);
            }

            _chunkHeap.Bind(cmd);
            cmd.DrawIndexedIndirect(indirectBuffer, 0, (uint)span.Length, (uint)Marshal.SizeOf<DrawIndexedIndirectCommand>());
        }

        Profiler.End("World Render Loop");
    }
    public void Dispose() {
        foreach (var chunk in _chunks.Values) {
            chunk.Dispose(_chunkHeap);
        }
        _chunkHeap.Dispose();
        foreach (var buffer in _indirectBuffers) {
            buffer.Dispose();
        }
        _batchUploader?.Dispose();
    }
}