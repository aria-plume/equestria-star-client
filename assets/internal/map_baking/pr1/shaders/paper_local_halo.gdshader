shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
uniform float paper_clock = 0.0;
uniform float paper_motion = 1.0;
uniform vec4 halo_color : source_color=vec4(0.48,0.64,1.0,1.0);
uniform float halo_strength=0.22;
uniform float phase=0.0;
void fragment() {
    float r=length(UV-vec2(0.5))*2.0;
    float ring=exp(-pow((r-0.62)*12.0,2.0))*0.40;
    float soft=(1.0-smoothstep(0.05,0.96,r))*0.22;
    float pulse=1.0+sin(paper_clock*0.95+phase)*0.12*paper_motion;
    ALBEDO=halo_color.rgb;
    ALPHA=(ring+soft)*halo_strength*pulse;
}
