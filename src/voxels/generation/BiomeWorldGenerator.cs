using System;
using System.Collections.Generic;
using Silk.NET.Maths;

namespace Shiron.VulkanDumpster.Voxels.Generation;

public class BiomeWorldGenerator : IWorldGenerator {
    private FastNoiseLite _heightNoise = null!;
    private FastNoiseLite _tempNoise = null!;
    private FastNoiseLite _moistureNoise = null!;
    private int _seed;

    public void Initialize(int seed) {
        _seed = seed;
        
        _heightNoise = new FastNoiseLite(seed);
        _heightNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        _heightNoise.SetFrequency(0.005f);
        _heightNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        _heightNoise.SetFractalOctaves(5);

        _tempNoise = new FastNoiseLite(seed + 1);
        _tempNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        _tempNoise.SetFrequency(0.002f);

        _moistureNoise = new FastNoiseLite(seed + 2);
        _moistureNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        _moistureNoise.SetFrequency(0.002f);
    }

    public Dictionary<string, string> GetDebugData(int x, int z) {
        float t = (_tempNoise.GetNoise(x, z) + 1.0f) * 0.5f;
        float m = (_moistureNoise.GetNoise(x, z) + 1.0f) * 0.5f;
        float h = (_heightNoise.GetNoise(x, z) + 1.0f) * 0.5f;
        Biome biome = BiomeManager.GetBiome(t, m);
        
        return new Dictionary<string, string> {
            { "Biome", biome.Name },
            { "Temp", t.ToString("F3") },
            { "Moisture", m.ToString("F3") },
            { "HeightNoise", h.ToString("F3") },
            { "BiomeID", biome.Id.ToString() }
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
                    
                    float avgScale = 0;
                    float avgOffset = 0;
                    
                    // Smoothing: sample biome parameters in a 5x5 grid
                    const int SmoothRadius = 2; 
                    const int SmoothStep = 4;
                    const float SampleCount = (SmoothRadius * 2 + 1) * (SmoothRadius * 2 + 1);

                    for (int ox = -SmoothRadius; ox <= SmoothRadius; ox++) {
                        for (int oz = -SmoothRadius; oz <= SmoothRadius; oz++) {
                            float sampleX = worldX + ox * SmoothStep;
                            float sampleZ = worldZ + oz * SmoothStep;
                            
                            float lt = (_tempNoise.GetNoise(sampleX, sampleZ) + 1.0f) * 0.5f;
                            float lm = (_moistureNoise.GetNoise(sampleX, sampleZ) + 1.0f) * 0.5f;
                            
                            Biome b = BiomeManager.GetBiome(lt, lm);
                            avgScale += b.HeightScale;
                            avgOffset += b.HeightOffset;
                        }
                    }
                    
                    avgScale /= SampleCount;
                    avgOffset /= SampleCount;
                    
                    // Use center biome for block types
                    float t = (_tempNoise.GetNoise(worldX, worldZ) + 1.0f) * 0.5f;
                    float m = (_moistureNoise.GetNoise(worldX, worldZ) + 1.0f) * 0.5f;
                    Biome centerBiome = BiomeManager.GetBiome(t, m);
                    
                    float heightNoise = (_heightNoise.GetNoise(worldX, worldZ) + 1.0f) * 0.5f;
                    int height = (int)(heightNoise * avgScale + avgOffset);
                    height = Math.Clamp(height, 1, YChunk.TotalHeight - 1);
                    
                    for (int y = 0; y < height; y++) {
                        BlockType type = BlockType.Stone;
                        if (y == height - 1) type = centerBiome.SurfaceBlock;
                        else if (y > height - 5) type = centerBiome.SubSurfaceBlock;
                        
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
