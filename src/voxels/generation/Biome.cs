using System;

namespace Shiron.VulkanDumpster.Voxels.Generation;

public class Biome {
    public int Id { get; init; } = 0;
    public string Name { get; init; } = "Default";
    public BlockType SurfaceBlock { get; init; } = BlockType.Grass;
    public BlockType SubSurfaceBlock { get; init; } = BlockType.Dirt;
    public float HeightScale { get; init; } = 64.0f;
    public float HeightOffset { get; init; } = 32.0f;
    
    public static readonly Biome Plains = new Biome {
        Id = 1,
        Name = "Plains",
        SurfaceBlock = BlockType.Grass,
        SubSurfaceBlock = BlockType.Dirt,
        HeightScale = 20.0f,
        HeightOffset = 60.0f
    };

    public static readonly Biome Desert = new Biome {
        Id = 2,
        Name = "Desert",
        SurfaceBlock = BlockType.Sand,
        SubSurfaceBlock = BlockType.Sand,
        HeightScale = 10.0f,
        HeightOffset = 64.0f
    };

    public static readonly Biome Mountains = new Biome {
        Id = 3,
        Name = "Mountains",
        SurfaceBlock = BlockType.Stone,
        SubSurfaceBlock = BlockType.Stone,
        HeightScale = 100.0f,
        HeightOffset = 80.0f
    };

    public static readonly Biome Forest = new Biome {
        Id = 4,
        Name = "Forest",
        SurfaceBlock = BlockType.Grass,
        SubSurfaceBlock = BlockType.Dirt,
        HeightScale = 40.0f,
        HeightOffset = 64.0f
    };
}

public static class BiomeManager {
    public static Biome GetBiome(float temperature, float moisture) {
        // Simple 2D lookup
        if (temperature < 0.3f) {
            return moisture < 0.5f ? Biome.Mountains : Biome.Forest;
        } else if (temperature > 0.7f) {
            return moisture < 0.3f ? Biome.Desert : Biome.Plains;
        } else {
            return moisture > 0.6f ? Biome.Forest : Biome.Plains;
        }
    }
}
