using System;
using System.Collections.Generic;
using Silk.NET.Maths;

namespace Shiron.VulkanDumpster.Voxels.Generation;

public class DefaultWorldGenerator : IWorldGenerator {
    private int _seed;

    public void Initialize(int seed) {
        _seed = seed;
    }

    public Dictionary<string, string> GetDebugData(int x, int z) {
        float noise = MathF.Sin(x * 0.05f) * 10 + MathF.Cos(z * 0.05f) * 10 + 64;
        return new Dictionary<string, string> {
            { "Height", noise.ToString("F2") }
        };
    }

    public unsafe void Generate(YChunk chunk) {
        if (chunk == null) return;
        Vector2D<int> chunkPos = chunk.ChunkPos;
        int baseX = chunkPos.X * Chunk.Size;
        int baseZ = chunkPos.Y * Chunk.Size;
        int totalBlockCount = Chunk.Size * Chunk.Size * YChunk.TotalHeight;
        
        BlockType* columnBlocks = (BlockType*)UnmanagedPool.Rent((nuint)(totalBlockCount * sizeof(BlockType)));
        if (columnBlocks == null) {
            throw new OutOfMemoryException("Failed to rent memory for columnBlocks");
        }
        System.Runtime.CompilerServices.Unsafe.InitBlock(columnBlocks, 0, (uint)(totalBlockCount * sizeof(BlockType)));

        try {
            for (int x = 0; x < Chunk.Size; x++) {
                for (int z = 0; z < Chunk.Size; z++) {
                    int worldX = baseX + x;
                    int worldZ = baseZ + z;
                    
                    // Simple sine-based terrain for now
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
                chunk.SetChunkBlocks(i, columnBlocks + (i * Chunk.BlockCount));
            }
        } finally {
            UnmanagedPool.Return(columnBlocks, (nuint)(totalBlockCount * sizeof(BlockType)));
        }
    }
}
