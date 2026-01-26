using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Shiron.VulkanDumpster.Vulkan;
using Silk.NET.Maths;
namespace Shiron.VulkanDumpster.Voxels;

public class Chunk {
    public const int Size = 32;
    public readonly BlockType[] Blocks = new BlockType[Size * Size * Size];
    public bool IsMeshDirty { get; private set; } = true;
    public Vector3D<float> Position { get; private set; }
    private readonly VulkanContext _ctx;
    private readonly World _world;
    public Mesh Mesh { get; private set; }
    private volatile bool _isMeshing;
    private volatile bool _hasPendingMesh;
    private List<Vertex> _pendingVertices = null!;
    private List<uint> _pendingIndices = null!;
    private readonly object _blockLock = new();

    // Pooling to reduce allocations
    private static readonly ConcurrentQueue<List<Vertex>> _vertexListPool = new();
    private static readonly ConcurrentQueue<List<uint>> _indexListPool = new();
    private static int _activeMeshingTasks;
    public static int ActiveMeshingTasks => _activeMeshingTasks;

    public Chunk(VulkanContext ctx, World world, Vector3D<float> position) {
        _ctx = ctx;
        _world = world;
        Position = position;
        Mesh = new Mesh(_ctx);
    }
    public void SetBlock(int x, int y, int z, BlockType type) {
        if (x < 0 || x >= Size || y < 0 || y >= Size || z < 0 || z >= Size) return;
        int index = GetIndex(x, y, z);
        lock (_blockLock) {
            if (Blocks[index] != type) {
                Blocks[index] = type;
                IsMeshDirty = true;
            }
        }
    }
    public BlockType GetBlock(int x, int y, int z) {
        if (x < 0 || x >= Size || y < 0 || y >= Size || z < 0 || z >= Size) return BlockType.Air;
        lock (_blockLock) {
            return Blocks[GetIndex(x, y, z)];
        }
    }
    private int GetIndex(int x, int y, int z) {
        return x + (y * Size) + (z * Size * Size);
    }
    public void MarkDirty() {
        IsMeshDirty = true;
    }
    public void Update() {
        if (_hasPendingMesh) {
            var (oldV, oldI) = Mesh.SetData(_pendingVertices, _pendingIndices);
            Mesh.UpdateGpuBuffers();
            
            // Recycle the old lists
            if (oldV != null) _vertexListPool.Enqueue(oldV);
            if (oldI != null) _indexListPool.Enqueue(oldI);

            _hasPendingMesh = false;
            _pendingVertices = null!;
            _pendingIndices = null!;
        }
        if (IsMeshDirty && !_isMeshing) {
            _isMeshing = true;
            IsMeshDirty = false;
            // Snapshot data
            BlockType[] blocksCopy = new BlockType[Blocks.Length];
            lock (_blockLock) {
                Array.Copy(Blocks, blocksCopy, Blocks.Length);
            }
            System.Threading.Interlocked.Increment(ref _activeMeshingTasks);
            Task.Run(() => {
                try {
                    BuildMeshTask(blocksCopy);
                } catch (Exception e) {
                    Console.WriteLine($"Mesh build failed: {e}");
                } finally {
                    _isMeshing = false;
                    System.Threading.Interlocked.Decrement(ref _activeMeshingTasks);
                }
            });
        }
    }
    private void BuildMeshTask(BlockType[] blocks) {
        // Get lists from pool
        if (!_vertexListPool.TryDequeue(out var vertices)) vertices = new List<Vertex>();
        else vertices.Clear();

        if (!_indexListPool.TryDequeue(out var indices)) indices = new List<uint>();
        else indices.Clear();

        int vertexCount = 0;

        // Local helper to avoid lock overhead and use snapshot
        BlockType GetBlockLocal(int x, int y, int z) {
            if (x < 0 || x >= Size || y < 0 || y >= Size || z < 0 || z >= Size) {
                // Check neighbor via World
                int worldX = (int) Position.X + x;
                int worldY = (int) Position.Y + y;
                int worldZ = (int) Position.Z + z;
                return _world.GetBlock(worldX, worldY, worldZ);
            }
            return blocks[x + (y * Size) + (z * Size * Size)];
        }

        bool IsTransparentLocal(int x, int y, int z) {
            return GetBlockLocal(x, y, z) == BlockType.Air;
        }

        // Helper to mesh a slice
        // axis1: inner loop dimension (width)
        // axis2: outer loop dimension (height)
        // sliceAxis: the dimension we are slicing through
        void MeshFace(int axis1Max, int axis2Max, int sliceMax, 
                      Func<int, int, int, BlockType> getMaskAt, 
                      Action<int, int, int, int, int, float> addFace) {
            
            var mask = new BlockType[axis1Max * axis2Max];

            for (int slice = 0; slice < sliceMax; slice++) {
                // 1. Build Mask for this slice
                for (int ax2 = 0; ax2 < axis2Max; ax2++) {
                    for (int ax1 = 0; ax1 < axis1Max; ax1++) {
                        mask[ax1 + ax2 * axis1Max] = getMaskAt(ax1, ax2, slice);
                    }
                }

                // 2. Greedy merge
                for (int j = 0; j < axis2Max; j++) {
                    for (int i = 0; i < axis1Max; i++) {
                        var type = mask[i + j * axis1Max];
                        if (type != BlockType.Air) {
                            int w = 1;
                            // Expand width
                            while (i + w < axis1Max && mask[(i + w) + j * axis1Max] == type) {
                                w++;
                            }

                            int h = 1;
                            // Expand height
                            bool done = false;
                            while (j + h < axis2Max) {
                                for (int k = 0; k < w; k++) {
                                    if (mask[(i + k) + (j + h) * axis1Max] != type) {
                                        done = true;
                                        break;
                                    }
                                }
                                if (done) break;
                                h++;
                            }

                            // Add Quad
                            // Note: We pass i, j, slice. The interpretation depends on the caller.
                            addFace(i, j, slice, w, h, (float)type - 1);

                            // Clear Mask
                            for (int dy = 0; dy < h; dy++) {
                                for (int dx = 0; dx < w; dx++) {
                                    mask[(i + dx) + (j + dy) * axis1Max] = BlockType.Air;
                                }
                            }

                            i += w - 1;
                        }
                    }
                }
            }
        }

        // Top (+Y)
        // Dimensions: X (width), Z (height), Y (slice)
        MeshFace(Size, Size, Size, 
            (x, z, y) => (IsTransparentLocal(x, y + 1, z) && GetBlockLocal(x, y, z) != BlockType.Air) ? GetBlockLocal(x, y, z) : BlockType.Air,
            (x, z, y, w, h, tex) => AddFaceTop(x, y, z, w, h, tex, ref vertexCount, vertices, indices));

        // Bottom (-Y)
        // Dimensions: X (width), Z (height), Y (slice)
        MeshFace(Size, Size, Size,
            (x, z, y) => (IsTransparentLocal(x, y - 1, z) && GetBlockLocal(x, y, z) != BlockType.Air) ? GetBlockLocal(x, y, z) : BlockType.Air,
            (x, z, y, w, h, tex) => AddFaceBottom(x, y, z, w, h, tex, ref vertexCount, vertices, indices));

        // Right (+X)
        // Dimensions: Z (width), Y (height), X (slice)
        // Note: Orientation matters for UVs/Back-face culling. 
        // Standard loops: X=slice. Z=width (inner), Y=height (outer).
        MeshFace(Size, Size, Size,
            (z, y, x) => (IsTransparentLocal(x + 1, y, z) && GetBlockLocal(x, y, z) != BlockType.Air) ? GetBlockLocal(x, y, z) : BlockType.Air,
            (z, y, x, w, h, tex) => AddFaceRight(x, y, z, w, h, tex, ref vertexCount, vertices, indices));

        // Left (-X)
        // Dimensions: Z (width), Y (height), X (slice)
        MeshFace(Size, Size, Size,
            (z, y, x) => (IsTransparentLocal(x - 1, y, z) && GetBlockLocal(x, y, z) != BlockType.Air) ? GetBlockLocal(x, y, z) : BlockType.Air,
            (z, y, x, w, h, tex) => AddFaceLeft(x, y, z, w, h, tex, ref vertexCount, vertices, indices));

        // Front (+Z)
        // Dimensions: X (width), Y (height), Z (slice)
        MeshFace(Size, Size, Size,
            (x, y, z) => (IsTransparentLocal(x, y, z + 1) && GetBlockLocal(x, y, z) != BlockType.Air) ? GetBlockLocal(x, y, z) : BlockType.Air,
            (x, y, z, w, h, tex) => AddFaceFront(x, y, z, w, h, tex, ref vertexCount, vertices, indices));

        // Back (-Z)
        // Dimensions: X (width), Y (height), Z (slice)
        MeshFace(Size, Size, Size,
            (x, y, z) => (IsTransparentLocal(x, y, z - 1) && GetBlockLocal(x, y, z) != BlockType.Air) ? GetBlockLocal(x, y, z) : BlockType.Air,
            (x, y, z, w, h, tex) => AddFaceBack(x, y, z, w, h, tex, ref vertexCount, vertices, indices));

        _pendingVertices = vertices;
        _pendingIndices = indices;
        _hasPendingMesh = true;
        _isMeshing = false;
    }

