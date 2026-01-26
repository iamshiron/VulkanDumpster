using System;
using System.Numerics;
using Silk.NET.Maths;

namespace Shiron.VulkanDumpster.Voxels;

public class Position : IFormattable, ISpanFormattable {
    public Vector3D<int> Block;
    public Vector3D<float> Offset;

    public float X => Block.X + Offset.X;
    public float Y => Block.Y + Offset.Y;
    public float Z => Block.Z + Offset.Z;

    public Position(Vector3D<int> block, Vector3D<float> offset) {
        Block = block;
        Offset = offset;
        Normalize();
    }

    public Position(float x, float y, float z) {
        Block = new Vector3D<int>((int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Floor(z));
        Offset = new Vector3D<float>(x - Block.X, y - Block.Y, z - Block.Z);
    }

    public void Normalize() {
        if (Offset.X >= 1.0f || Offset.X < 0.0f) {
            int floorX = (int)MathF.Floor(Offset.X);
            Block.X += floorX;
            Offset.X -= floorX;
        }
        if (Offset.Y >= 1.0f || Offset.Y < 0.0f) {
            int floorY = (int)MathF.Floor(Offset.Y);
            Block.Y += floorY;
            Offset.Y -= floorY;
        }
        if (Offset.Z >= 1.0f || Offset.Z < 0.0f) {
            int floorZ = (int)MathF.Floor(Offset.Z);
            Block.Z += floorZ;
            Offset.Z -= floorZ;
        }
    }

    public Vector3D<float> ToVector3() {
        return new Vector3D<float>(X, Y, Z);
    }

    public Vector2D<int> GetChunkPos() {
        return new Vector2D<int>(
            (int)MathF.Floor((float)Block.X / Chunk.Size),
            (int)MathF.Floor((float)Block.Z / Chunk.Size)
        );
    }

    public Vector2D<int> GetRegionPos() {
        var chunkPos = GetChunkPos();
        return new Vector2D<int>(
            (int)MathF.Floor((float)chunkPos.X / Region.SizeInChunks),
            (int)MathF.Floor((float)chunkPos.Y / Region.SizeInChunks)
        );
    }

    public static Position operator +(Position a, Vector3D<float> b) {
        return new Position(a.Block, a.Offset + b);
    }

    public static Position operator -(Position a, Vector3D<float> b) {
        return new Position(a.Block, a.Offset - b);
    }

    public static implicit operator Vector3D<float>(Position p) => p.ToVector3();
    public static explicit operator Vector3(Position p) => new Vector3(p.X, p.Y, p.Z);

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? formatProvider) {
        return string.Format(formatProvider, "Pos: {0}, {1}, {2} (B: {3}, {4}, {5}) (O: {6}, {7}, {8})",
            X.ToString(format, formatProvider), Y.ToString(format, formatProvider), Z.ToString(format, formatProvider),
            Block.X, Block.Y, Block.Z,
            Offset.X.ToString(format, formatProvider), Offset.Y.ToString(format, formatProvider), Offset.Z.ToString(format, formatProvider));
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) {
        return destination.TryWrite(provider, $"Pos: {X}, {Y}, {Z} (B: {Block.X}, {Block.Y}, {Block.Z}) (O: {Offset.X}, {Offset.Y}, {Offset.Z})", out charsWritten);
    }
}
