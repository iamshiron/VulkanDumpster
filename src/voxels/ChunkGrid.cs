using System.Collections.Generic;
using Silk.NET.Maths;

namespace Shiron.VulkanDumpster.Voxels;

/// <summary>
/// A circular buffer for chunks. Maps world chunk coordinates to a fixed-size 2D array.
/// </summary>
public class ChunkGrid {
    private readonly YChunk?[,] _grid;
    private readonly int _size;
    private readonly int _offset;

    public int Size => _size;

    public ChunkGrid(int renderDistance) {
        _size = renderDistance * 2 + 1;
        _offset = renderDistance;
        _grid = new YChunk?[_size, _size];
    }

    public YChunk? GetChunk(Vector2D<int> pos) {
        int x = Mod(pos.X, _size);
        int z = Mod(pos.Y, _size);
        var chunk = _grid[x, z];
        if (chunk != null && chunk.ChunkPos == pos) {
            return chunk;
        }
        return null;
    }

    public bool SetChunk(Vector2D<int> pos, YChunk? chunk) {
        int x = Mod(pos.X, _size);
        int z = Mod(pos.Y, _size);
        _grid[x, z] = chunk;
        return true;
    }

    private int Mod(int n, int m) {
        return ((n % m) + m) % m;
    }

    public IEnumerable<YChunk> GetAllActive() {
        for (int x = 0; x < _size; x++) {
            for (int z = 0; z < _size; z++) {
                if (_grid[x, z] != null) yield return _grid[x, z]!;
            }
        }
    }
}
