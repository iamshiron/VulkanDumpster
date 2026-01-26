using System.Collections.Generic;
using Silk.NET.Maths;

namespace Shiron.VulkanDumpster.Voxels.Generation;

public interface IWorldGenerator {
    void Initialize(int seed);
    void Generate(YChunk chunk);
    Dictionary<string, string> GetDebugData(int x, int z);
}
