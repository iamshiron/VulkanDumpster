using System.Drawing;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

public class Program {
    private static IWindow _window = null!;
    private static GL _gl = null!;

    private static uint _vao;
    private static uint _vbo;
    private static uint _shaderProgram;
    private static uint _vertexShader;
    private static uint _fragmentShader;

    public static unsafe void Main(string[] args) {
        var options = WindowOptions.Default;
        options.Title = "Vulkan Dumpster Project";
        options.Size = new Vector2D<int>(1920, 1080);

        _window = Window.Create(options);

        _window.Load += () => {
            string vertexCode = @"
            #version 330 core

            layout (location = 0) in vec3 aPosition;

            void main() {
                gl_Position = vec4(aPosition, 1.0);
            }
            ";

            string fragmentCode = @"
            #version 330 core

            out vec4 out_color;

            void main() {
                out_color = vec4(1.0, 0.5, 0.2, 1.0);
            }
            ";

            Console.WriteLine("Window loaded");
            var inputContext = _window.CreateInput();

            foreach (var kb in inputContext.Keyboards) {
                kb.KeyDown += KeyDown;
            }

            _gl = _window.CreateOpenGL();
            _gl.ClearColor(Color.MediumPurple);

            _vao = _gl.GenVertexArray();
            _gl.BindVertexArray(_vao);

            _vbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.EnableVertexAttribArray(0);
            _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*) 0);

            float[] vertices = [
                 0.0f,  0.5f, 0.0f,
                -0.5f, -0.5f, 0.0f,
                 0.5f, -0.5f, 0.0f
            ];

            fixed (float* buf = vertices)
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint) (vertices.Length * sizeof(float)), buf, BufferUsageARB.StaticDraw);

            {
                _fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
                _gl.ShaderSource(_fragmentShader, fragmentCode);
                _gl.CompileShader(_fragmentShader);

                _gl.GetShader(_fragmentShader, ShaderParameterName.CompileStatus, out int success);
                if (success != (int) GLEnum.True) {
                    string infoLog = _gl.GetShaderInfoLog(_fragmentShader);
                    Console.WriteLine($"Fragment shader failed to compile:\n{infoLog}");
                }
            }
            System.Console.WriteLine($"Created fragment shader with ID {_fragmentShader}");

            {
                _vertexShader = _gl.CreateShader(ShaderType.VertexShader);
                _gl.ShaderSource(_vertexShader, vertexCode);
                _gl.CompileShader(_vertexShader);

                _gl.GetShader(_vertexShader, ShaderParameterName.CompileStatus, out int success);
                if (success != (int) GLEnum.True) {
                    string infoLog = _gl.GetShaderInfoLog(_vertexShader);
                    Console.WriteLine($"Vertex shader failed to compile:\n{infoLog}");
                }
            }
            System.Console.WriteLine($"Created vertex shader with ID {_vertexShader}");

            _shaderProgram = _gl.CreateProgram();

            _gl.AttachShader(_shaderProgram, _vertexShader);
            _gl.AttachShader(_shaderProgram, _fragmentShader);
            _gl.LinkProgram(_shaderProgram);

            _gl.GetProgram(_shaderProgram, ProgramPropertyARB.LinkStatus, out int lStatus);
            if (lStatus != (int) GLEnum.True)
                throw new Exception($"Program failed to link:\n{_gl.GetProgramInfoLog(_shaderProgram)}");

            _gl.DetachShader(_shaderProgram, _vertexShader);
            _gl.DetachShader(_shaderProgram, _fragmentShader);
            _gl.DeleteShader(_vertexShader);
            _gl.DeleteShader(_fragmentShader);

            _gl.BindVertexArray(0);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);
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

        _gl.BindVertexArray(_vao);
        _gl.UseProgram(_shaderProgram);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }
}