    private void AddFaceFront(float x, float y, float z, float w, float h, float texIndex, ref int vCount, List<Vertex> verts, List<uint> inds) {
        // Z+ face
        verts.Add(new Vertex(new Vector3D<float>(x, y, z + 1), new Vector2D<float>(0, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + w, y, z + 1), new Vector2D<float>(w, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + w, y + h, z + 1), new Vector2D<float>(w, h), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y + h, z + 1), new Vector2D<float>(0, h), texIndex));
        AddIndices(ref vCount, inds);
    }

    private void AddFaceBack(float x, float y, float z, float w, float h, float texIndex, ref int vCount, List<Vertex> verts, List<uint> inds) {
        // Z- face
        verts.Add(new Vertex(new Vector3D<float>(x + w, y, z), new Vector2D<float>(0, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y, z), new Vector2D<float>(w, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y + h, z), new Vector2D<float>(w, h), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + w, y + h, z), new Vector2D<float>(0, h), texIndex));
        AddIndices(ref vCount, inds);
    }

    private void AddFaceTop(float x, float y, float z, float w, float h, float texIndex, ref int vCount, List<Vertex> verts, List<uint> inds) {
        // Y+ face
        // Width is X, Height is Z
        verts.Add(new Vertex(new Vector3D<float>(x, y + 1, z + h), new Vector2D<float>(0, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + w, y + 1, z + h), new Vector2D<float>(w, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + w, y + 1, z), new Vector2D<float>(w, h), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y + 1, z), new Vector2D<float>(0, h), texIndex));
        AddIndices(ref vCount, inds);
    }

    private void AddFaceBottom(float x, float y, float z, float w, float h, float texIndex, ref int vCount, List<Vertex> verts, List<uint> inds) {
        // Y- face
        verts.Add(new Vertex(new Vector3D<float>(x, y, z), new Vector2D<float>(0, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + w, y, z), new Vector2D<float>(w, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + w, y, z + h), new Vector2D<float>(w, h), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y, z + h), new Vector2D<float>(0, h), texIndex));
        AddIndices(ref vCount, inds);
    }

    private void AddFaceLeft(float x, float y, float z, float w, float h, float texIndex, ref int vCount, List<Vertex> verts, List<uint> inds) {
        // X- face
        // Width is Z, Height is Y
        // Input: z=width axis, y=height axis
        // w param corresponds to Z growth, h param corresponds to Y growth
        verts.Add(new Vertex(new Vector3D<float>(x, y, z), new Vector2D<float>(0, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y, z + w), new Vector2D<float>(w, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y + h, z + w), new Vector2D<float>(w, h), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y + h, z), new Vector2D<float>(0, h), texIndex));
        AddIndices(ref vCount, inds);
    }

    private void AddFaceRight(float x, float y, float z, float w, float h, float texIndex, ref int vCount, List<Vertex> verts, List<uint> inds) {
        // X+ face
        // Width is Z, Height is Y
        // w param is Z, h param is Y
        verts.Add(new Vertex(new Vector3D<float>(x + 1, y, z + w), new Vector2D<float>(0, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + 1, y, z), new Vector2D<float>(w, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + 1, y + h, z), new Vector2D<float>(w, h), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + 1, y + h, z + w), new Vector2D<float>(0, h), texIndex));
        AddIndices(ref vCount, inds);
    }
    private void AddIndices(ref int vCount, List<uint> inds) {
        inds.Add((uint) (vCount + 0));
        inds.Add((uint) (vCount + 1));
        inds.Add((uint) (vCount + 2));
        inds.Add((uint) (vCount + 2));
        inds.Add((uint) (vCount + 3));
        inds.Add((uint) (vCount + 0));
        vCount += 4;
    }
    // Removed old RebuildMesh and AddFace methods as they are replaced by Task versions
}
