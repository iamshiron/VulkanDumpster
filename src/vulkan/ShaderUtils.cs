using Silk.NET.Vulkan;
using System;
using System.IO;

namespace Shiron.VulkanDumpster.Vulkan;

/// <summary>
/// Utility functions for working with SPIR-V shader modules.
/// </summary>
public static class ShaderUtils {
    /// <summary>
    /// Load a shader module from a compiled SPIR-V file.
    /// </summary>
    /// <param name="vk">The Vulkan API instance.</param>
    /// <param name="device">The Vulkan device to create the shader module on.</param>
    /// <param name="filePath">Path to the compiled .spv shader file.</param>
    /// <param name="shaderModule">The created shader module if successful.</param>
    /// <returns>True if the shader was loaded successfully, false otherwise.</returns>
    public static unsafe bool LoadShaderModule(Vk vk, Device device, string filePath, out ShaderModule shaderModule) {
        shaderModule = default;

        // Check if file exists
        if (!File.Exists(filePath)) {
            Console.WriteLine($"Shader file not found: {filePath}");
            return false;
        }

        try {
            // Read the compiled SPIR-V bytecode
            var bytes = File.ReadAllBytes(filePath);

            // SPIR-V code must be aligned to 4 bytes
            if (bytes.Length % 4 != 0) {
                Console.WriteLine($"Invalid SPIR-V file: {filePath} (size not aligned to 4 bytes)");
                return false;
            }

            fixed (byte* pCode = bytes) {
                var createInfo = new ShaderModuleCreateInfo {
                    SType = StructureType.ShaderModuleCreateInfo,
                    CodeSize = (nuint) bytes.Length,
                    PCode = (uint*) pCode
                };

                ShaderModule module;
                var result = vk.CreateShaderModule(device, &createInfo, null, &module);

                if (result != Result.Success) {
                    Console.WriteLine($"Failed to create shader module from {filePath}: {result}");
                    return false;
                }

                shaderModule = module;
                return true;
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error loading shader {filePath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Create a pipeline shader stage for the "main" entry point.
    /// </summary>
    /// <param name="stage">The shader stage (vertex, fragment, etc.).</param>
    /// <param name="shaderModule">The shader module to use.</param>
    /// <returns>The configured shader stage create info.</returns>
    public static unsafe PipelineShaderStageCreateInfo PipelineShaderStageCreateInfo(
        ShaderStageFlags stage, ShaderModule shaderModule) {
        return new PipelineShaderStageCreateInfo {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = stage,
            Module = shaderModule,
            PName = (byte*) System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("main")
        };
    }
}
