using System.Drawing;
using Silk.NET.Core;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Windowing;

namespace Shiron.VulkanDumpster;

public class Program {
    private static IWindow _window = null!;

    private static Vk _vk;
    private static Instance _instance;

    public static unsafe void Main(string[] args) {
        var options = WindowOptions.Default;
        options.Title = "Vulkan Dumpster Project";
        options.Size = new Vector2D<int>(1920, 1080);

        _window = Window.Create(options);
        _vk = Vk.GetApi();
        var instanceBuilder = new InstanceBuilder(_vk)
            .WithApp("VulkanDumpster", new Version32(1, 0, 0))
            .WithEngine("NoEngine", new Version32(1, 0, 0))
            .WithApiVersion(Vk.Version13)
            .AddExtensions([
                "VK_KHR_surface",
                "VK_KHR_win32_surface"
            ])
            .EnableValidationLayers(enable: true);
        _instance = instanceBuilder.Build();

        _window.Load += () => {
            Console.WriteLine("Window loaded");
            var inputContext = _window.CreateInput();

            foreach (var kb in inputContext.Keyboards) {
                kb.KeyDown += KeyDown;
            }
        };
        _window.Render += Render;

        _window.Run();

        _window.Dispose();
        instanceBuilder.Dispose();
    }

    private static void KeyDown(IKeyboard keyboard, Key key, int arg3) {
        if (key == Key.Escape) {
            _window.Close();
        }
    }

    private static void Render(double delta) {
    }
}
