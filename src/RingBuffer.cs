using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Shiron.VulkanDumpster;

/// <summary>
/// A generic, high-performance ring buffer using unmanaged memory.
/// Supports rolling average and median calculations.
/// </summary>
public unsafe class RingBuffer<T> : IDisposable where T : unmanaged, INumber<T> {
    private T* _buffer;
    private readonly int _capacity;
    private int _head;
    private int _count;
    private T _sum;

    public int Count => _count;
    public int Capacity => _capacity;

    public RingBuffer(int capacity) {
        if (capacity <= 0) throw new ArgumentException("Capacity must be greater than zero.");
        _capacity = capacity;
        _buffer = (T*)NativeMemory.Alloc((nuint)_capacity, (nuint)sizeof(T));
        _sum = T.Zero;
    }

    public void Add(T value) {
        if (_count == _capacity) {
            _sum -= _buffer[_head];
        } else {
            _count++;
        }

        _buffer[_head] = value;
        _sum += value;
        _head = (_head + 1) % _capacity;
    }

    public T Average => _count == 0 ? T.Zero : _sum / T.CreateChecked(_count);

    /// <summary>
    /// Returns the minimum value in the buffer.
    /// </summary>
    public T GetMin() {
        if (_count == 0) return T.Zero;
        T min = _buffer[0];
        for (int i = 1; i < _count; i++) {
            if (_buffer[i] < min) min = _buffer[i];
        }
        return min;
    }

    /// <summary>
    /// Returns the maximum value in the buffer.
    /// </summary>
    public T GetMax() {
        if (_count == 0) return T.Zero;
        T max = _buffer[0];
        for (int i = 1; i < _count; i++) {
            if (_buffer[i] > max) max = _buffer[i];
        }
        return max;
    }

    /// <summary>
    /// Calculates the median using an optimized sort on a temporary copy.
    /// </summary>
    public T GetMedian() => GetPercentile(0.5f);

    /// <summary>
    /// Returns the value at the specified percentile (0.0 to 1.0).
    /// </summary>
    public T GetPercentile(float percentile) {
        if (_count == 0) return T.Zero;
        if (_count == 1) return _buffer[0];

        percentile = Math.Clamp(percentile, 0f, 1f);

        T[]? rented = null;
        Span<T> sortSpan;

        if (_count <= 512) {
            T* ptr = stackalloc T[_count];
            sortSpan = new Span<T>(ptr, _count);
        } else {
            rented = System.Buffers.ArrayPool<T>.Shared.Rent(_count);
            sortSpan = rented.AsSpan(0, _count);
        }

        try {
            new Span<T>(_buffer, _count).CopyTo(sortSpan);
            sortSpan.Sort();

            if (percentile <= 0) return sortSpan[0];
            if (percentile >= 1) return sortSpan[_count - 1];

            float index = percentile * (_count - 1);
            int i = (int)index;
            float fraction = index - i;

            if (i + 1 < _count) {
                // Linear interpolation for smooth percentile values
                double v0 = double.CreateChecked(sortSpan[i]);
                double v1 = double.CreateChecked(sortSpan[i + 1]);
                return T.CreateChecked(v0 * (1 - fraction) + v1 * fraction);
            } else {
                return sortSpan[i];
            }
        } finally {
            if (rented != null) {
                System.Buffers.ArrayPool<T>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Returns the standard deviation of the values in the buffer.
    /// High values relative to the average indicate micro-stutters.
    /// </summary>
    public double GetStandardDeviation() {
        if (_count <= 1) return 0;
        
        double avg = double.CreateChecked(Average);
        double sumSquares = 0;
        
        for (int i = 0; i < _count; i++) {
            double diff = double.CreateChecked(_buffer[i]) - avg;
            sumSquares += diff * diff;
        }
        
        return Math.Sqrt(sumSquares / _count);
    }

    /// <summary>
    /// Returns a Span view over the internal unmanaged data.
    /// Note: Data is in the order it appears in memory, not necessarily insertion order.
    /// </summary>
    public Span<T> AsSpan() => new Span<T>(_buffer, _count);

    public void Dispose() {
        if (_buffer != null) {
            NativeMemory.Free(_buffer);
            _buffer = null;
        }
        GC.SuppressFinalize(this);
    }

    ~RingBuffer() {
        Dispose();
    }
}
