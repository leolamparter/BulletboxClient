#version 330

in vec2 fragTexCoord;
in vec4 fragColor;

out vec4 finalColor;

uniform sampler2D texture0;
uniform vec4 skyTint;
uniform float exposure;
uniform vec2 sunDirection;

struct Light {
    vec2 position;
    vec4 color;
    float radius;
    float intensity;
};

uniform Light lights[32];
uniform int lightCount;
uniform vec2 screenResolution;

void main()
{
    vec4 texelColor = texture(texture0, fragTexCoord);
    vec3 ambient = skyTint.rgb * exposure;
    vec3 totalLight = ambient;
    
    // Calculate screen-space position for point lights
    vec2 fragPos = fragTexCoord * screenResolution;

    for (int i = 0; i < lightCount; i++)
    {
        float dist = distance(fragPos, lights[i].position);
        if (dist < lights[i].radius)
        {
            float falloff = 1.0 - (dist / lights[i].radius);
            // Quadratic falloff for more realistic light
            float attenuation = pow(falloff, 2.0) * lights[i].intensity;
            totalLight += lights[i].color.rgb * attenuation;
        }
    }
    
    // Bloom-ready brightness clamping is handled in post-process
    // but we multiply the base color here
    finalColor = vec4(texelColor.rgb * totalLight, texelColor.a); // Final output color
}