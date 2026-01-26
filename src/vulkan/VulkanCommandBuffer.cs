using Silk.NET.Vulkan;
namespace Shiron.VulkanDumpster.Vulkan;
/// <summary>
/// Wrapper for VkCommandBuffer to avoid raw API calls in the main application.
/// </summary>
public unsafe struct VulkanCommandBuffer {
    private readonly Vk _vk;
    public CommandBuffer Handle { get; }
    private string _currentPipelineName;

    public VulkanCommandBuffer(Vk vk, CommandBuffer handle) {
        _vk = vk;
        Handle = handle;
        _currentPipelineName = "None";
    }

    public void BindPipeline(VulkanPipeline pipeline, PipelineBindPoint bindPoint = PipelineBindPoint.Graphics) {
        _vk.CmdBindPipeline(Handle, bindPoint, pipeline.Handle);
        _currentPipelineName = pipeline.Name;
        VulkanCommandProfiler.IncrementPipelineBinds();
    }

    public void BindDescriptorSets(VulkanPipeline pipeline, DescriptorSet[] sets, PipelineBindPoint bindPoint = PipelineBindPoint.Graphics) {
        fixed (DescriptorSet* pSets = sets) {
            _vk.CmdBindDescriptorSets(Handle, bindPoint, pipeline.Layout, 0, (uint) sets.Length, pSets, 0, null);
        }
        VulkanCommandProfiler.IncrementDescriptorSetBinds();
    }

    public void BindDescriptorSets(VulkanPipeline pipeline, DescriptorSet set, PipelineBindPoint bindPoint = PipelineBindPoint.Graphics) {
        _vk.CmdBindDescriptorSets(Handle, bindPoint, pipeline.Layout, 0, 1, &set, 0, null);
        VulkanCommandProfiler.IncrementDescriptorSetBinds();
    }

    public void BindVertexBuffer(VulkanBuffer buffer, uint binding = 0) {
        var hBuffer = buffer.Handle;
        ulong offset = 0;
        _vk.CmdBindVertexBuffers(Handle, binding, 1, &hBuffer, &offset);
        VulkanCommandProfiler.IncrementVertexBufferBinds();
    }

    public void BindIndexBuffer(VulkanBuffer buffer, IndexType indexType = IndexType.Uint16) {
        _vk.CmdBindIndexBuffer(Handle, buffer.Handle, 0, indexType);
        VulkanCommandProfiler.IncrementIndexBufferBinds();
    }

    public void PushConstants<T>(VulkanPipeline pipeline, ShaderStageFlags stages, T data) where T : unmanaged {
        _vk.CmdPushConstants(Handle, pipeline.Layout, stages, 0, (uint) sizeof(T), &data);
        VulkanCommandProfiler.IncrementPushConstants();
    }

    public void WriteTimestamp(PipelineStageFlags stage, QueryPool queryPool, uint query) {
        _vk.CmdWriteTimestamp(Handle, stage, queryPool, query);
    }

    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) {
        _vk.CmdDraw(Handle, vertexCount, instanceCount, firstVertex, firstInstance);
        VulkanCommandProfiler.IncrementDrawCalls();
        for (uint i = 0; i < instanceCount; i++) {
            VulkanCommandProfiler.IncrementShaderExecution(_currentPipelineName);
        }
    }

    public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0) {
        _vk.CmdDrawIndexed(Handle, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        VulkanCommandProfiler.IncrementDrawCalls();
        VulkanCommandProfiler.IncrementShaderExecution(_currentPipelineName);
    }

    public void DrawIndexedIndirect(VulkanBuffer buffer, ulong offset, uint drawCount, uint stride) {
        _vk.CmdDrawIndexedIndirect(Handle, buffer.Handle, offset, drawCount, stride);
        VulkanCommandProfiler.IncrementDrawCalls();
        for (uint i = 0; i < drawCount; i++) {
            VulkanCommandProfiler.IncrementShaderExecution(_currentPipelineName);
        }
    }
}