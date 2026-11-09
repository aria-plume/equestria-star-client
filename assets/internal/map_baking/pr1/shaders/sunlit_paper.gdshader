shader_type spatial;
// P9 "Sunlit papercraft" material (Claude, 2026-10-26).
// Only used when ArtRenderProfile preset 9 is active. physical_paper.gdshader (V5) and
// physical_paper_npr.gdshader (P7/P8) stay untouched as controls.
// Every uniform of physical_paper.gdshader is kept with the same name, so ConceptPaperStyle
// creates/refreshes this material exactly like the other two.
//
// What this adds, in the order a viewer reads it:
//   1. Crumpled paper sheets: a tileable facet/crease texture perturbs the shading normal
//      (world-space, per hex card randomly rotated) and darkens the creases a little.
//   2. Hex paper cards: a cream raised rim with a bevel that catches the sun, and a soft
//      crease line just inside it, so every tile reads as a separate thick card.
//   3. Sunlit response: wide warm wrap on the lit side, cool tinted fall-off on the shade
//      side, real shadows through ATTENUATION, and a faint fibre sheen at grazing sun.
render_mode cull_disabled;

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
uniform float paper_overview = 1.0;   // 1 at the widest zoom: exterior scenery steps back
uniform bool scenery = false;          // exterior world (not board cells, landmarks or pawns)
uniform float water_flow = 0.0;

// --- crumpled paper (RG slope, B crease cavity, A height; linear data) ---
uniform sampler2D crumple_texture : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_crumple = false;
uniform float crumple_scale = 1.45;          // world units per texture repeat on hex tops
uniform float crumple_object_scale = 0.62;   // world units per repeat on buildings/props (triplanar)
uniform float crumple_top = 0.34;            // normal strength on terrain tops
uniform float crumple_other = 0.16;          // normal strength elsewhere
uniform float crumple_ink = 0.20;            // crease darkening

// --- fine cardstock grain (RG slope, B pit cavity, A height; linear data). Claude Code ---
uniform sampler2D grain_texture : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_grain = false;
uniform float grain_scale = 0.22;            // world units per repeat
uniform float grain_normal = 0.0;            // normal strength
uniform float grain_albedo = 0.0;            // pit darkening + low-frequency mottle

// --- seam between flush hex cards: one crisp paper-white line (terrain only) ---
uniform float seam_width = 0.0;              // fraction of hex radius, per card (0 = off)
uniform vec4 seam_color : source_color = vec4(0.97, 0.95, 0.90, 1.0);

// --- hex paper card rim (terrain only) ---
uniform float hex_radius = 0.55;
uniform float rim_width : hint_range(0.0, 0.5) = 0.12;
uniform float rim_tilt : hint_range(0.0, 2.0) = 0.70;
uniform float rim_lift : hint_range(0.0, 1.0) = 0.42;
uniform vec4 rim_color : source_color = vec4(0.98, 0.95, 0.86, 1.0);
uniform float rim_crease : hint_range(0.0, 1.0) = 0.16;
uniform float side_tint : hint_range(0.0, 1.0) = 0.65;   // card side = darker region colour instead of brown core

// --- colour of the printed paper ---
uniform float albedo_saturation = 1.0;
uniform float albedo_lift = 0.0;

// --- sunlit response (per light) ---
uniform float sun_wrap : hint_range(0.0, 1.0) = 0.42;
uniform float sun_band_lo : hint_range(0.0, 1.0) = 0.18;
uniform float sun_band_hi : hint_range(0.0, 1.0) = 0.78;
uniform float sun_grad : hint_range(0.0, 1.0) = 0.55;
uniform float sun_shade_floor : hint_range(0.0, 0.6) = 0.10;
uniform vec3 sun_shade_tint = vec3(0.72, 0.80, 1.0);
uniform float sun_shade_tint_mix : hint_range(0.0, 1.0) = 0.60;
uniform float sun_sheen : hint_range(0.0, 1.0) = 0.10;

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

