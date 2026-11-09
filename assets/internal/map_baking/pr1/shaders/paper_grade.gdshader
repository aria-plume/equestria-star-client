shader_type canvas_item;
render_mode unshaded, blend_disabled;

// LDR canvas pass: Viewport.use_hdr_2d MUST be false. Screen texture is already
// display-referred after 3D tone mapping. Do not add source_color or gamma here.
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;
uniform float strength : hint_range(0.0, 1.0) = 0.50;
uniform float saturation : hint_range(0.0, 1.4) = 1.02;
uniform float contrast : hint_range(0.8, 1.2) = 1.025;
uniform vec3 shadow_shift = vec3(-0.004, -0.001, 0.012);
uniform vec3 highlight_shift = vec3(0.006, 0.003, -0.004);
uniform bool fairy = false;
// Preset 7 only. 0.0 keeps every earlier preset byte-identical to the V5 grade.
uniform float npr_strength = 0.0;
uniform float npr_saturation = 1.20;
uniform float npr_contrast = 0.26;
uniform float npr_shadow_depth = 0.10;
uniform float npr_highlight_soft = 0.06;
// Optional cinematic vignette (preset 8). 0.0 keeps the P7 framing untouched.
uniform float npr_vignette = 0.0;
// Preset 9 "sunlit papercraft" (Claude). 0.0 keeps every earlier preset byte-identical.
uniform float sun_strength = 0.0;
uniform vec3 sun_white_balance = vec3(1.035, 1.0, 0.93);
uniform float sun_saturation = 1.04;
uniform float sun_cyan_desat = 0.22;
uniform float sun_contrast = 0.10;
uniform float sun_shade_lift = 0.035;
uniform vec3 sun_shade_color = vec3(0.42, 0.35, 0.55);
uniform float sun_leak = 0.30;
uniform vec3 sun_leak_color = vec3(1.0, 0.85, 0.60);
uniform vec2 sun_leak_centre = vec2(-0.05, -0.10);
uniform float sun_leak_radius = 0.95;
uniform float sun_highlight_soft = 0.10;
uniform float sun_haze = 0.0;
uniform vec3 sun_haze_color = vec3(0.96, 0.92, 0.84);
uniform float sun_haze_top = 0.42;

