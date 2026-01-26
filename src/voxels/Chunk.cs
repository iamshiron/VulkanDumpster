using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Shiron.VulkanDumpster.Vulkan;
using Silk.NET.Maths;

namespace Shiron.VulkanDumpster.Voxels;

public unsafe class Chunk : IDisposable {
    public const int Size = 32;
    public const int BlockCount = Size * Size * Size;

    private BlockType* _blocks;
    public BlockType* Blocks => _blocks;

    public bool IsMeshDirty { get; private set; } = true;
    public bool IsEmpty { get; private set; } = true;
    public bool IsDisposed { get; private set; }
    public Vector3D<float> Position { get; private set; }
    private readonly VulkanContext _ctx;
    private readonly World _world;
    private volatile bool _isMeshing;
    private volatile bool _hasPendingMesh;
    private List<Vertex> _pendingVertices = null!;
    private List<uint> _pendingIndices = null!;
    private readonly Lock _blockLock = new();

    // Pooling to reduce allocations
    private static readonly ConcurrentQueue<List<Vertex>> _vertexListPool = new();
    private static readonly ConcurrentQueue<List<uint>> _indexListPool = new();
    private static int _activeMeshingTasks;
    public static int ActiveMeshingTasks => _activeMeshingTasks;

    static Chunk() {
        // Pre-warm pools
        for (int i = 0; i < 2048; i++) {
            _vertexListPool.Enqueue(new List<Vertex>(2048));
            _indexListPool.Enqueue(new List<uint>(2048));
        }
    }

    public Chunk(VulkanContext ctx, World world, Vector3D<float> position) {
        _ctx = ctx;
        _world = world;
        Position = position;
        _blocks = (BlockType*) UnmanagedPool.Rent((nuint) (BlockCount * sizeof(BlockType)));
        System.Runtime.CompilerServices.Unsafe.InitBlock(_blocks, 0, (uint) (BlockCount * sizeof(BlockType)));
    }

    public void SetBlock(int x, int y, int z, BlockType type) {
        if (x < 0 || x >= Size || y < 0 || y >= Size || z < 0 || z >= Size) return;
        int index = GetIndex(x, y, z);
        lock (_blockLock) {
            if (_blocks == null) return;
            if (_blocks[index] != type) {
                _blocks[index] = type;
                if (type != BlockType.Air) IsEmpty = false;
                IsMeshDirty = true;
            }
        }
    }

    public void SetBlocks(BlockType* newBlocks) {
        if (newBlocks == null) return;
        lock (_blockLock) {
            if (_blocks == null) return;
            System.Buffer.MemoryCopy(newBlocks, _blocks, BlockCount * sizeof(BlockType), BlockCount * sizeof(BlockType));

            // Check if entirely empty
            bool allAir = true;
            for (int i = 0; i < BlockCount; i++) {
                if (newBlocks[i] != BlockType.Air) {
                    allAir = false;
                    break;
                }
            }
            IsEmpty = allAir;
            IsMeshDirty = true;
        }
    }

    public BlockType GetBlock(int x, int y, int z) {
        if (x < 0 || x >= Size || y < 0 || y >= Size || z < 0 || z >= Size) return BlockType.Air;
        lock (_blockLock) {
            if (_blocks == null) return BlockType.Air;
            return _blocks[GetIndex(x, y, z)];
        }
    }

    private int GetIndex(int x, int y, int z) {
        return x + (y * Size) + (z * Size * Size);
    }

    public void MarkDirty() {
        IsMeshDirty = true;
    }

    public bool HasPendingMesh => _hasPendingMesh;

    public (List<Vertex> vertices, List<uint> indices) TakePendingMesh() {
        var v = _pendingVertices;
        var i = _pendingIndices;
        _pendingVertices = null!;
        _pendingIndices = null!;
        _hasPendingMesh = false;
        return (v, i);
    }

    public static void RecycleLists(List<Vertex> vertices, List<uint> indices) {
        if (vertices != null) _vertexListPool.Enqueue(vertices);
        if (indices != null) _indexListPool.Enqueue(indices);
    }

    private struct MeshTaskState {
        public Chunk Chunk;
        public BlockType* Snapshot;
        public nuint SnapshotSize;
    }

