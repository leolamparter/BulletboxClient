#version 330

in vec2 fragTexCoord;
out vec4 finalColor;

uniform sampler2D texture0;
uniform float saturation;
uniform float contrast;
uniform float vignetteIntensity;
uniform float fogDensity;
uniform vec4 fogColor;
uniform float dustDensity;
uniform vec4 dustColor;

void main()
{
    vec4 color = texture(texture0, fragTexCoord);
    
    // 1. Contrast
    color.rgb = ((color.rgb - 0.5) * contrast) + 0.5;
    
    // 2. Saturation (Luminance based)
    float luminance = dot(color.rgb, vec3(0.2126, 0.7152, 0.0722));
    color.rgb = mix(vec3(luminance), color.rgb, saturation);
    
    // 3. Vignette
    vec2 uv = fragTexCoord - 0.5;
    float dist = length(uv);
    float vignette = smoothstep(0.8, 0.5 - vignetteIntensity * 0.4, dist);
    color.rgb *= vignette;

    // 4. Fog/Dust Overlays
    if (fogDensity > 0.0) {
        color.rgb = mix(color.rgb, fogColor.rgb, fogDensity * dist * 1.5);
    }
    if (dustDensity > 0.0) {
        color.rgb = mix(color.rgb, dustColor.rgb, dustDensity);
    }

    // 5. Enhanced Bloom Thresholding
    // In a real bloom filter, this would be a second render texture pass.
    // Here we just ensure brights stay vibrant.
    float brightness = dot(color.rgb, vec3(0.2126, 0.7152, 0.0722));
    if (brightness > 0.85) color.rgb += (color.rgb * (brightness - 0.85) * 2.0);

    finalColor = vec4(color.rgb, color.a);
}