shader_type spatial;
render_mode unshaded, specular_disabled, shadows_disabled, cull_disabled;
uniform float paper_clock = 0.0;
uniform float paper_motion = 1.0;
uniform float paper_overview = 1.0;
uniform vec2 map_centre;
uniform float hex_diameter=1.1;
// Preset 9 sunlit sea (Claude): 0 keeps V5 byte-identical.
uniform float sun_mix=0.0;
varying vec2 world;
void vertex(){world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xz;}
void fragment(){
    vec2 p=(world-map_centre)/hex_diameter;
    // Broad, quiet paper sheets: static contours have no high frequency noise.
    float depth=sin(p.x*.23+p.y*.11)+.55*cos(p.y*.29-p.x*.08);
    vec3 shallow=mix(vec3(.24,.48,.44),vec3(.22,.52,.44),sun_mix), water=mix(vec3(.10,.30,.38),vec3(.06,.26,.31),sun_mix), deep=mix(vec3(.07,.20,.29),vec3(.03,.19,.28),sun_mix);
    vec3 c=mix(water,shallow,smoothstep(-.15,1.3,depth));
    c=mix(c,deep,(1.0-smoothstep(-1.3,-.40,depth))*.70);
    float band=sin(p.y*1.7+p.x*.45+sin(p.x*.20)*1.2-paper_clock*.24);
    float broken=smoothstep(.15,.8,sin(p.x*.75+p.y*.13));
    float wave=smoothstep(.975,.997,band)*broken;
    c=mix(c,mix(vec3(.68,.81,.78),vec3(.95,.92,.80),sun_mix),wave*mix(.21,.34,sun_mix)*paper_motion);
    // P9 widest zoom: the sea steps back with the rest of the exterior
    float l=dot(c,vec3(.2126,.7152,.0722));c=mix(c,mix(vec3(l),c,.45)*.85+vec3(.70,.74,.74)*.15,clamp(paper_overview,0.0,1.0)*.25*sun_mix);
    ALBEDO=c;ROUGHNESS=1.0;
}