    public void Update() {
        if (IsMeshDirty && !_isMeshing) {
            if (IsEmpty) {
                IsMeshDirty = false;
                return;
            }

            _isMeshing = true;
            IsMeshDirty = false;

            nuint bufferSize = (nuint) (BlockCount * sizeof(BlockType));
            BlockType* blocksSnapshot = (BlockType*) UnmanagedPool.Rent(bufferSize);
            lock (_blockLock) {
                if (_blocks == null) {
                    UnmanagedPool.Return(blocksSnapshot, bufferSize);
                    _isMeshing = false;
                    return;
                }
                System.Buffer.MemoryCopy(_blocks, blocksSnapshot, BlockCount * sizeof(BlockType), BlockCount * sizeof(BlockType));
            }

            System.Threading.Interlocked.Increment(ref _activeMeshingTasks);

            var state = new MeshTaskState {
                Chunk = this,
                Snapshot = blocksSnapshot,
                SnapshotSize = bufferSize
            };

            Task.Run(() => {
                var s = state;
                try {
                    s.Chunk.BuildMeshTask(s.Snapshot);
                } catch (Exception e) {
                    Console.WriteLine($"Mesh build failed: {e}");
                } finally {
                    UnmanagedPool.Return(s.Snapshot, s.SnapshotSize);
                    s.Chunk._isMeshing = false;
                    System.Threading.Interlocked.Decrement(ref _activeMeshingTasks);
                }
            });
        }
    }

