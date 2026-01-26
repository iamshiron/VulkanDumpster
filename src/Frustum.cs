using System.Numerics;
using Silk.NET.Maths;

namespace Shiron.VulkanDumpster;

public struct Plane {
    public Vector3D<float> Normal;
    public float Distance;

    public Plane(float x, float y, float z, float d) {
        float length = MathF.Sqrt(x * x + y * y + z * z);
        Normal = new Vector3D<float>(x / length, y / length, z / length);
        Distance = d / length;
    }

    public float GetSignedDistance(Vector3D<float> point) {
        return Vector3D.Dot(Normal, point) + Distance;
    }
}

public class Frustum {
    private readonly Plane[] _planes = new Plane[6];

    public void Update(Matrix4X4<float> viewProj) {
        // Left
        _planes[0] = new Plane(
            viewProj.M14 + viewProj.M11,
            viewProj.M24 + viewProj.M21,
            viewProj.M34 + viewProj.M31,
            viewProj.M44 + viewProj.M41
        );
        // Right
        _planes[1] = new Plane(
            viewProj.M14 - viewProj.M11,
            viewProj.M24 - viewProj.M21,
            viewProj.M34 - viewProj.M31,
            viewProj.M44 - viewProj.M41
        );
        // Bottom
        _planes[2] = new Plane(
            viewProj.M14 + viewProj.M12,
            viewProj.M24 + viewProj.M22,
            viewProj.M34 + viewProj.M32,
            viewProj.M44 + viewProj.M42
        );
        // Top
        _planes[3] = new Plane(
            viewProj.M14 - viewProj.M12,
            viewProj.M24 - viewProj.M22,
            viewProj.M34 - viewProj.M32,
            viewProj.M44 - viewProj.M42
        );
        // Near
        _planes[4] = new Plane(
            viewProj.M13,
            viewProj.M23,
            viewProj.M33,
            viewProj.M43
        );
        // Far
        _planes[5] = new Plane(
            viewProj.M14 - viewProj.M13,
            viewProj.M24 - viewProj.M23,
            viewProj.M34 - viewProj.M33,
            viewProj.M44 - viewProj.M43
        );
    }

    public bool IsBoxVisible(Vector3D<float> min, Vector3D<float> max) {
        foreach (var plane in _planes) {
            // Check if the positive vertex (in direction of normal) is outside (behind) the plane
            var p = new Vector3D<float>(
                plane.Normal.X > 0 ? max.X : min.X,
                plane.Normal.Y > 0 ? max.Y : min.Y,
                plane.Normal.Z > 0 ? max.Z : min.Z
            );

            if (plane.GetSignedDistance(p) < 0) {
                return false;
            }
        }
        return true;
    }
}
