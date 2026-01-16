using Silk.NET.Windowing;

var options = WindowOptions.Default;
options.Title = "Vulkan Dumpster Project";
options.Size = new Silk.NET.Maths.Vector2D<int>(1920, 1080);

var window = Window.Create(options);
window.Run();