    private void BuildMeshTask(BlockType* blocks) {
        const int Padding = 1;
        const int PaddedSize = Size + 2 * Padding;
        const int PaddedCount = PaddedSize * PaddedSize * PaddedSize;
        nuint paddedBufferSize = (nuint) (PaddedCount * sizeof(BlockType));

        BlockType* paddedBlocks = (BlockType*) UnmanagedPool.Rent(paddedBufferSize);

        try {
            for (int y = -1; y <= Size; y++) {
                for (int z = -1; z <= Size; z++) {
                    for (int x = -1; x <= Size; x++) {
                        BlockType type;
                        if (x >= 0 && x < Size && y >= 0 && y < Size && z >= 0 && z < Size) {
                            type = blocks[x + (y * Size) + (z * Size * Size)];
                        } else {
                            type = _world.GetBlock((int) Position.X + x, (int) Position.Y + y, (int) Position.Z + z);
                        }
                        paddedBlocks[(x + Padding) + (y + Padding) * PaddedSize + (z + Padding) * PaddedSize * PaddedSize] = type;
                    }
                }
            }

            if (!_vertexListPool.TryDequeue(out var vertices)) {
                vertices = new List<Vertex>(2048);
            } else vertices.Clear();

            if (!_indexListPool.TryDequeue(out var indices)) {
                indices = new List<uint>(2048);
            } else indices.Clear();

            int vertexCount = 0;

            BlockType GetBlockLocal(int x, int y, int z) {
                return paddedBlocks[(x + Padding) + (y + Padding) * PaddedSize + (z + Padding) * PaddedSize * PaddedSize];
            }

            bool IsTransparentLocal(int x, int y, int z) {
                return GetBlockLocal(x, y, z) == BlockType.Air;
            }

            nuint maskSize = (nuint) (Size * Size * sizeof(BlockType));
            BlockType* mask = (BlockType*) UnmanagedPool.Rent(maskSize);
            try {
                // TOP FACE (y + 1)
                for (int y = 0; y < Size; y++) {
                    for (int z = 0; z < Size; z++) {
                        for (int x = 0; x < Size; x++) {
                            mask[x + z * Size] = (IsTransparentLocal(x, y + 1, z) && GetBlockLocal(x, y, z) != BlockType.Air) ? GetBlockLocal(x, y, z) : BlockType.Air;
                        }
                    }
                    for (int j = 0; j < Size; j++) {
                        for (int i = 0; i < Size; i++) {
                            var type = mask[i + j * Size];
                            if (type != BlockType.Air) {
                                int w = 1;
                                while (i + w < Size && mask[(i + w) + j * Size] == type) w++;
                                int h = 1;
                                bool done = false;
                                while (j + h < Size) {
                                    for (int k = 0; k < w; k++) {
                                        if (mask[(i + k) + (j + h) * Size] != type) { done = true; break; }
                                    }
                                    if (done) break;
                                    h++;
                                }
                                AddFaceTop(i, y, j, w, h, (float) type - 1, ref vertexCount, vertices, indices);
                                for (int dy = 0; dy < h; dy++) {
                                    for (int dx = 0; dx < w; dx++) { mask[(i + dx) + (j + dy) * Size] = BlockType.Air; }
                                }
                                i += w - 1;
                            }
                        }
                    }
                }

                // BOTTOM FACE (y - 1)
                for (int y = 0; y < Size; y++) {
                    for (int z = 0; z < Size; z++) {
                        for (int x = 0; x < Size; x++) {
                            mask[x + z * Size] = (IsTransparentLocal(x, y - 1, z) && GetBlockLocal(x, y, z) != BlockType.Air) ? GetBlockLocal(x, y, z) : BlockType.Air;
                        }
                    }
                    for (int j = 0; j < Size; j++) {
                        for (int i = 0; i < Size; i++) {
                            var type = mask[i + j * Size];
                            if (type != BlockType.Air) {
                                int w = 1;
                                while (i + w < Size && mask[(i + w) + j * Size] == type) w++;
                                int h = 1;
                                bool done = false;
                                while (j + h < Size) {
                                    for (int k = 0; k < w; k++) {
                                        if (mask[(i + k) + (j + h) * Size] != type) { done = true; break; }
                                    }
                                    if (done) break;
                                    h++;
                                }
                                AddFaceBottom(i, y, j, w, h, (float) type - 1, ref vertexCount, vertices, indices);
                                for (int dy = 0; dy < h; dy++) {
                                    for (int dx = 0; dx < w; dx++) { mask[(i + dx) + (j + dy) * Size] = BlockType.Air; }
                                }
                                i += w - 1;
                            }
                        }
                    }
                }

                // RIGHT FACE (x + 1)
                for (int x = 0; x < Size; x++) {
                    for (int y = 0; y < Size; y++) {
                        for (int z = 0; z < Size; z++) {
                            mask[z + y * Size] = (IsTransparentLocal(x + 1, y, z) && GetBlockLocal(x, y, z) != BlockType.Air) ? GetBlockLocal(x, y, z) : BlockType.Air;
                        }
                    }
                    for (int j = 0; j < Size; j++) {
                        for (int i = 0; i < Size; i++) {
                            var type = mask[i + j * Size];
                            if (type != BlockType.Air) {
                                int w = 1;
                                while (i + w < Size && mask[(i + w) + j * Size] == type) w++;
                                int h = 1;
                                bool done = false;
                                while (j + h < Size) {
                                    for (int k = 0; k < w; k++) {
                                        if (mask[(i + k) + (j + h) * Size] != type) { done = true; break; }
                                    }
                                    if (done) break;
                                    h++;
                                }
                                AddFaceRight(x, j, i, w, h, (float) type - 1, ref vertexCount, vertices, indices);
                                for (int dy = 0; dy < h; dy++) {
                                    for (int dx = 0; dx < w; dx++) { mask[(i + dx) + (j + dy) * Size] = BlockType.Air; }
                                }
                                i += w - 1;
                            }
                        }
                    }
                }

                // LEFT FACE (x - 1)
                for (int x = 0; x < Size; x++) {
                    for (int y = 0; y < Size; y++) {
                        for (int z = 0; z < Size; z++) {
                            mask[z + y * Size] = (IsTransparentLocal(x - 1, y, z) && GetBlockLocal(x, y, z) != BlockType.Air) ? GetBlockLocal(x, y, z) : BlockType.Air;
                        }
                    }
                    for (int j = 0; j < Size; j++) {
                        for (int i = 0; i < Size; i++) {
                            var type = mask[i + j * Size];
                            if (type != BlockType.Air) {
                                int w = 1;
                                while (i + w < Size && mask[(i + w) + j * Size] == type) w++;
                                int h = 1;
                                bool done = false;
                                while (j + h < Size) {
                                    for (int k = 0; k < w; k++) {
                                        if (mask[(i + k) + (j + h) * Size] != type) { done = true; break; }
                                    }
                                    if (done) break;
                                    h++;
                                }
                                AddFaceLeft(x, j, i, w, h, (float) type - 1, ref vertexCount, vertices, indices);
                                for (int dy = 0; dy < h; dy++) {
                                    for (int dx = 0; dx < w; dx++) { mask[(i + dx) + (j + dy) * Size] = BlockType.Air; }
                                }
                                i += w - 1;
                            }
                        }
                    }
                }

                // FRONT FACE (z + 1)
                for (int z = 0; z < Size; z++) {
                    for (int y = 0; y < Size; y++) {
                        for (int x = 0; x < Size; x++) {
                            mask[x + y * Size] = (IsTransparentLocal(x, y, z + 1) && GetBlockLocal(x, y, z) != BlockType.Air) ? GetBlockLocal(x, y, z) : BlockType.Air;
                        }
                    }
                    for (int j = 0; j < Size; j++) {
                        for (int i = 0; i < Size; i++) {
                            var type = mask[i + j * Size];
                            if (type != BlockType.Air) {
                                int w = 1;
                                while (i + w < Size && mask[(i + w) + j * Size] == type) w++;
                                int h = 1;
                                bool done = false;
                                while (j + h < Size) {
                                    for (int k = 0; k < w; k++) {
                                        if (mask[(i + k) + (j + h) * Size] != type) { done = true; break; }
                                    }
                                    if (done) break;
                                    h++;
                                }
                                AddFaceFront(i, j, z, w, h, (float) type - 1, ref vertexCount, vertices, indices);
                                for (int dy = 0; dy < h; dy++) {
                                    for (int dx = 0; dx < w; dx++) { mask[(i + dx) + (j + dy) * Size] = BlockType.Air; }
                                }
                                i += w - 1;
                            }
                        }
                    }
                }

                // BACK FACE (z - 1)
                for (int z = 0; z < Size; z++) {
                    for (int y = 0; y < Size; y++) {
                        for (int x = 0; x < Size; x++) {
                            mask[x + y * Size] = (IsTransparentLocal(x, y, z - 1) && GetBlockLocal(x, y, z) != BlockType.Air) ? GetBlockLocal(x, y, z) : BlockType.Air;
                        }
                    }
                    for (int j = 0; j < Size; j++) {
                        for (int i = 0; i < Size; i++) {
                            var type = mask[i + j * Size];
                            if (type != BlockType.Air) {
                                int w = 1;
                                while (i + w < Size && mask[(i + w) + j * Size] == type) w++;
                                int h = 1;
                                bool done = false;
                                while (j + h < Size) {
                                    for (int k = 0; k < w; k++) {
                                        if (mask[(i + k) + (j + h) * Size] != type) { done = true; break; }
                                    }
                                    if (done) break;
                                    h++;
                                }
                                AddFaceBack(i, j, z, w, h, (float) type - 1, ref vertexCount, vertices, indices);
                                for (int dy = 0; dy < h; dy++) {
                                    for (int dx = 0; dx < w; dx++) { mask[(i + dx) + (j + dy) * Size] = BlockType.Air; }
                                }
                                i += w - 1;
                            }
                        }
                    }
                }

            } finally {
                UnmanagedPool.Return(mask, maskSize);
            }

            _pendingVertices = vertices;
            _pendingIndices = indices;
            _hasPendingMesh = true;
        } finally {
            UnmanagedPool.Return(paddedBlocks, paddedBufferSize);
            _isMeshing = false;
        }
    }

