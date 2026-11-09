shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx, cull_disabled;
uniform vec4 base_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D fibre_texture : filter_linear_mipmap, repeat_enable;
uniform bool use_albedo_texture = false;
uniform bool use_vertex_color = false;
uniform bool terrain_palette = false;
uniform vec4 region_color : source_color = vec4(0.4,0.6,0.25,1.0);
uniform vec4 core_color : source_color = vec4(0.39,0.34,0.26,1.0);
uniform float grain_strength = 0.16;
uniform float detail_fade = 1.0;
varying vec3 paper_world_position;
varying vec3 paper_world_normal;
void vertex() {
    paper_world_position = (MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
    paper_world_normal = normalize(mat3(MODEL_MATRIX)*NORMAL);
}
float paper_noise(vec3 p, vec3 weights) {
    return texture(fibre_texture,p.yz).r*weights.x + texture(fibre_texture,p.xz).r*weights.y + texture(fibre_texture,p.xy).r*weights.z;
}
void fragment() {
    if(detail_fade<0.999) {
        float stipple=fract(sin(dot(floor(FRAGCOORD.xy),vec2(12.9898,78.233)))*43758.5453);
        if(stipple>detail_fade) discard;
    }
    vec3 weights = pow(abs(normalize(paper_world_normal)),vec3(6.0));
    weights /= max(dot(weights,vec3(1.0)),0.0001);
    float grain = paper_noise(paper_world_position*4.5,weights);
    float broad = paper_noise(paper_world_position*.31,weights);
    vec3 color = base_color.rgb;
    if (use_albedo_texture) color *= texture(albedo_texture,UV).rgb;
    if (use_vertex_color) color *= COLOR.rgb;
    if (terrain_palette) {
        float top = smoothstep(0.48,0.83,abs(paper_world_normal.y));
        color = mix(core_color.rgb,region_color.rgb,top);
        float paper_layers = mix(0.70,1.03,smoothstep(.18,.42,fract(paper_world_position.y*105.0)));
        color *= mix(paper_layers,1.0,top);
    }
    color *= 1.0 + (grain-.5)*grain_strength + (broad-.5)*.12;
    ALBEDO = color * (terrain_palette ? 1.0 : .94);
    ROUGHNESS = 0.95;
    METALLIC = 0.0;
    SPECULAR = 0.16;
    // Fine screen derivatives add only the micro relief; original large folded normals remain.
    NORMAL = normalize(NORMAL + vec3(-dFdx(grain),-dFdy(grain),0.0)*.12);
}
