using System;

namespace Shiron.VulkanDumpster;

public class AppSettings {
    public bool VSync { get; set; } = false;
    public ushort MaxFPS { get; set; } = 0; // 0 for unlimited
    public ushort RenderDistance { get; set; } = 16;
}
