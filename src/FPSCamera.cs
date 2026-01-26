using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
namespace Shiron.VulkanDumpster;

public class FPSCamera : ICamera {
    public Vector3D<float> Position { get; set; } = new(0, 0, 3);
    public Vector3D<float> Front { get; private set; } = new(0, 0, -1);
    public Vector3D<float> Up { get; private set; } = Vector3D<float>.UnitY;
    public Vector3D<float> Right { get; private set; } = Vector3D<float>.UnitX;
    public Vector3D<float> WorldUp { get; } = Vector3D<float>.UnitY;
    public float Yaw { get; set; } = -90f;
    public float Pitch { get; set; } = 0f;
    public float MoveSpeed { get; set; } = 100f;
    public float MouseSensitivity { get; set; } = 0.1f;
    public float Zoom { get; set; } = 45f;
    public FPSCamera(Vector3D<float> position) {
        Position = position;
        UpdateCameraVectors();
    }
    public Matrix4X4<float> GetViewMatrix() {
        return Matrix4X4.CreateLookAt(Position, Position + Front, Up);
    }
    public Matrix4X4<float> GetProjectionMatrix(float aspectRatio) {
        return Matrix4X4.CreatePerspectiveFieldOfView(Scalar.DegreesToRadians(Zoom), aspectRatio, 0.1f, 1000.0f);
    }
    public void ProcessKeyboard(Key key, double deltaTime) {
        float velocity = MoveSpeed * (float) deltaTime;
        if (key == Key.W) Position += Front * velocity;
        if (key == Key.S) Position -= Front * velocity;
        if (key == Key.A) Position -= Right * velocity;
        if (key == Key.D) Position += Right * velocity;
        if (key == Key.Space) Position += WorldUp * velocity;
        if (key == Key.ControlLeft) Position -= WorldUp * velocity;
    }
    public void ProcessMouseMovement(float xOffset, float yOffset, bool constrainPitch = true) {
        xOffset *= MouseSensitivity;
        yOffset *= MouseSensitivity;
        Yaw += xOffset;
        Pitch -= yOffset;
        if (constrainPitch) {
            Pitch = Math.Clamp(Pitch, -89f, 89f);
        }
        UpdateCameraVectors();
    }
    public void Update(double deltaTime) {
        // High-level update logic if needed
    }
    private void UpdateCameraVectors() {
        Vector3D<float> front;
        front.X = MathF.Cos(Scalar.DegreesToRadians(Yaw)) * MathF.Cos(Scalar.DegreesToRadians(Pitch));
        front.Y = MathF.Sin(Scalar.DegreesToRadians(Pitch));
        front.Z = MathF.Sin(Scalar.DegreesToRadians(Yaw)) * MathF.Cos(Scalar.DegreesToRadians(Pitch));
        Front = Vector3D.Normalize(front);
        Right = Vector3D.Normalize(Vector3D.Cross(Front, WorldUp));
        Up = Vector3D.Normalize(Vector3D.Cross(Right, Front));
    }
}
