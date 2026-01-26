using System;
using System.Collections.Generic;
using Silk.NET.Maths;

namespace Shiron.VulkanDumpster.Voxels.Generation;

public class PerlinWorldGenerator : IWorldGenerator {
    private FastNoiseLite _noise = null!;
    private int _seed;

    public void Initialize(int seed) {
        _seed = seed;
        _noise = new FastNoiseLite(seed);
        _noise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        _noise.SetFrequency(0.01f);
        _noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        _noise.SetFractalOctaves(4);
    }

    public Dictionary<string, string> GetDebugData(int x, int z) {
        float noiseValue = _noise.GetNoise(x, z);
        return new Dictionary<string, string> {
            { "Perlin", noiseValue.ToString("F3") },
            { "Height", ((noiseValue + 1.0f) * 0.5f * 64.0f + 32).ToString("F1") }
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
                    
                    float noiseValue = _noise.GetNoise(worldX, worldZ);
                    // Map noise (-1 to 1) to height (e.g., 32 to 96)
                    int height = (int)((noiseValue + 1.0f) * 0.5f * 64.0f) + 32;
                    height = Math.Clamp(height, 1, YChunk.TotalHeight - 1);
                    
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
