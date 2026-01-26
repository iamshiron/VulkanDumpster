using Silk.NET.Vulkan;
namespace Shiron.VulkanDumpster.Vulkan;
/// <summary>
/// Builder class to simplify the creation of Vulkan graphics pipelines.
/// Uses dynamic rendering (no render pass) to reduce boilerplate for learning.
/// </summary>
public class PipelineBuilder {
    private readonly Vk _vk;
    private readonly List<PipelineShaderStageCreateInfo> _shaderStages = new();
    private PipelineInputAssemblyStateCreateInfo _inputAssembly;
    private PipelineRasterizationStateCreateInfo _rasterizer;
    private PipelineColorBlendAttachmentState _colorBlendAttachment;
    private PipelineMultisampleStateCreateInfo _multisampling;
    private PipelineDepthStencilStateCreateInfo _depthStencil;
    private PipelineRenderingCreateInfo _renderInfo;
    private Format _colorAttachmentFormat;
    private VertexInputBindingDescription _bindingDescription;
    private VertexInputAttributeDescription[] _attributeDescriptions;
    /// <summary>
    /// The pipeline layout that controls inputs/outputs of the shader.
    /// Must be set before building the pipeline.
    /// </summary>
    public PipelineLayout PipelineLayout { get; set; }
    public PipelineBuilder(Vk vk) {
        _vk = vk;
        Clear();
    }
    /// <summary>
    /// Reset all state to defaults.
    /// </summary>
    public void Clear() {
        _shaderStages.Clear();
        _inputAssembly = new PipelineInputAssemblyStateCreateInfo {
            SType = StructureType.PipelineInputAssemblyStateCreateInfo
        };
        _rasterizer = new PipelineRasterizationStateCreateInfo {
            SType = StructureType.PipelineRasterizationStateCreateInfo
        };
        _colorBlendAttachment = new PipelineColorBlendAttachmentState();
        _multisampling = new PipelineMultisampleStateCreateInfo {
            SType = StructureType.PipelineMultisampleStateCreateInfo
        };
        _depthStencil = new PipelineDepthStencilStateCreateInfo {
            SType = StructureType.PipelineDepthStencilStateCreateInfo
        };
        _renderInfo = new PipelineRenderingCreateInfo {
            SType = StructureType.PipelineRenderingCreateInfo
        };
        _colorAttachmentFormat = Format.Undefined;
        PipelineLayout = default;
        _bindingDescription = Vertex.GetBindingDescription();
        _attributeDescriptions = Vertex.GetAttributeDescriptions();
    }
    /// <summary>
    /// Set the vertex and fragment shaders for the pipeline.
    /// </summary>
    public unsafe PipelineBuilder SetShaders(ShaderModule vertexShader, ShaderModule fragmentShader) {
        _shaderStages.Clear();
        _shaderStages.Add(new PipelineShaderStageCreateInfo {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.VertexBit,
            Module = vertexShader,
            PName = (byte*) System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("main")
        });
        _shaderStages.Add(new PipelineShaderStageCreateInfo {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = ShaderStageFlags.FragmentBit,
            Module = fragmentShader,
            PName = (byte*) System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("main")
        });
        return this;
    }
    /// <summary>
    /// Set the input topology (triangle list, point list, line list, etc.).
    /// </summary>
    public PipelineBuilder SetInputTopology(PrimitiveTopology topology) {
        _inputAssembly.Topology = topology;
        _inputAssembly.PrimitiveRestartEnable = false;
        return this;
    }
    /// <summary>
    /// Set the polygon mode (fill, line/wireframe, point).
    /// </summary>
    public PipelineBuilder SetPolygonMode(PolygonMode mode) {
        _rasterizer.PolygonMode = mode;
        _rasterizer.LineWidth = 1.0f;
        return this;
    }
    /// <summary>
    /// Set the culling mode and front face winding order.
    /// </summary>
    public PipelineBuilder SetCullMode(CullModeFlags cullMode, FrontFace frontFace) {
        _rasterizer.CullMode = cullMode;
        _rasterizer.FrontFace = frontFace;
        return this;
    }
    /// <summary>
    /// Disable multisampling (1 sample per pixel).
    /// </summary>
    public unsafe PipelineBuilder SetMultisamplingNone() {
        _multisampling.SampleShadingEnable = false;
        _multisampling.RasterizationSamples = SampleCountFlags.Count1Bit;
        _multisampling.MinSampleShading = 1.0f;
        _multisampling.PSampleMask = null;
        _multisampling.AlphaToCoverageEnable = false;
        _multisampling.AlphaToOneEnable = false;
        return this;
    }
    /// <summary>
    /// Disable blending - writes color directly without any blending operations.
    /// </summary>
    public PipelineBuilder DisableBlending() {
        _colorBlendAttachment.ColorWriteMask =
            ColorComponentFlags.RBit |
            ColorComponentFlags.GBit |
            ColorComponentFlags.BBit |
            ColorComponentFlags.ABit;
        _colorBlendAttachment.BlendEnable = false;
        return this;
    }
    /// <summary>
    /// Enable additive blending.
    /// </summary>
    public PipelineBuilder EnableBlendingAdditive() {
        _colorBlendAttachment.ColorWriteMask =
            ColorComponentFlags.RBit |
            ColorComponentFlags.GBit |
            ColorComponentFlags.BBit |
            ColorComponentFlags.ABit;
        _colorBlendAttachment.BlendEnable = true;
        _colorBlendAttachment.SrcColorBlendFactor = BlendFactor.One;
        _colorBlendAttachment.DstColorBlendFactor = BlendFactor.DstAlpha;
        _colorBlendAttachment.ColorBlendOp = BlendOp.Add;
        _colorBlendAttachment.SrcAlphaBlendFactor = BlendFactor.One;
        _colorBlendAttachment.DstAlphaBlendFactor = BlendFactor.Zero;
        _colorBlendAttachment.AlphaBlendOp = BlendOp.Add;
        return this;
    }
    /// <summary>
    /// Enable alpha blending (standard transparency).
    /// </summary>
    public PipelineBuilder EnableBlendingAlphaBlend() {
        _colorBlendAttachment.ColorWriteMask =
            ColorComponentFlags.RBit |
            ColorComponentFlags.GBit |
            ColorComponentFlags.BBit |
            ColorComponentFlags.ABit;
        _colorBlendAttachment.BlendEnable = true;
        _colorBlendAttachment.SrcColorBlendFactor = BlendFactor.SrcAlpha;
        _colorBlendAttachment.DstColorBlendFactor = BlendFactor.OneMinusSrcAlpha;
        _colorBlendAttachment.ColorBlendOp = BlendOp.Add;
        _colorBlendAttachment.SrcAlphaBlendFactor = BlendFactor.One;
        _colorBlendAttachment.DstAlphaBlendFactor = BlendFactor.Zero;
        _colorBlendAttachment.AlphaBlendOp = BlendOp.Add;
        return this;
    }
    /// <summary>
    /// Set the color attachment format for dynamic rendering.
    /// </summary>
    public PipelineBuilder SetColorAttachmentFormat(Format format) {
        _colorAttachmentFormat = format;
        _renderInfo.ColorAttachmentCount = 1;
        return this;
    }
    /// <summary>
    /// Set the depth attachment format for dynamic rendering.
    /// </summary>
    public PipelineBuilder SetDepthFormat(Format format) {
        _renderInfo.DepthAttachmentFormat = format;
        return this;
    }
    /// <summary>
    /// Disable depth testing entirely.
    /// </summary>
    public PipelineBuilder DisableDepthTest() {
        _depthStencil.DepthTestEnable = false;
        _depthStencil.DepthWriteEnable = false;
        _depthStencil.DepthCompareOp = CompareOp.Never;
        _depthStencil.DepthBoundsTestEnable = false;
        _depthStencil.StencilTestEnable = false;
        _depthStencil.Front = new StencilOpState();
        _depthStencil.Back = new StencilOpState();
        _depthStencil.MinDepthBounds = 0.0f;
        _depthStencil.MaxDepthBounds = 1.0f;
        return this;
    }
    /// <summary>
    /// Enable depth testing with specified compare operation.
    /// </summary>
    public PipelineBuilder EnableDepthTest(bool depthWriteEnable, CompareOp compareOp) {
        _depthStencil.DepthTestEnable = true;
        _depthStencil.DepthWriteEnable = depthWriteEnable;
        _depthStencil.DepthCompareOp = compareOp;
        _depthStencil.DepthBoundsTestEnable = false;
        _depthStencil.StencilTestEnable = false;
        _depthStencil.Front = new StencilOpState();
        _depthStencil.Back = new StencilOpState();
        _depthStencil.MinDepthBounds = 0.0f;
        _depthStencil.MaxDepthBounds = 1.0f;
        return this;
    }
    /// <summary>
    /// Set custom vertex input descriptions.
    /// </summary>
    public PipelineBuilder SetVertexInput(VertexInputBindingDescription binding, VertexInputAttributeDescription[] attributes) {
        _bindingDescription = binding;
        _attributeDescriptions = attributes;
        return this;
    }
    /// <summary>
    /// Build the graphics pipeline using the current builder state.
    /// </summary>
    /// <param name="device">The Vulkan device to create the pipeline on.</param>
    /// <returns>The created pipeline, or null handle if creation failed.</returns>
    public unsafe Pipeline Build(Device device) {
        // Viewport state - we use dynamic state so just set counts
        var viewportState = new PipelineViewportStateCreateInfo {
            SType = StructureType.PipelineViewportStateCreateInfo,
            ViewportCount = 1,
            ScissorCount = 1
        };
        // Color blending state
        fixed (PipelineColorBlendAttachmentState* pColorBlendAttachment = &_colorBlendAttachment) {
            var colorBlending = new PipelineColorBlendStateCreateInfo {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = false,
                LogicOp = LogicOp.Copy,
                AttachmentCount = 1,
                PAttachments = pColorBlendAttachment
            };
            fixed (VertexInputBindingDescription* pBinding = &_bindingDescription)
            fixed (VertexInputAttributeDescription* pAttributes = _attributeDescriptions) {
                // Empty vertex input state (we use vertex pulling)
                var vertexInputInfo = new PipelineVertexInputStateCreateInfo {
                    SType = StructureType.PipelineVertexInputStateCreateInfo,
                    VertexBindingDescriptionCount = 1,
                    PVertexBindingDescriptions = pBinding,
                    VertexAttributeDescriptionCount = (uint) _attributeDescriptions.Length,
                    PVertexAttributeDescriptions = pAttributes
                };
                // Dynamic state - viewport and scissor
                var dynamicStates = stackalloc DynamicState[] {
                DynamicState.Viewport,
                DynamicState.Scissor
            };
                var dynamicInfo = new PipelineDynamicStateCreateInfo {
                    SType = StructureType.PipelineDynamicStateCreateInfo,
                    PDynamicStates = dynamicStates,
                    DynamicStateCount = 2
                };
                // Set color attachment format pointer for render info
                fixed (Format* pColorFormat = &_colorAttachmentFormat)
                fixed (PipelineInputAssemblyStateCreateInfo* pInputAssembly = &_inputAssembly)
                fixed (PipelineRasterizationStateCreateInfo* pRasterizer = &_rasterizer)
                fixed (PipelineMultisampleStateCreateInfo* pMultisampling = &_multisampling)
                fixed (PipelineDepthStencilStateCreateInfo* pDepthStencil = &_depthStencil) {
                    _renderInfo.PColorAttachmentFormats = pColorFormat;
                    var stages = _shaderStages.ToArray();
                    fixed (PipelineShaderStageCreateInfo* pStages = stages)
                    fixed (PipelineRenderingCreateInfo* pRenderInfo = &_renderInfo) {
                        var pipelineInfo = new GraphicsPipelineCreateInfo {
                            SType = StructureType.GraphicsPipelineCreateInfo,
                            PNext = pRenderInfo,
                            StageCount = (uint) _shaderStages.Count,
                            PStages = pStages,
                            PVertexInputState = &vertexInputInfo,
                            PInputAssemblyState = pInputAssembly,
                            PViewportState = &viewportState,
                            PRasterizationState = pRasterizer,
                            PMultisampleState = pMultisampling,
                            PColorBlendState = &colorBlending,
                            PDepthStencilState = pDepthStencil,
                            Layout = PipelineLayout,
                            PDynamicState = &dynamicInfo
                        };
                        Pipeline newPipeline;
                        var result = _vk.CreateGraphicsPipelines(
                            device, default, 1, &pipelineInfo, null, &newPipeline);
                        if (result != Result.Success) {
                            Console.WriteLine($"Failed to create graphics pipeline: {result}");
                            return default;
                        }
                        return newPipeline;
                    }
                }
            }
        }
    }
}
