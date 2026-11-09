// Original single-pass screen grade. Technique: Godot 4.6 custom postprocessing docs.
// No downloaded shader code, no blur/UV distortion, UI is drawn on higher CanvasLayers.
shader_type canvas_item;
render_mode unshaded;
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;
uniform float warmth : hint_range(0.0, 1.0) = 0.34;
uniform float paper_grain : hint_range(0.0, 0.04) = 0.007;
uniform float saturation : hint_range(0.5, 1.5) = 1.015;
uniform float contrast : hint_range(0.8, 1.2) = 1.06;
uniform float vignette : hint_range(0.0, 0.3) = 0.055;
float hash(vec2 p) { return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }
void fragment() {
    vec3 color = texture(screen_texture, SCREEN_UV).rgb;
    float luminance = dot(color,vec3(0.2126,0.7152,0.0722));
    color = mix(vec3(luminance),color,saturation);
    // Small warm lift in shadows rather than yellowing every white paper surface.
    color += vec3(0.018,0.010,0.003) * warmth * (1.0-luminance);
    color *= mix(vec3(1.0),vec3(1.035,1.012,0.965),warmth);
    color = (color-vec3(0.5))*contrast+vec3(0.5);
    vec2 pixel = floor(SCREEN_UV / SCREEN_PIXEL_SIZE);
    float fibre = (hash(pixel)-0.5)*0.7 + (hash(floor(pixel/3.0))-0.5)*0.3;
    color += fibre * paper_grain * (0.4+0.6*luminance);
    vec2 edge = SCREEN_UV*2.0-1.0;
    color *= 1.0-vignette*dot(edge,edge)*0.45;
    float sunlight=pow(max(0.0,1.0-distance(SCREEN_UV,vec2(.03,.03))*.85),5.0);
    color=mix(color,vec3(1.0,.88,.60),sunlight*.09);
    COLOR = vec4(clamp(color,vec3(0.0),vec3(1.0)),1.0);
}
