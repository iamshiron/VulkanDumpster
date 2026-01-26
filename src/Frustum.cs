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

    public void Update(Matrix4X4<float> m) {
        // Left
        _planes[0] = new Plane(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41);
        // Right
        _planes[1] = new Plane(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41);
        // Bottom
        _planes[2] = new Plane(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42);
        // Top
        _planes[3] = new Plane(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42);
        // Near (Vulkan: 0 <= z <= w)
        _planes[4] = new Plane(m.M13, m.M23, m.M33, m.M43);
        // Far
        _planes[5] = new Plane(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43);
    }

    public bool IsBoxVisible(Vector3D<float> min, Vector3D<float> max) {
        for (int i = 0; i < 6; i++) {
            var plane = _planes[i];
            
            // For each plane, find the vertex of the box that is "most inside" 
            // the plane. If even this vertex is outside, the box is completely outside.
            // Using the P-vertex logic for frustum culling.
            
            float px = plane.Normal.X >= 0 ? max.X : min.X;
            float py = plane.Normal.Y >= 0 ? max.Y : min.Y;
            float pz = plane.Normal.Z >= 0 ? max.Z : min.Z;

            if (plane.GetSignedDistance(new Vector3D<float>(px, py, pz)) < 0) {
                return false;
            }
        }
        return true;
    }
}