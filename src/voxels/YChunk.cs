using System.Collections.Generic;
using System.Runtime.InteropServices;
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
    public Vector2D<int> ChunkPos => _chunkPos;
    private readonly Vector2D<int> _chunkPos; // X, Z position in chunk coordinates
    private readonly Vector3D<float> _min;
    private readonly Vector3D<float> _max;

    private readonly Mesh _combinedMesh;
    private readonly List<Vertex>[] _subChunkVertices = new List<Vertex>[HeightInChunks];
    private readonly List<uint>[] _subChunkIndices = new List<uint>[HeightInChunks];
    private readonly List<Vertex> _combinedVertices = new();
    private readonly List<uint> _combinedIndices = new();
    private bool _needsMeshRebuild;
    private readonly World _world;

    public YChunk(VulkanContext ctx, World world, Vector2D<int> chunkPos) {
        _chunkPos = chunkPos;
        _world = world;
        _combinedMesh = new Mesh(ctx);
        
        float minX = _chunkPos.X * Chunk.Size;
        float minZ = _chunkPos.Y * Chunk.Size;
        _min = new Vector3D<float>(minX, 0, minZ);
        _max = new Vector3D<float>(minX + Chunk.Size, TotalHeight, minZ + Chunk.Size);

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
    
    public unsafe void SetChunkBlocks(int chunkIndex, BlockType* blocks) {
        if (chunkIndex >= 0 && chunkIndex < HeightInChunks) {
            _chunks[chunkIndex].SetBlocks(blocks);
        }
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
        for (int i = 0; i < HeightInChunks; i++) {
            _chunks[i].Update();
            if (_chunks[i].HasPendingMesh) {
                var (v, ind) = _chunks[i].TakePendingMesh();
                
                if (_subChunkVertices[i] != null) {
                    Chunk.RecycleLists(_subChunkVertices[i], _subChunkIndices[i]);
                }

                _subChunkVertices[i] = v;
                _subChunkIndices[i] = ind;
                _needsMeshRebuild = true;
            }
        }
    }
    
    public void UploadCombinedMesh(BatchUploader uploader, ChunkHeap heap, ref int uploadedCount, int maxUploads) {
        if (!_needsMeshRebuild || uploadedCount >= maxUploads) return;

        _combinedVertices.Clear();
        _combinedIndices.Clear();
        uint vertexOffset = 0;

        float xOffset = _chunkPos.X * Chunk.Size;
        float zOffset = _chunkPos.Y * Chunk.Size;

        for (int i = 0; i < HeightInChunks; i++) {
            var verts = _subChunkVertices[i];
            var inds = _subChunkIndices[i];
            if (verts == null || verts.Count == 0) continue;

            float yOffset = i * Chunk.Size;
            int vCount = verts.Count;
            for (int j = 0; j < vCount; j++) {
                var v = verts[j];
                var pos = v.Position;
                pos.X += xOffset;
                pos.Y += yOffset;
                pos.Z += zOffset;
                _combinedVertices.Add(new Vertex(pos, v.TexCoord, v.TexIndex));
            }

            int iCount = inds.Count;
            for (int j = 0; j < iCount; j++) {
                _combinedIndices.Add(inds[j] + vertexOffset);
            }

            vertexOffset += (uint) vCount;
        }

        _combinedMesh.Update(uploader, heap, CollectionsMarshal.AsSpan(_combinedVertices), CollectionsMarshal.AsSpan(_combinedIndices));
        
        _needsMeshRebuild = false;
        uploadedCount++;
    }

    public void CollectIndirectCommands(List<DrawIndexedIndirectCommand> commands, Frustum frustum, ref int renderedCount) {
        if (_combinedMesh.IndexCount == 0 || !_combinedMesh.HasAllocation) return;

        if (!frustum.IsBoxVisible(_min, _max)) return;

        commands.Add(new DrawIndexedIndirectCommand {
            IndexCount = (uint)_combinedMesh.IndexCount,
            InstanceCount = 1,
            FirstIndex = (uint)(_combinedMesh.IndexOffset / sizeof(uint)),
            VertexOffset = (int)(_combinedMesh.VertexOffset / (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<Vertex>()),
            FirstInstance = 0
        });

        renderedCount++;
    }

    public void Dispose(ChunkHeap heap) {
        for (int i = 0; i < HeightInChunks; i++) {
            if (_subChunkVertices[i] != null) {
                Chunk.RecycleLists(_subChunkVertices[i], _subChunkIndices[i]);
                _subChunkVertices[i] = null!;
                _subChunkIndices[i] = null!;
            }
            _chunks[i]?.Dispose();
        }
        _combinedMesh.Free(heap);
        _combinedMesh.Dispose();
    }
    
    public void Dispose() {
    }
}