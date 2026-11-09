shader_type spatial;
// Papercraft NPR lighting branch.
// Only used when the active art profile asks for it (ArtRenderProfile preset 7).
// physical_paper.gdshader stays untouched and remains the V5 control.
// Every uniform that exists in physical_paper.gdshader is kept with the same name,
// so ConceptPaperStyle can create/refresh this material the same way.
render_mode specular_disabled, cull_disabled;

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

// --- NPR lighting response (wide, smooth paper bands; no hard cel steps) ---
// npr_wrap: how far the terminator is pushed into the back side (paper fibre scatter).
// npr_band_lo / npr_band_hi: smoothstep window of the light/shade separation.
// npr_grad: how much of the smooth N.L falloff survives inside the lit region.
// npr_shade: portion of the key light still present on unlit-but-unshadowed faces.
// npr_lit_gain: overall multiplier on the direct light term.
uniform float npr_wrap : hint_range(0.0, 1.0) = 0.34;
uniform float npr_band_lo : hint_range(0.0, 0.95) = 0.30;
uniform float npr_band_hi : hint_range(0.0, 1.0) = 0.74;
uniform float npr_grad : hint_range(0.0, 1.0) = 0.55;
uniform float npr_shade : hint_range(0.0, 0.5) = 0.20;
uniform float npr_lit_gain : hint_range(0.2, 1.6) = 1.0;
// Cool the light response as a face turns away from a given light: this is the
// papercraft "coloured dark" (sky bounce), and it is multiplicative, so stacking
// more lights never adds a flat base tone.
uniform vec3 npr_shade_tint = vec3(0.60, 0.70, 1.0);
uniform float npr_shade_tint_mix : hint_range(0.0, 1.0) = 0.55;
// Large, gentle paper sheets: only up-facing paper leans; this is what gives the
// board's big flat hex tops readable light/shade organisation without new geometry.
uniform float npr_fold : hint_range(0.0, 1.5) = 0.0;
uniform float npr_fold_scale : hint_range(0.2, 6.0) = 1.35;
uniform float npr_fold_lift : hint_range(0.0, 1.0) = 0.0;

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

// Two-scale low-frequency fold field. Returns a world-space slope for the sheet.
vec2 paper_fold_slope(vec2 p, float scale) {
    vec2 d1 = vec2(0.62, 0.34);
    vec2 d2 = vec2(-0.28, 0.71);
    float k1 = scale;
    float k2 = scale*1.37;
    float a = 0.62*cos(dot(p,d1)*k1);
    float b = 0.44*cos(dot(p,d2)*k2 + 1.9);
    float c = 0.18*cos(dot(p,vec2(0.15,-0.93))*k2*2.3 + 4.1);
    return d1*a + d2*b + vec2(0.15,-0.93)*c;
}

void light() {
    float ndl = dot(NORMAL, LIGHT);
    // Wide, smooth wrap: paper is not a mirror, its fibre carries light past the
    // geometric terminator without turning the whole model into a flat card.
    float w = clamp((ndl + npr_wrap) / (1.0 + npr_wrap), 0.0, 1.0);
    float lit = smoothstep(npr_band_lo, npr_band_hi, w);
    float response = mix(npr_shade, 1.0, lit) * mix(1.0, w, npr_grad);
    vec3 tint = mix(vec3(1.0), npr_shade_tint, (1.0 - lit) * npr_shade_tint_mix);
    DIFFUSE_LIGHT += LIGHT_COLOR * (1.0 / PI) * ATTENUATION * response * tint * npr_lit_gain;
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
    float top = smoothstep(0.48,0.83,abs(paper_world_normal.y));
    if (terrain_palette) {
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
        if (npr_fold > 0.0) {
            // Give the huge flat hex tops a slow, readable paper-sheet lean.
            // Only the shading normal moves; geometry, silhouette and shadow bias stay put.
            vec2 slope = paper_fold_slope(paper_world_position.xz, npr_fold_scale) * npr_fold;
            vec3 wn = normalize(vec3(-slope.x, 1.0, -slope.y));
            vec3 blended = normalize(mix(paper_world_normal, wn, top));
            NORMAL = normalize((VIEW_MATRIX * vec4(blended, 0.0)).xyz);
            // Paper sheets read slightly darker where they turn away from the sky.
            color *= mix(1.0, 1.0 - npr_fold_lift*clamp(slope.y*0.5+slope.x*0.5,-1.0,1.0), top);
        }
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
