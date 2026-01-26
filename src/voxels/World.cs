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
using Shiron.VulkanDumpster.Voxels.Generation;

namespace Shiron.VulkanDumpster.Voxels;

public class World : IDisposable {
    public int RenderDistance => _settings.RenderDistance;
    public int ChunkCount { get; private set; }
    public int RenderedChunksCount { get; private set; }
    public int TotalRegionsCount => _regions.Count;
    public int RenderedRegionsCount { get; private set; }
    public int LastFrameUpdates { get; private set; }
    public IWorldGenerator Generator => _generator;

    private readonly ChunkGrid _grid;
    private readonly List<Region> _regionList = new();
    private readonly ConcurrentDictionary<Vector2D<int>, Region> _regions = new(); 
    private readonly VulkanContext _ctx;
    private readonly Renderer _renderer;
    private readonly AppSettings _settings;
    private Vector2D<int>? _lastCenterChunk;
    private readonly BatchUploader _batchUploader;

    private readonly ChunkHeap _chunkHeap;
    private readonly VulkanBuffer[] _indirectBuffers;
    private readonly List<DrawIndexedIndirectCommand> _indirectCommands = new();
    private readonly BlockingCollection<YChunk> _chunkGenerationQueue = new();
    private readonly ConcurrentQueue<YChunk> _failedChunks = new();
    private readonly IWorldGenerator _generator;
    private readonly Task _workerTask;
    private bool _disposed;

