# Vulkan Dumpster - Learning Guide

This repository is a learning-focused Vulkan sample that renders a single
triangle using a clean, step-by-step boot sequence. The goal is clarity:
every stage of Vulkan setup is visible in code, and the code is organized
so you can rebuild it yourself and then grow it into a renderer.

## Prerequisites

- **.NET SDK** (to build the C# project)
- **Vulkan SDK** (for validation layers and shader tools)
  - Install the LunarG Vulkan SDK and ensure `VK_LAYER_KHRONOS_validation`
    is available on your system.
- **GPU driver with Vulkan support**

## Rebuild the Project From Scratch (Conceptual Steps)

Follow the same sequence used in `src/Program.cs`. Each step maps to a
Vulkan concept and a helper class in this repo.

1. **Create a window**
   Use a windowing library that can create a Vulkan surface (Silk.NET here).
   This is required because Vulkan needs a platform surface for presentation.

2. **Create a Vulkan instance** (`src/InstanceBuilder.cs`)
   - Provide app/engine names and Vulkan API version.
   - Enable required instance extensions from the window system.
   - **Enable validation layers** in debug builds to catch invalid usage.
   - **Why**: the instance is the entry point for all Vulkan objects, and
     validation is your safety net while learning.

3. **Create the window surface**
   The surface represents the OS window. It is required for swapchain
   creation and present support checks.

4. **Select a physical device (GPU)** (`src/PhysicalDeviceSelector.cs`)
   - Enumerate GPUs and score them.
   - Require graphics and present queues.
   - Require swapchain extension support.
   - **Why**: Vulkan is explicit; you must ensure the GPU supports the
     features and queues you need before creating a device.

5. **Create a logical device and queues** (`src/LogicalDeviceBuilder.cs`)
   - Request queue(s) from the selected queue families.
   - Enable required device extensions (e.g., `VK_KHR_swapchain`).
   - **Why**: the logical device is the handle you use for almost every
     Vulkan call; queues are where you submit work.

6. **Create the swapchain** (`src/SwapchainBuilder.cs`)
   - Choose surface format, present mode, image count, and extent.
   - Create image views for each swapchain image.
   - **Why**: the swapchain provides the images that will be shown on screen.

7. **Create per-frame command resources** (`InitCommands` in `Program.cs`)
   - Command pool per frame.
   - Primary command buffer per frame.
   - **Why**: command buffers record GPU work; per-frame resources keep
     CPU/GPU in flight without stalling every frame.

8. **Create per-frame synchronization** (`InitSyncStructures`)
   - Fence per frame (CPU waits for GPU).
   - Semaphores for image acquire and render complete.
   - **Why**: Vulkan does not implicitly synchronize. You must explicitly
     coordinate ownership of per-frame resources and swapchain images.

9. **Create the graphics pipeline** (`InitTrianglePipeline`)
   - Load SPIR-V shaders from `shaders/*.spv`.
   - Create pipeline layout.
   - Build pipeline with a `PipelineBuilder`.
   - **Why**: the pipeline bakes fixed-function state and shader stages.

10. **Render loop** (`Render` -> `Draw`)
    - Wait for the frame fence.
    - Acquire a swapchain image.
    - Record commands (clear + draw).
    - Submit the command buffer.
    - Present the image.
    - **Why**: this is the standard Vulkan frame flow.

11. **Cleanup**
    Destroy resources in **reverse order** of creation. Vulkan objects
    hold references to each other, so destruction must be ordered.

## How to Build and Run

1. Compile shaders:
   - Run `shaders/compile.bat` (requires `glslc` from the Vulkan SDK).
2. Build and run:
   - Use your IDE or `dotnet build` / `dotnet run`.

## Why These Vulkan Choices?

Below are the main Vulkan concepts used in this repo and **why** we use them.

### Validation Layers + Debug Utils

The code enables `VK_LAYER_KHRONOS_validation` and the
`VK_EXT_debug_utils` extension when building the instance. This provides
runtime checks that catch incorrect API usage, resource lifetime issues,
and synchronization mistakes. Vulkan does **not** validate anything by
default, so these layers are critical for learning and correctness.

### Queue Families and Present Support

Vulkan queues are grouped into **queue families**, each supporting a set
of operations (graphics, compute, transfer, present). We select a GPU that
has both **graphics** and **present** support because the sample draws and
then presents to the screen.

### Swapchain Image Sharing Mode

If graphics and present queues are **the same family**, the swapchain uses
**exclusive** sharing mode for best performance. If they are **different**,
the swapchain uses **concurrent** sharing mode to avoid explicit ownership
transfer barriers. This keeps the learning code simpler, at the cost of a
small performance hit on some drivers.

### Synchronization2 and `vkQueueSubmit2`

The render loop uses synchronization2 (`vkQueueSubmit2` and
`VkSemaphoreSubmitInfo`). This modern API makes sync scopes and stage
masks explicit and is the recommended path in Vulkan 1.3. It also aligns
better with timeline semaphores if you expand later.

### Dynamic Rendering (No Render Pass)

Pipelines are created with `VkPipelineRenderingCreateInfo` so we can use
`vkCmdBeginRendering` without building a `VkRenderPass`/`VkFramebuffer`.
Dynamic rendering reduces boilerplate and is great for learning because
you can focus on core pipeline state first.

### Per-Frame Resources

The sample uses multiple frames in flight (triple buffering). Each frame
has its own command pool, command buffer, fence, and semaphores. This
pattern keeps the GPU busy while the CPU prepares the next frame.

## Suggested Next Steps

After you understand this flow, try extending it:

- Add a depth buffer and enable depth testing.
- Create a vertex buffer instead of hardcoded triangle vertices.
- Introduce descriptor sets and uniform buffers.
- Move swapchain recreation into a resize handler.
- Add a second pipeline (e.g., wireframe) to compare states.

## File Map

- `src/Program.cs` - App entry point and render loop.
- `src/InstanceBuilder.cs` - Vulkan instance + debug messenger.
- `src/PhysicalDeviceSelector.cs` - GPU selection and queue families.
- `src/LogicalDeviceBuilder.cs` - Logical device creation and queues.
- `src/SwapchainBuilder.cs` - Swapchain creation and image views.
- `src/PipelineBuilder.cs` - Graphics pipeline setup (dynamic rendering).
- `src/ShaderUtils.cs` - SPIR-V shader module loading.

