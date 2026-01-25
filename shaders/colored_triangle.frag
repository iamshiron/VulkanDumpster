#version 450

layout (binding = 1) uniform sampler2DArray texSampler;

layout (location = 0) in vec3 inTexCoord;

layout (location = 0) out vec4 outColor;

void main() {
    outColor = texture(texSampler, inTexCoord);
}