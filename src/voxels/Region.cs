using System.Collections.Generic;
using Shiron.VulkanDumpster.Vulkan;
using Silk.NET.Maths;
using Silk.NET.Vulkan;

namespace Shiron.VulkanDumpster.Voxels;

public class Region {
    public const int SizeInChunks = 8;
    public const int SizeInBlocks = SizeInChunks * Chunk.Size;

    public Vector2D<int> RegionPos { get; }
    public Vector3D<float> Min => _min;
    public Vector3D<float> Max => _max;

    private readonly List<YChunk> _chunks = new();
    private Vector3D<float> _min;
    private Vector3D<float> _max;

    public Region(Vector2D<int> regionPos) {
        RegionPos = regionPos;
        float minX = regionPos.X * SizeInBlocks;
        float minZ = regionPos.Y * SizeInBlocks;
        _min = new Vector3D<float>(minX, 0, minZ);
        _max = new Vector3D<float>(minX + SizeInBlocks, YChunk.TotalHeight, minZ + SizeInBlocks);
    }

    public void AddChunk(YChunk chunk) {
        _chunks.Add(chunk);
    }

    public bool RemoveChunk(YChunk chunk) {
        return _chunks.Remove(chunk);
    }

    public bool IsEmpty => _chunks.Count == 0;

    public void Render(List<DrawIndexedIndirectCommand> commands, Frustum frustum, ref int renderedCount) {
        // Region visibility is already checked by World.Render
        for (int i = 0; i < _chunks.Count; i++) {
            _chunks[i].CollectIndirectCommands(commands, frustum, ref renderedCount);
        }
    }

    public void Update(BatchUploader uploader, ChunkHeap heap, ref int uploadedCount, int maxUploads) {
        for (int i = 0; i < _chunks.Count; i++) {
            var chunk = _chunks[i];
            chunk.Update();
            chunk.UploadCombinedMesh(uploader, heap, ref uploadedCount, maxUploads);
        }
    }
}