void fragment() {
	vec4 src = textureLod(screen_texture, SCREEN_UV, 0.0);
	float y = dot(src.rgb, vec3(0.2126, 0.7152, 0.0722));
	if (sun_strength > 0.0) {
		// Display-referred (post tone map) sRGB values in, same out. All steps are smooth and
		// endpoint-preserving; no blur, sharpening, outline or noise.
		vec3 c = src.rgb * sun_white_balance;                      // warm golden balance
		float yy = dot(c, vec3(0.2126, 0.7152, 0.0722));
		float hi = max(c.r, max(c.g, c.b));
		float lo = min(c.r, min(c.g, c.b));
		// cyan/teal (G,B high, R low) is what makes the board read cold: ease it toward pastel
		float cyan = clamp((min(c.g, c.b) - c.r) * 3.0, 0.0, 1.0);
		float sat = sun_saturation * (1.0 - sun_cyan_desat * cyan);
		c = mix(vec3(yy), c, sat);
		// gentle luminance S-curve, hue preserving
		float s_curve = yy * yy * (3.0 - 2.0 * yy);
		float target = mix(yy, s_curve, sun_contrast);
		c *= target / max(yy, 0.002);
		// lifted, slightly violet paper shade: deep shade never becomes black ink
		float shade = 1.0 - smoothstep(0.0, 0.32, target);
		c = mix(c, c + sun_shade_color * sun_shade_lift, shade);
		// distant haze: the far (upper) part of an orthographic board view drifts toward warm air
		float haze = sun_haze * (1.0 - smoothstep(0.0, sun_haze_top, SCREEN_UV.y));
		c = mix(c, sun_haze_color * mix(0.92, 1.0, target), haze);
		// warm light leak from the sun side, screen blended so it brightens without greying
		vec2 d = (SCREEN_UV - sun_leak_centre) * vec2(1.6, 1.0);
		float leak = sun_leak * exp(-dot(d, d) / max(sun_leak_radius * sun_leak_radius, 0.001));
		c = 1.0 - (1.0 - c) * (1.0 - sun_leak_color * leak);
		// soft shoulder so cream paper keeps its printed edge
		float over = max(max(c.r, max(c.g, c.b)) - 0.88, 0.0);
		c -= c * over * sun_highlight_soft;
		COLOR = vec4(mix(src.rgb, clamp(c, 0.0, 1.0), sun_strength), src.a);
	} else if (npr_strength > 0.0) {
		vec3 c = src.rgb;
		float hi = max(c.r, max(c.g, c.b));
		float lo = min(c.r, min(c.g, c.b));
		float chroma = hi - lo;
		// Paper white and near-neutral highlights keep their printed value.
		float paper_white = smoothstep(0.78, 0.98, y) * (1.0 - smoothstep(0.015, 0.11, chroma));
		// Saturation lives in the mid tones so bright paper does not go fluorescent.
		float mid = smoothstep(0.05, 0.30, y) * (1.0 - smoothstep(0.72, 0.96, y));
		c = mix(vec3(y), c, 1.0 + (npr_saturation - 1.0) * min(1.0, mid + 0.25));
		// Luminance-only S-curve: keeps hue ratios, preserves black and white.
		float s = y * y * (3.0 - 2.0 * y);
		float curve = mix(y, s, npr_contrast);
		// A little extra weight in the deep shadows organises the form,
		// without turning the forest into a black hole.
		curve -= npr_shadow_depth * (1.0 - smoothstep(0.0, 0.34, y)) * y * (1.0 - y) * 2.2;
		c *= curve / max(y, 0.002);
		// Cool paper shadow / warm sunlit paper. Weighted by luminance only.
		float shade = 1.0 - smoothstep(0.10, 0.60, y);
		float light = smoothstep(0.52, 0.95, y);
		c += shadow_shift * shade * (1.0 - paper_white) * 0.85;
		c += highlight_shift * light * (1.0 - paper_white) * 0.90;
		// Shoulder on the brightest paper so printed ink keeps a readable edge.
		float over = max(max(c.r, max(c.g, c.b)) - 0.86, 0.0);
		c -= c * over * npr_highlight_soft;
		c = clamp(c, 0.0, 1.0);
		if (npr_vignette > 0.0) {
			vec2 d = SCREEN_UV - 0.5;
			float r = dot(d, d) * 1.9;
			c *= 1.0 - npr_vignette * smoothstep(0.35, 1.25, r);
		}
		COLOR = vec4(mix(src.rgb, c, npr_strength), src.a);
	} else if (fairy) {
		float hi = max(src.r, max(src.g, src.b));
		float lo = min(src.r, min(src.g, src.b));
		float chroma = hi-lo;
		float sat = chroma / max(hi, 0.001);
		float gain = 1.0+(saturation-1.0)*(1.0-smoothstep(0.35,0.92,sat));
		vec3 c = mix(vec3(y),src.rgb,gain);
		// Smooth endpoint-preserving contrast; neither paper white nor deep forest clips.
		float curve = (y-0.5)*sin(3.14159265*y)*(contrast-1.0)*1.7;
		c += vec3(curve);
		float paper_white = smoothstep(0.72,0.97,y)*(1.0-smoothstep(0.025,0.17,chroma));
		float protected = smoothstep(0.015,0.15,y)*(1.0-paper_white);
		c += (shadow_shift*(1.0-smoothstep(0.16,0.58,y)) + highlight_shift*smoothstep(0.48,0.90,y))*protected;
		c = mix(c,src.rgb,paper_white*0.90);
		COLOR=vec4(mix(src.rgb,clamp(c,0.0,1.0),strength),src.a);
	} else {
	vec3 c = mix(vec3(y), src.rgb, saturation);
	c = (c - vec3(0.5)) * contrast + vec3(0.5);
	float low = 1.0 - smoothstep(0.12, 0.55, y);
	float high = smoothstep(0.50, 0.92, y);
	// Protect blacks and white paper; no haze, sharpening, outlines, blur or noise.
	float protection = smoothstep(0.02, 0.12, y) * (1.0 - smoothstep(0.88, 1.0, y));
	c += (shadow_shift * low + highlight_shift * high) * protection;
	COLOR = vec4(mix(src.rgb, clamp(c, 0.0, 1.0), strength), src.a);
	}
}
