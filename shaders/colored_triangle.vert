#version 450

layout(binding = 0) uniform GlobalUniforms {
    mat4 viewProj;
} global;

layout(push_constant) uniform PushConstants {
    mat4 model;
} pc;

layout (location = 0) in vec3 pos;
layout (location = 1) in vec2 texCoord;
layout (location = 2) in float texIndex;

layout (location = 0) out vec3 outTexCoord;

void main() {
    vec4 worldPos = pc.model * vec4(pos, 1.0);
    gl_Position = global.viewProj * worldPos;
    
    // Pass UV and Layer Index as a single vec3 to the fragment shader
    outTexCoord = vec3(texCoord, texIndex);
}