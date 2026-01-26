using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Shiron.VulkanDumpster.Vulkan;

public static class Profiler {
    private static readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private static readonly Dictionary<string, long> _activeTimers = new();
    private static readonly Dictionary<string, List<double>> _history = new();
    private const int HistorySize = 60;

    public static void Begin(string name) {
        _activeTimers[name] = _stopwatch.ElapsedTicks;
    }

    public static void End(string name) {
        if (_activeTimers.TryGetValue(name, out long startTicks)) {
            double elapsedMs = (double)(_stopwatch.ElapsedTicks - startTicks) / Stopwatch.Frequency * 1000.0;
            if (!_history.ContainsKey(name)) {
                _history[name] = new List<double>(HistorySize);
            }
            var list = _history[name];
            list.Add(elapsedMs);
            if (list.Count > HistorySize) {
                list.RemoveAt(0);
            }
            _activeTimers.Remove(name);
        }
    }

    public static Dictionary<string, double> GetAverageResults() {
        var results = new Dictionary<string, double>();
        foreach (var pair in _history) {
            if (pair.Value.Count > 0) {
                results[pair.Key] = pair.Value.Average();
            }
        }
        return results;
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

    public GPUProfiler(Vk vk, Device device, PhysicalDevice physicalDevice, int frameCount, uint maxSections = 32) {
        _vk = vk;
        _device = device;
        _frameCount = frameCount;
        _maxQueries = maxSections * 2;

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

            // Initial reset on host if supported, or we'll just wait for the first CmdReset
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

        ulong[] results = new ulong[frame.QueryCount];
        fixed (ulong* pResults = results) {
            // We don't use WAIT_BIT here because we call this after waiting for the frame fence
            var res = _vk.GetQueryPoolResults(_device, frame.Pool, 0, frame.QueryCount, 
                (nuint)(results.Length * sizeof(ulong)), pResults, sizeof(ulong), 
                QueryResultFlags.Result64Bit);
            
            if (res != Result.Success) return;
        }

        var dict = new Dictionary<string, double>();
        foreach (var name in frame.Names) {
            var (start, end) = frame.Sections[name];
            if (end > start) {
                ulong duration = results[end] - results[start];
                dict[name] = (duration * _timestampPeriod) / 1000000.0;
            }
        }
        _lastResults = dict;
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

    public Dictionary<string, double> GetLatestResults() => _lastResults;

    public void Dispose() {
        foreach (var frame in _frames) {
            _vk.DestroyQueryPool(_device, frame.Pool, null);
        }
    }
}