    private void AddFaceFront(float x, float y, float z, float w, float h, float texIndex, ref int vCount, List<Vertex> verts, List<uint> inds) {
        verts.Add(new Vertex(new Vector3D<float>(x, y, z + 1), new Vector2D<float>(0, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + w, y, z + 1), new Vector2D<float>(w, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + w, y + h, z + 1), new Vector2D<float>(w, h), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y + h, z + 1), new Vector2D<float>(0, h), texIndex));
        AddIndices(ref vCount, inds);
    }

    private void AddFaceBack(float x, float y, float z, float w, float h, float texIndex, ref int vCount, List<Vertex> verts, List<uint> inds) {
        verts.Add(new Vertex(new Vector3D<float>(x + w, y, z), new Vector2D<float>(0, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y, z), new Vector2D<float>(w, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y + h, z), new Vector2D<float>(w, h), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + w, y + h, z), new Vector2D<float>(0, h), texIndex));
        AddIndices(ref vCount, inds);
    }

    private void AddFaceTop(float x, float y, float z, float w, float h, float texIndex, ref int vCount, List<Vertex> verts, List<uint> inds) {
        verts.Add(new Vertex(new Vector3D<float>(x, y + 1, z + h), new Vector2D<float>(0, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + w, y + 1, z + h), new Vector2D<float>(w, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + w, y + 1, z), new Vector2D<float>(w, h), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y + 1, z), new Vector2D<float>(0, h), texIndex));
        AddIndices(ref vCount, inds);
    }

    private void AddFaceBottom(float x, float y, float z, float w, float h, float texIndex, ref int vCount, List<Vertex> verts, List<uint> inds) {
        verts.Add(new Vertex(new Vector3D<float>(x, y, z), new Vector2D<float>(0, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + w, y, z), new Vector2D<float>(w, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x + w, y, z + h), new Vector2D<float>(w, h), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y, z + h), new Vector2D<float>(0, h), texIndex));
        AddIndices(ref vCount, inds);
    }

    private void AddFaceLeft(float x, float y, float z, float w, float h, float texIndex, ref int vCount, List<Vertex> verts, List<uint> inds) {
        verts.Add(new Vertex(new Vector3D<float>(x, y, z), new Vector2D<float>(0, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y, z + w), new Vector2D<float>(w, 0), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y + h, z + w), new Vector2D<float>(w, h), texIndex));
        verts.Add(new Vertex(new Vector3D<float>(x, y + h, z), new Vector2D<float>(0, h), texIndex));
        AddIndices(ref vCount, inds);
    }

    private void AddFaceRight(float x, float y, float z, float w, float h, float texIndex, ref int vCount, List<Vertex> verts, List<uint> inds) {
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

    public void Dispose() {
        lock (_blockLock) {
            if (IsDisposed) return;
            IsDisposed = true;
            if (_blocks != null) {
                UnmanagedPool.Return(_blocks, (nuint) (BlockCount * sizeof(BlockType)));
                _blocks = null;
            }
        }
        GC.SuppressFinalize(this);
    }

    ~Chunk() {
        Dispose();
    }
}
