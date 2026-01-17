#version 450

// Shader input (interpolated from vertex shader)
layout (location = 0) in vec3 inColor;

// Output write to color attachment
layout (location = 0) out vec4 outFragColor;

void main() {
    outFragColor = vec4(inColor, 1.0);
}
