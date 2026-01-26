#version 450

layout(binding = 0) uniform sampler2D texSampler;

layout(location = 0) in vec2 fragTexCoord;
layout(location = 1) in vec4 fragColor;

layout(location = 0) out vec4 outColor;

void main() {
    // Sample texture. 
    // We assume the texture data is uploaded as RGBA where RGB=255 and A=coverage.
    vec4 sampled = texture(texSampler, fragTexCoord);
    outColor = fragColor * sampled;
}
