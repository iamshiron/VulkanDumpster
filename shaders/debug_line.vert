#version 450

layout(binding = 0) uniform GlobalUniforms {
    mat4 viewProj;
} global;

layout(push_constant) uniform PushConstants {
    mat4 model;
} pc;

layout (location = 0) in vec3 pos;
layout (location = 1) in vec3 color;

layout (location = 0) out vec3 outColor;

void main() {
    gl_Position = global.viewProj * pc.model * vec4(pos, 1.0);
    outColor = color;
}
