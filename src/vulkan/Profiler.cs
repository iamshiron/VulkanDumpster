using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Silk.NET.Vulkan;

namespace Shiron.VulkanDumpster.Vulkan;

public static class Profiler {
    private static readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private static readonly Dictionary<string, long> _activeTimers = new();
    private static readonly Dictionary<string, (List<double> history, double max)> _history = new();
    private const int HistorySize = 60;

    public static void Begin(string name) {
        _activeTimers[name] = _stopwatch.ElapsedTicks;
    }

    public static void End(string name) {
        if (_activeTimers.TryGetValue(name, out long startTicks)) {
            double elapsedMs = (double)(_stopwatch.ElapsedTicks - startTicks) / Stopwatch.Frequency * 1000.0;
            if (!_history.ContainsKey(name)) {
                _history[name] = (new List<double>(HistorySize), 0);
            }
            
            var (list, max) = _history[name];
            list.Add(elapsedMs);
            if (list.Count > HistorySize) {
                list.RemoveAt(0);
            }
            
            _history[name] = (list, Math.Max(max, elapsedMs));
            _activeTimers.Remove(name);
        }
    }

    public struct ProfileResult {
        public string Name;
        public double Average;
        public double Max;
    }

    public static List<ProfileResult> GetResults() {
        var results = new List<ProfileResult>(_history.Count);
        foreach (var pair in _history) {
            var history = pair.Value.history;
            if (history.Count > 0) {
                double sum = 0;
                for (int i = 0; i < history.Count; i++) {
                    sum += history[i];
                }
                results.Add(new ProfileResult {
                    Name = pair.Key,
                    Average = sum / history.Count,
                    Max = pair.Value.max
                });
            }
        }
        return results;
    }

    public static void ForEachResult(Action<string, double, double> action) {
        foreach (var pair in _history) {
            var history = pair.Value.history;
            if (history.Count > 0) {
                double sum = 0;
                for (int i = 0; i < history.Count; i++) {
                    sum += history[i];
                }
                action(pair.Key, sum / history.Count, pair.Value.max);
            }
        }
    }

    public static Dictionary<string, (double avg, double max)> GetAverageResults() {
        var results = new Dictionary<string, (double avg, double max)>();
        ForEachResult((name, avg, max) => results[name] = (avg, max));
        return results;
    }
    
    public static void ResetMax() {
        var keys = new List<string>(_history.Keys);
        foreach (var name in keys) {
            _history[name] = (_history[name].history, 0);
        }
    }

    public class ProfileSnapshot {
        public DateTime Timestamp { get; set; }
        public FrameMetadata Frame { get; set; } = new();
        public Dictionary<string, CpuSectionStats> CpuStats { get; set; } = new();
        public Dictionary<string, double> GpuTimesMs { get; set; } = new();
        public VulkanStats Vulkan { get; set; } = new();
        public MemoryProfiler.MemoryStats Memory { get; set; }
        public IReadOnlyDictionary<string, int> ShaderExecutions { get; set; } = new Dictionary<string, int>();

        public class CpuSectionStats {
            public double AverageMs { get; set; }
            public double PeakMs { get; set; }
        }

        public class FrameMetadata {
            public double DeltaTime { get; set; }
            public float AverageFPS { get; set; }
            public float MedianFPS { get; set; }
            public float MinFPS { get; set; }
            public float MaxFPS { get; set; }
            public float Low1PercentFPS { get; set; }
            public float Low01PercentFPS { get; set; }
            public Vector3 CameraPosition { get; set; }
            public int TotalChunks { get; set; }
            public int RenderedChunks { get; set; }
            public int TotalRegions { get; set; }
            public int RenderedRegions { get; set; }
            public int ChunkUpdates { get; set; }
            public float AllocPerFrameKB { get; set; }
        }

        public class VulkanStats {
            public int DrawCalls { get; set; }
            public int PipelineBinds { get; set; }
            public int DescriptorSetBinds { get; set; }
            public int VertexBufferBinds { get; set; }
            public int IndexBufferBinds { get; set; }
            public int PushConstants { get; set; }
        }
    }

