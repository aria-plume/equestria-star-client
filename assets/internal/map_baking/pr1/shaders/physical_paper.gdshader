shader_type spatial;
render_mode diffuse_lambert, specular_disabled, cull_disabled;
uniform vec4 base_color : source_color = vec4(1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D fibre_texture : filter_linear_mipmap, repeat_enable;
uniform bool use_albedo_texture = false;
uniform bool use_vertex_color = false;
uniform bool vertex_color_srgb = false;
uniform bool terrain_palette = false;
uniform bool world_paper_fields = false;
uniform vec4 region_color : source_color = vec4(0.4,0.6,0.25,1.0);
uniform vec4 core_color : source_color = vec4(0.39,0.34,0.26,1.0);
uniform float grain_strength = 0.0;
uniform float detail_fade = 1.0;
uniform float paper_clock = 0.0;
uniform float paper_motion = 1.0;
uniform float water_flow = 0.0;
varying vec3 paper_world_position;
varying vec3 paper_world_normal;
varying vec3 paper_object_position;
varying vec3 paper_object_normal;
void vertex() {
    paper_world_position = (MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
    paper_world_normal = normalize(mat3(MODEL_MATRIX)*NORMAL);
    paper_object_position = VERTEX;
    paper_object_normal = NORMAL;
}
float paper_noise(vec3 p, vec3 weights) {
    return texture(fibre_texture,p.yz).r*weights.x + texture(fibre_texture,p.xz).r*weights.y + texture(fibre_texture,p.xy).r*weights.z;
}
void fragment() {
    if(detail_fade<0.999) {
        float stipple=fract(sin(dot(floor(FRAGCOORD.xy),vec2(12.9898,78.233)))*43758.5453);
        if(stipple>detail_fade) discard;
    }
    vec3 color = base_color.rgb;
    if (use_albedo_texture) color *= texture(albedo_texture,UV).rgb;
    if (use_vertex_color) {
        vec3 vc=COLOR.rgb;
        if(vertex_color_srgb) vc=mix(pow((vc+0.055)/1.055,vec3(2.4)),vc/12.92,lessThanEqual(vc,vec3(0.04045)));
        color *= vc;
    }
    if(water_flow>0.5) {
        float along=water_flow>1.5?paper_object_position.y:paper_object_position.x+paper_object_position.z;
        color*=1.0+paper_motion*0.045*sin(along*12.0-paper_clock*(water_flow>1.5?2.4:1.25));
    }
    if (terrain_palette) {
        float top = smoothstep(0.48,0.83,abs(paper_world_normal.y));
        color = mix(core_color.rgb,region_color.rgb,top);
        if(world_paper_fields) {
            // Continuous large sheets across a district, independent of individual hex seeds.
            float field=sin(paper_world_position.x*1.75+paper_world_position.z*.77);
            float fold=smoothstep(-.15,.35,field);
            color*=mix(.965,1.045,fold)*top+(1.0-top);
        }
        float footprint=max(length(dFdx(paper_world_position)),length(dFdy(paper_world_position)));
        float fade=1.0-smoothstep(.004,.025,footprint);
        float paper_layers=1.0-.055*fade*(1.0-smoothstep(.18,.42,fract(paper_world_position.y*34.0)));
        color *= mix(paper_layers,1.0,top);
    }
    if(grain_strength>0.0) {
        vec3 weights=pow(abs(normalize(paper_object_normal)),vec3(6.0));weights/=max(dot(weights,vec3(1.0)),.0001);
        float footprint=max(length(dFdx(paper_object_position*4.5)),length(dFdy(paper_object_position*4.5)));
        float fade=1.0-smoothstep(.35,.9,footprint);
        color *= 1.0+(paper_noise(paper_object_position*4.5,weights)-.5)*grain_strength*fade;
    }
    ALBEDO = color;
    ROUGHNESS = 0.88;
    METALLIC = 0.0;
    SPECULAR = 0.0;
}
