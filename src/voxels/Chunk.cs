using System;
using Silk.NET.Maths;
using Shiron.VulkanDumpster.Vulkan;

namespace Shiron.VulkanDumpster.Voxels;

public class Chunk {
    public const int Size = 32;
    public readonly BlockType[] Blocks = new BlockType[Size * Size * Size];
    public bool IsMeshDirty { get; private set; } = true;
    public Vector3D<float> Position { get; private set; }

    private readonly VulkanContext _ctx;
    public Mesh Mesh { get; private set; }

    public Chunk(VulkanContext ctx, Vector3D<float> position) {
        _ctx = ctx;
        Position = position;
        Mesh = new Mesh(_ctx);
    }

    public void SetBlock(int x, int y, int z, BlockType type) {
        if (x < 0 || x >= Size || y < 0 || y >= Size || z < 0 || z >= Size) return;
        
        int index = GetIndex(x, y, z);
        if (Blocks[index] != type) {
            Blocks[index] = type;
            IsMeshDirty = true;
        }
    }

    public BlockType GetBlock(int x, int y, int z) {
        if (x < 0 || x >= Size || y < 0 || y >= Size || z < 0 || z >= Size) return BlockType.Air;
        return Blocks[GetIndex(x, y, z)];
    }

    private int GetIndex(int x, int y, int z) {
        return x + (y * Size) + (z * Size * Size);
    }

    public void Update() {
        if (IsMeshDirty) {
            RebuildMesh();
            IsMeshDirty = false;
        }
    }

    private void RebuildMesh() {
        Mesh.Clear();
        int vertexCount = 0;

        for (int x = 0; x < Size; x++) {
            for (int y = 0; y < Size; y++) {
                for (int z = 0; z < Size; z++) {
                    var type = GetBlock(x, y, z);
                    if (type == BlockType.Air) continue;

                    float texIndex = (float)type - 1;

                    // Check neighbors (simple culling)
                    if (IsTransparent(x, y, z + 1)) AddFaceFront(x, y, z, texIndex, ref vertexCount);
                    if (IsTransparent(x, y, z - 1)) AddFaceBack(x, y, z, texIndex, ref vertexCount);
                    if (IsTransparent(x, y + 1, z)) AddFaceTop(x, y, z, texIndex, ref vertexCount);
                    if (IsTransparent(x, y - 1, z)) AddFaceBottom(x, y, z, texIndex, ref vertexCount);
                    if (IsTransparent(x - 1, y, z)) AddFaceLeft(x, y, z, texIndex, ref vertexCount);
                    if (IsTransparent(x + 1, y, z)) AddFaceRight(x, y, z, texIndex, ref vertexCount);
                }
            }
        }

        Mesh.Build();
        Mesh.UpdateGpuBuffers();
    }

    private bool IsTransparent(int x, int y, int z) {
        return GetBlock(x, y, z) == BlockType.Air;
    }

    private void AddFaceFront(float x, float y, float z, float texIndex, ref int vCount) {
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x, y, z + 1), new Vector2D<float>(0, 0), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x + 1, y, z + 1), new Vector2D<float>(1, 0), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x + 1, y + 1, z + 1), new Vector2D<float>(1, 1), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x, y + 1, z + 1), new Vector2D<float>(0, 1), texIndex));
        AddIndices(ref vCount);
    }

    private void AddFaceBack(float x, float y, float z, float texIndex, ref int vCount) {
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x + 1, y, z), new Vector2D<float>(0, 0), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x, y, z), new Vector2D<float>(1, 0), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x, y + 1, z), new Vector2D<float>(1, 1), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x + 1, y + 1, z), new Vector2D<float>(0, 1), texIndex));
        AddIndices(ref vCount);
    }

    private void AddFaceTop(float x, float y, float z, float texIndex, ref int vCount) {
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x, y + 1, z + 1), new Vector2D<float>(0, 0), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x + 1, y + 1, z + 1), new Vector2D<float>(1, 0), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x + 1, y + 1, z), new Vector2D<float>(1, 1), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x, y + 1, z), new Vector2D<float>(0, 1), texIndex));
        AddIndices(ref vCount);
    }

    private void AddFaceBottom(float x, float y, float z, float texIndex, ref int vCount) {
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x, y, z), new Vector2D<float>(0, 0), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x + 1, y, z), new Vector2D<float>(1, 0), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x + 1, y, z + 1), new Vector2D<float>(1, 1), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x, y, z + 1), new Vector2D<float>(0, 1), texIndex));
        AddIndices(ref vCount);
    }

    private void AddFaceLeft(float x, float y, float z, float texIndex, ref int vCount) {
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x, y, z), new Vector2D<float>(0, 0), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x, y, z + 1), new Vector2D<float>(1, 0), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x, y + 1, z + 1), new Vector2D<float>(1, 1), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x, y + 1, z), new Vector2D<float>(0, 1), texIndex));
        AddIndices(ref vCount);
    }

    private void AddFaceRight(float x, float y, float z, float texIndex, ref int vCount) {
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x + 1, y, z + 1), new Vector2D<float>(0, 0), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x + 1, y, z), new Vector2D<float>(1, 0), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x + 1, y + 1, z), new Vector2D<float>(1, 1), texIndex));
        Mesh.AddVertex(new Vertex(new Vector3D<float>(x + 1, y + 1, z + 1), new Vector2D<float>(0, 1), texIndex));
        AddIndices(ref vCount);
    }

    private void AddIndices(ref int vCount) {
        Mesh.AddIndex((uint)(vCount + 0));
        Mesh.AddIndex((uint)(vCount + 1));
        Mesh.AddIndex((uint)(vCount + 2));
        Mesh.AddIndex((uint)(vCount + 2));
        Mesh.AddIndex((uint)(vCount + 3));
        Mesh.AddIndex((uint)(vCount + 0));
        vCount += 4;
    }
}