    public World(VulkanContext ctx, Renderer renderer, AppSettings settings, IWorldGenerator generator) {
        _ctx = ctx;
        _renderer = renderer;
        _settings = settings;
        _generator = generator;
        _batchUploader = new BatchUploader(_ctx, renderer);
        _grid = new ChunkGrid(_settings.RenderDistance);

        // Start with smaller heap, will grow as needed
        _chunkHeap = new ChunkHeap(_ctx, 64 * 1024 * 1024, 32 * 1024 * 1024);

        _indirectBuffers = new VulkanBuffer[3];
        for (int i = 0; i < 3; i++) {
            _indirectBuffers[i] = new VulkanBuffer(_ctx.Vk, _ctx.Device, _ctx.PhysicalDevice,
                10000 * (ulong) Marshal.SizeOf<DrawIndexedIndirectCommand>(),
                BufferUsageFlags.IndirectBufferBit | BufferUsageFlags.TransferDstBit,
                MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
        }

        _workerTask = Task.Factory.StartNew(() => {
            foreach (var chunk in _chunkGenerationQueue.GetConsumingEnumerable()) {
                try {
                    _generator.Generate(chunk);
                    OnChunkGenerated(chunk.ChunkPos);
                } catch (Exception e) {
                    Console.WriteLine($"[World] Chunk generation failed for {chunk.ChunkPos}: {e}");
                    _failedChunks.Enqueue(chunk);
                }
            }
        }, TaskCreationOptions.LongRunning);
    }

    public void Update(Position cameraPos) {
        // Handle failed chunks from worker thread
        while (_failedChunks.TryDequeue(out var failedChunk)) {
            if (_grid.GetChunk(failedChunk.ChunkPos) == failedChunk) {
                UnloadChunk(failedChunk.ChunkPos);
            }
        }

        var centerChunk = cameraPos.GetChunkPos();
        
        if (_lastCenterChunk != centerChunk) {
            _lastCenterChunk = centerChunk;
            
            // 1. Unload out-of-bounds chunks
            foreach (var chunk in _grid.GetAllActive()) {
                if (!IsInSquare(chunk.ChunkPos, centerChunk, RenderDistance)) {
                    UnloadChunk(chunk.ChunkPos);
                }
            }

            // 2. Load new chunks
            for (int x = -RenderDistance; x <= RenderDistance; x++) {
                for (int z = -RenderDistance; z <= RenderDistance; z++) {
                    var pos = new Vector2D<int>(centerChunk.X + x, centerChunk.Y + z);
                    if (_grid.GetChunk(pos) == null) {
                        LoadChunk(pos);
                    }
                }
            }
        }

        int uploadedCount = 0;
        const int MaxUploads = 32;

        _batchUploader.Begin();
        // HOT PATH: Zero-allocation iteration
        for (int i = 0; i < _regionList.Count; i++) {
            _regionList[i].Update(_batchUploader, _chunkHeap, ref uploadedCount, MaxUploads);
        }
        _batchUploader.Flush();
        LastFrameUpdates = uploadedCount;
    }

    private void LoadChunk(Vector2D<int> pos) {
        var chunk = new YChunk(_ctx, this, pos);
        _grid.SetChunk(pos, chunk);
        ChunkCount++;

        var regionPos = GetRegionPos(pos);
        var region = _regions.GetOrAdd(regionPos, p => {
            var r = new Region(p);
            lock (_regionList) {
                _regionList.Add(r);
            }
            return r;
        });
        region.AddChunk(chunk);

        _chunkGenerationQueue.Add(chunk);
    }

    private void UnloadChunk(Vector2D<int> pos) {
        var chunk = _grid.GetChunk(pos);
        if (chunk != null) {
            _grid.SetChunk(pos, null);
            ChunkCount--;

            var regionPos = GetRegionPos(pos);
            if (_regions.TryGetValue(regionPos, out var region)) {
                region.RemoveChunk(chunk);
                if (region.IsEmpty) {
                    if (_regions.TryRemove(regionPos, out _)) {
                        lock (_regionList) {
                            _regionList.Remove(region);
                        }
                    }
                }
            }
            chunk.Dispose(_chunkHeap);
        }
    }

    private bool IsInSquare(Vector2D<int> pos, Vector2D<int> center, int radius) {
        return Math.Abs(pos.X - center.X) <= radius &&
               Math.Abs(pos.Y - center.Y) <= radius;
    }

    private void OnChunkGenerated(Vector2D<int> chunkPos) {
        UpdateNeighbor(chunkPos.X + 1, chunkPos.Y);
        UpdateNeighbor(chunkPos.X - 1, chunkPos.Y);
        UpdateNeighbor(chunkPos.X, chunkPos.Y + 1);
        UpdateNeighbor(chunkPos.X, chunkPos.Y - 1);
        var chunk = _grid.GetChunk(chunkPos);
        chunk?.MarkDirty();
    }

    private void UpdateNeighbor(int x, int z) {
        var neighbor = _grid.GetChunk(new Vector2D<int>(x, z));
        neighbor?.MarkDirty();
    }

    public void SetBlock(int x, int y, int z, BlockType type) {
        if (y < 0 || y >= YChunk.TotalHeight) return;
        var chunkPos = GetChunkPos(x, z);
        var chunk = _grid.GetChunk(chunkPos);
        if (chunk != null) {
            chunk.SetBlock(Mod(x, Chunk.Size), y, Mod(z, Chunk.Size), type);
        }
    }

    public BlockType GetBlock(int x, int y, int z) {
        if (y < 0 || y >= YChunk.TotalHeight) return BlockType.Air;
        var chunkPos = GetChunkPos(x, z);
        var chunk = _grid.GetChunk(chunkPos);
        if (chunk != null) {
            return chunk.GetBlock(Mod(x, Chunk.Size), y, Mod(z, Chunk.Size));
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
        // HOT PATH: Zero-allocation hierarchical culling
        for (int i = 0; i < _regionList.Count; i++) {
            var region = _regionList[i];
            if (frustum.IsBoxVisible(region.Min, region.Max)) {
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
        if (_disposed) return;
        _disposed = true;

        _chunkGenerationQueue.CompleteAdding();
        if (_workerTask != null) {
            try {
                _workerTask.Wait();
            } catch (AggregateException) { }
        }
        _chunkGenerationQueue.Dispose();

        foreach (var chunk in _grid.GetAllActive()) {
            chunk.Dispose(_chunkHeap);
        }
        _chunkHeap.Dispose();
        foreach (var buffer in _indirectBuffers) {
            buffer.Dispose();
        }
        _batchUploader?.Dispose();
    }
}