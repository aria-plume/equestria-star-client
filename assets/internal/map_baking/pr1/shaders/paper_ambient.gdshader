shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled;
uniform float paper_clock = 0.0;
uniform float paper_motion = 1.0;
uniform float paper_detail = 1.0;
uniform float kind = 0.0; // 0 folded water, 1 local stars, 2 occasional paper leaves
varying float life_alpha;
void vertex() {
    float phase=INSTANCE_CUSTOM.x*6.2831853;
    float d=INSTANCE_CUSTOM.y;
    float t=paper_clock;
    life_alpha=1.0;
    if(kind<0.5) {
        VERTEX.x+=sin(t*0.8+phase)*d*0.025*paper_motion;
        VERTEX.z+=sin(t*0.65+phase)*d*0.012*paper_motion;
        life_alpha=0.45+0.22*sin(t*0.8+phase);
    } else if(kind<1.5) {
        float age=fract(t*0.17+INSTANCE_CUSTOM.x);
        VERTEX.y+=age*d*0.38*paper_motion;
        VERTEX.x+=sin(t*0.7+phase)*d*0.045*paper_motion;
        life_alpha=smoothstep(0.0,0.16,age)*(1.0-smoothstep(0.65,1.0,age))*paper_detail;
    } else if(kind<2.5) {
        float angle=sin(t*1.35+phase)*0.40*paper_motion;
        mat3 turn=mat3(vec3(cos(angle),sin(angle),0.0),vec3(-sin(angle),cos(angle),0.0),vec3(0.0,0.0,1.0));
        VERTEX=turn*VERTEX;NORMAL=turn*NORMAL;
        VERTEX+=vec3(sin(t*0.8+phase)*0.10,sin(t*1.2+phase)*0.045,cos(t*0.6+phase)*0.065)*d*paper_motion;
        life_alpha=smoothstep(-0.55,0.65,sin(t*0.45+phase))*paper_detail;
    } else {
        float age=fract(t*0.15+INSTANCE_CUSTOM.x);
        VERTEX*=mix(0.55,1.35,age);
        VERTEX+=vec3(age*0.12,age*0.34,0.0)*d*paper_motion;
        life_alpha=0.60*smoothstep(0.0,0.12,age)*(1.0-smoothstep(0.55,1.0,age));
    }
}
void fragment() {
    ALBEDO=COLOR.rgb;
    EMISSION=kind>0.5 && kind<1.5?COLOR.rgb*0.35:vec3(0.0);
    ALPHA=COLOR.a*life_alpha;
}
