using System.Drawing;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

public class Program {
    private static IWindow _window = null!;
    private static GL _gl = null!;

    public static void Main(string[] args) {
        var options = WindowOptions.Default;
        options.Title = "Vulkan Dumpster Project";
        options.Size = new Vector2D<int>(1920, 1080);

        _window = Window.Create(options);

        _window.Load += () => {
            Console.WriteLine("Window loaded");
            var inputContext = _window.CreateInput();

            foreach (var kb in inputContext.Keyboards) {
                kb.KeyDown += KeyDown;
            }

            _gl = _window.CreateOpenGL();
            _gl.ClearColor(Color.MediumPurple);
        };
        _window.Render += Render;

        _window.Run();
    }

    private static void KeyDown(IKeyboard keyboard, Key key, int arg3) {
        if (key == Key.Escape) {
            _window.Close();
        }
    }

    private static void Render(double delta) {
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }
}