    public static void SaveProfile(string path, Dictionary<string, double> gpuResults, ProfileSnapshot.FrameMetadata metadata) {
        var avgResults = GetAverageResults();
        var snapshot = new ProfileSnapshot {
            Timestamp = DateTime.Now,
            Frame = metadata,
            CpuStats = avgResults.ToDictionary(k => k.Key, v => new ProfileSnapshot.CpuSectionStats {
                AverageMs = v.Value.avg,
                PeakMs = v.Value.max
            }),
            GpuTimesMs = gpuResults,
            Vulkan = new ProfileSnapshot.VulkanStats {
                DrawCalls = VulkanCommandProfiler.DrawCalls,
                PipelineBinds = VulkanCommandProfiler.PipelineBinds,
                DescriptorSetBinds = VulkanCommandProfiler.DescriptorSetBinds,
                VertexBufferBinds = VulkanCommandProfiler.VertexBufferBinds,
                IndexBufferBinds = VulkanCommandProfiler.IndexBufferBinds,
                PushConstants = VulkanCommandProfiler.PushConstantsCount
            },
            Memory = MemoryProfiler.GetStats(),
            ShaderExecutions = VulkanCommandProfiler.ShaderExecutions
        };

        string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(path, json);
        Console.WriteLine($"[Profiler] Saved performance snapshot to: {path}");
    }
}

public unsafe class GPUProfiler : IDisposable {
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly uint _maxQueries;
    private readonly double _timestampPeriod;
    private readonly int _frameCount;

    private struct FrameQueries {
        public QueryPool Pool;
        public List<string> Names;
        public Dictionary<string, (uint start, uint end)> Sections;
        public uint QueryCount;
        public bool IsSubmitted;
    }

    private readonly FrameQueries[] _frames;
    private int _currentFrameIndex;
    private Dictionary<string, double> _lastResults = new();
    private readonly ulong[] _queryResultsBuffer;

    public GPUProfiler(Vk vk, Device device, PhysicalDevice physicalDevice, int frameCount, uint maxSections = 32) {
        _vk = vk;
        _device = device;
        _frameCount = frameCount;
        _maxQueries = maxSections * 2;
        _queryResultsBuffer = new ulong[_maxQueries];

        _vk.GetPhysicalDeviceProperties(physicalDevice, out var properties);
        _timestampPeriod = properties.Limits.TimestampPeriod;

        _frames = new FrameQueries[frameCount];
        for (int i = 0; i < frameCount; i++) {
            var createInfo = new QueryPoolCreateInfo {
                SType = StructureType.QueryPoolCreateInfo,
                QueryType = QueryType.Timestamp,
                QueryCount = _maxQueries
            };

            if (_vk.CreateQueryPool(_device, &createInfo, null, out var pool) != Result.Success) {
                throw new Exception("Failed to create GPU query pool");
            }
            
            _frames[i] = new FrameQueries {
                Pool = pool,
                Names = new List<string>(),
                Sections = new Dictionary<string, (uint start, uint end)>(),
                QueryCount = 0,
                IsSubmitted = false
            };
        }
    }

    public void BeginFrame(int frameIndex) {
        _currentFrameIndex = frameIndex;
        ref var frame = ref _frames[_currentFrameIndex];
        
        if (frame.IsSubmitted) {
            FetchResults(ref frame);
        }

        frame.QueryCount = 0;
        frame.Sections.Clear();
        frame.Names.Clear();
        frame.IsSubmitted = false;
    }

    private void FetchResults(ref FrameQueries frame) {
        if (frame.QueryCount == 0) return;

        fixed (ulong* pResults = _queryResultsBuffer) {
            var res = _vk.GetQueryPoolResults(_device, frame.Pool, 0, frame.QueryCount, 
                (nuint)(frame.QueryCount * sizeof(ulong)), pResults, sizeof(ulong), 
                QueryResultFlags.Result64Bit);
            
            if (res != Result.Success) return;
        }

        // We clear the dictionary instead of creating a new one
        _lastResults.Clear();
        foreach (var name in frame.Names) {
            var (start, end) = frame.Sections[name];
            if (end > start) {
                ulong duration = _queryResultsBuffer[end] - _queryResultsBuffer[start];
                _lastResults[name] = (duration * _timestampPeriod) / 1000000.0;
            }
        }
    }

    public void Reset(CommandBuffer cmd) {
        _vk.CmdResetQueryPool(cmd, _frames[_currentFrameIndex].Pool, 0, _maxQueries);
    }

    public void BeginSection(CommandBuffer cmd, string name) {
        ref var frame = ref _frames[_currentFrameIndex];
        if (frame.QueryCount + 2 > _maxQueries) return;
        
        uint start = frame.QueryCount++;
        frame.Sections[name] = (start, 0);
        frame.Names.Add(name);
        _vk.CmdWriteTimestamp(cmd, PipelineStageFlags.BottomOfPipeBit, frame.Pool, start);
    }

