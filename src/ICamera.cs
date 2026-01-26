using Silk.NET.Maths;
namespace Shiron.VulkanDumpster;
/// <summary>
/// Interface for any camera system.
/// </summary>
public interface ICamera {
    Matrix4X4<float> GetViewMatrix();
    Matrix4X4<float> GetProjectionMatrix(float aspectRatio);
    void Update(double deltaTime);
}