float hash12(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

// Sample the crumple map; returns slope (xy) in the sample's own uv frame, cavity (z), height (w).
vec4 crumple_at(vec2 uv) {
    vec4 t = texture(crumple_texture, uv);
    return vec4(t.rg * 2.0 - 1.0, t.b, t.a);
}

void light() {
    float ndl = dot(NORMAL, LIGHT);
    // Paper fibre carries light a little past the terminator: wide, smooth wrap.
    float w = clamp((ndl + sun_wrap) / (1.0 + sun_wrap), 0.0, 1.0);
    float lit = smoothstep(sun_band_lo, sun_band_hi, w);
    float response = mix(sun_shade_floor, 1.0, lit) * mix(1.0, w, sun_grad);
    // Faces turning away from this light fall off into a cool tint (multiplicative, so adding
    // lights never adds a flat base tone). Cast shadows come through ATTENUATION.
    vec3 tint = mix(vec3(1.0), sun_shade_tint, (1.0 - lit) * sun_shade_tint_mix);
    DIFFUSE_LIGHT += LIGHT_COLOR * (1.0 / PI) * ATTENUATION * response * tint;
    // A faint broad fibre sheen when the sun grazes a crease facet: no plastic hotspot.
    if (sun_sheen > 0.0) {
        vec3 h = normalize(LIGHT + VIEW);
        float s = pow(clamp(dot(NORMAL, h), 0.0, 1.0), 10.0) * smoothstep(0.0, 0.35, ndl);
        SPECULAR_LIGHT += LIGHT_COLOR * (1.0 / PI) * ATTENUATION * s * sun_sheen;
    }
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
    // geometric normal as Godot provides it (inverse-transpose, already flipped for back faces)
    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    float top = smoothstep(0.48,0.83,abs(wn.y));
    vec3 n_world = wn;
    float cavity = 1.0;
    // Screen footprint: fades the procedural detail out before it can shimmer at overview zoom.
    float footprint = max(length(dFdx(paper_world_position)), length(dFdy(paper_world_position)));

    if (terrain_palette) {
        vec3 side_color = mix(core_color.rgb, region_color.rgb * 0.62, side_tint);
        color = mix(side_color, region_color.rgb, top);
        if(world_paper_fields) {
            float field=sin(paper_world_position.x*1.75+paper_world_position.z*.77);
            float fold=smoothstep(-.15,.35,field);
            color*=mix(.975,1.03,fold)*top+(1.0-top);
        }
        float fade=1.0-smoothstep(.004,.025,footprint);
        float paper_layers=1.0-.055*fade*(1.0-smoothstep(.18,.42,fract(paper_world_position.y*34.0)));
        color *= mix(paper_layers,1.0,top);

        // ---- hex paper card: rim, bevel and inner crease (world space, cell-centred) ----
        vec2 rel = (paper_world_position.xz - NODE_POSITION_WORLD.xz) / max(hex_radius, 0.001);
        const vec2 e0 = vec2(1.0, 0.0);
        const vec2 e1 = vec2(0.5, 0.8660254);
        const vec2 e2 = vec2(-0.5, 0.8660254);
        float d0 = dot(rel, e0), d1 = dot(rel, e1), d2 = dot(rel, e2);
        float a0 = abs(d0), a1 = abs(d1), a2 = abs(d2);
        float hexd = max(a0, max(a1, a2)) / 0.8660254;   // 0 centre .. 1 outline
        vec2 outward = a0 >= a1 && a0 >= a2 ? e0 * sign(d0) : (a1 >= a2 ? e1 * sign(d1) : e2 * sign(d2));
        float rim = smoothstep(1.0 - rim_width, 1.0 - rim_width * 0.35, hexd) * top;
        float aa = clamp(footprint / max(hex_radius * rim_width, 0.0005), 0.0, 1.0);
        rim = mix(rim, rim * 0.6, aa);
        // inner crease: a thin soft valley just inside the rim
        float creaseline = exp(-pow((hexd - (1.0 - rim_width)) / max(rim_width * 0.22, 0.001), 2.0)) * top * (1.0 - aa);
        color = mix(color, rim_color.rgb * mix(vec3(1.0), region_color.rgb / max(max(region_color.r, max(region_color.g, region_color.b)), 0.05), 0.18), rim * rim_lift);
        color *= 1.0 - creaseline * rim_crease;
        // rim bevel leans outward: catches the sun on one side, falls into cool shade on the other
        n_world = normalize(n_world + vec3(outward.x, 0.0, outward.y) * rim_tilt * rim * sign(wn.y));
        // the crease line itself leans inward a little
        n_world = normalize(n_world - vec3(outward.x, 0.0, outward.y) * rim_tilt * 0.35 * creaseline * sign(wn.y));

        // ---- crumpled sheet on the card top, one random orientation per hex card ----
        if (use_crumple && top > 0.01) {
            float hsh = hash12(floor(NODE_POSITION_WORLD.xz * 7.31) + 0.5);
            float ang = hsh * 6.2831853;
            mat2 rot = mat2(vec2(cos(ang), sin(ang)), vec2(-sin(ang), cos(ang)));
            vec2 uv = rot * (paper_world_position.xz / crumple_scale) + vec2(hsh * 7.13, hsh * 3.71);
            vec4 c = crumple_at(uv);
            vec2 slope = transpose(rot) * c.xy;   // back to world XZ
            float k = crumple_top * top * (1.0 - rim * 0.6);
            n_world = normalize(n_world + vec3(-slope.x, 0.0, -slope.y) * k * sign(wn.y));
            cavity = mix(1.0, c.z, top);
        }

        // ---- fine cardstock grain on the card top ----
        if (use_grain && top > 0.01) {
            vec2 guv = paper_world_position.xz / grain_scale;
            vec4 g = texture(grain_texture, guv);
            vec2 gs = g.rg * 2.0 - 1.0;
            n_world = normalize(n_world + vec3(-gs.x, 0.0, -gs.y) * grain_normal * top * sign(wn.y));
            float mottle = texture(grain_texture, guv * 0.137 + vec2(0.31, 0.77)).a;
            color *= mix(1.0, g.b * mix(0.94, 1.06, mottle), grain_albedo * top);
        }

        // ---- seam: flush cards meet in a single crisp paper-white line ----
        if (seam_width > 0.0) {
            float w = max(seam_width, footprint * 0.75 / max(hex_radius, 0.001));
            float aaw = footprint * 0.6 / max(hex_radius, 0.001) + 0.002;
            // the card top sits at the cell origin: the bevel groove and a thin lip of the side
            // wall join the line, deeper wall keeps the card-side colour
            float depth = (NODE_POSITION_WORLD.y - paper_world_position.y) / max(hex_radius, 0.001);
            float seam = smoothstep(1.0 - w - aaw, 1.0 - w + aaw, hexd) * (1.0 - smoothstep(0.055, 0.075, depth));
            color = mix(color, seam_color.rgb, seam);
            n_world = normalize(mix(n_world, vec3(0.0, 1.0, 0.0), seam));
            cavity = mix(cavity, 1.0, seam);
        }
    } else if (use_crumple && crumple_other > 0.0) {
        // ---- triplanar crumple for buildings, props and folded landforms ----
        vec3 aw = pow(abs(wn), vec3(4.0));
        aw /= max(dot(aw, vec3(1.0)), 0.0001);
        vec3 p = paper_world_position / crumple_object_scale;
        vec4 cx = crumple_at(p.zy);
        vec4 cy = crumple_at(p.xz + vec2(0.37, 0.61));
        vec4 cz = crumple_at(p.xy + vec2(0.71, 0.13));
        vec3 pert = aw.x * vec3(0.0, -cx.y, -cx.x) + aw.y * vec3(-cy.x, 0.0, -cy.y) + aw.z * vec3(-cz.x, -cz.y, 0.0);
        float fadeo = 1.0 - smoothstep(0.012, 0.05, footprint);
        n_world = normalize(n_world + pert * crumple_other * fadeo);
        cavity = mix(1.0, aw.x * cx.z + aw.y * cy.z + aw.z * cz.z, fadeo);
        if (use_grain && grain_albedo > 0.0) {
            vec3 q = paper_world_position / grain_scale;
            float gp = aw.x * texture(grain_texture, q.zy).b + aw.y * texture(grain_texture, q.xz).b + aw.z * texture(grain_texture, q.xy).b;
            color *= mix(1.0, gp, grain_albedo * 0.5);
        }
    }
    color *= mix(1.0, cavity, crumple_ink);

    if(grain_strength>0.0) {
        vec3 weights=pow(abs(normalize(paper_object_normal)),vec3(6.0));weights/=max(dot(weights,vec3(1.0)),.0001);
        float gfoot=max(length(dFdx(paper_object_position*4.5)),length(dFdy(paper_object_position*4.5)));
        float gfade=1.0-smoothstep(.35,.9,gfoot);
        color *= 1.0+(paper_noise(paper_object_position*4.5,weights)-.5)*grain_strength*gfade;
    }

    // printed-paper colour adjustment (linear): pastel lift + saturation, preserves hue
    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
    color = max(mix(vec3(luma), color, albedo_saturation), vec3(0.0));
    color = mix(color, vec3(1.0), albedo_lift * (1.0 - luma));

    // Widest zoom: the world outside the board fades toward muted paper so pawns and cells lead.
    if (scenery && paper_overview > 0.0) {
        float l = dot(color, vec3(0.2126, 0.7152, 0.0722));
        vec3 muted = mix(vec3(l), color, 0.35) * 0.8 + vec3(0.78, 0.76, 0.70) * 0.2;
        color = mix(color, muted, clamp(paper_overview, 0.0, 1.0) * 0.28);
    }
    NORMAL = normalize((VIEW_MATRIX * vec4(n_world, 0.0)).xyz);
    ALBEDO = color;
    ROUGHNESS = 0.9;
    METALLIC = 0.0;
    SPECULAR = 0.12;
}