    public void EndSection(CommandBuffer cmd, string name) {
        ref var frame = ref _frames[_currentFrameIndex];
        if (frame.Sections.TryGetValue(name, out var indices)) {
            uint end = frame.QueryCount++;
            frame.Sections[name] = (indices.start, end);
            _vk.CmdWriteTimestamp(cmd, PipelineStageFlags.BottomOfPipeBit, frame.Pool, end);
        }
    }

    public void MarkSubmitted() {
        _frames[_currentFrameIndex].IsSubmitted = true;
    }

    public void ForEachLatestResult(Action<string, double> action) {
        foreach (var pair in _lastResults) {
            action(pair.Key, pair.Value);
        }
    }

    public Dictionary<string, double> GetLatestResults() => _lastResults;

    public void Dispose() {
        foreach (var frame in _frames) {
            _vk.DestroyQueryPool(_device, frame.Pool, null);
        }
    }
}

public static class MemoryProfiler {
    private static readonly Process _currentProcess = Process.GetCurrentProcess();
    private static MemoryStats _cachedStats;
    private static long _lastRefreshTimestamp;
    private static readonly long RefreshIntervalTicks = Stopwatch.Frequency; // 1 second

    public struct MemoryStats {
        public double WorkingSetMB { get; set; }
        public double PrivateMemoryMB { get; set; }
        public double ManagedMemoryMB { get; set; }
        public long TotalAllocatedBytes { get; set; }
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }
        public long HeapSizeMB { get; set; }
        public long TotalCommittedMB { get; set; }
        public double PromotionRateMB { get; set; }
    }

    public static MemoryStats GetStats() {
        long currentTimestamp = Stopwatch.GetTimestamp();
        if (currentTimestamp - _lastRefreshTimestamp >= RefreshIntervalTicks) {
            _currentProcess.Refresh();
            var gcInfo = GC.GetGCMemoryInfo();
            _cachedStats = new MemoryStats {
                WorkingSetMB = _currentProcess.WorkingSet64 / 1024.0 / 1024.0,
                PrivateMemoryMB = _currentProcess.PrivateMemorySize64 / 1024.0 / 1024.0,
                ManagedMemoryMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0,
                TotalAllocatedBytes = GC.GetTotalAllocatedBytes(),
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2),
                HeapSizeMB = gcInfo.HeapSizeBytes / 1024 / 1024,
                TotalCommittedMB = gcInfo.TotalCommittedBytes / 1024 / 1024,
                PromotionRateMB = gcInfo.PromotedBytes / 1024.0 / 1024.0
            };
            _lastRefreshTimestamp = currentTimestamp;
        }
        return _cachedStats;
    }
}

public static class VulkanCommandProfiler {
    public static int DrawCalls { get; private set; }
    public static int PipelineBinds { get; private set; }
    public static int DescriptorSetBinds { get; private set; }
    public static int VertexBufferBinds { get; private set; }
    public static int IndexBufferBinds { get; private set; }
    public static int PushConstantsCount { get; private set; }
    private static readonly Dictionary<string, int> _shaderExecutions = new();
    public static IReadOnlyDictionary<string, int> ShaderExecutions => _shaderExecutions;

    public static void IncrementDrawCalls() => DrawCalls++;
    public static void IncrementPipelineBinds() => PipelineBinds++;
    public static void IncrementDescriptorSetBinds() => DescriptorSetBinds++;
    public static void IncrementVertexBufferBinds() => VertexBufferBinds++;
    public static void IncrementIndexBufferBinds() => IndexBufferBinds++;
    public static void IncrementPushConstants() => PushConstantsCount++;
    public static void IncrementShaderExecution(string shaderName) {
        _shaderExecutions[shaderName] = _shaderExecutions.GetValueOrDefault(shaderName) + 1;
    }

    public static void ForEachShaderExecution(Action<string, int> action) {
        foreach (var pair in _shaderExecutions) {
            action(pair.Key, pair.Value);
        }
    }

    public static void Reset() {
        DrawCalls = 0;
        PipelineBinds = 0;
        DescriptorSetBinds = 0;
        VertexBufferBinds = 0;
        IndexBufferBinds = 0;
        PushConstantsCount = 0;
        _shaderExecutions.Clear();
    }
}
