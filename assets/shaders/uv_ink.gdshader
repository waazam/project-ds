// Ink only the blacklight shows (the lantern's second mode): R.H.'s numbers on the trees, handprints,
// smears, footprints to the bunker. Invisible under any other light; where the lantern's violet cone
// falls on it, it fluoresces: a cold cyan-white (or a sick green) with the speckled, fibrous grain of
// real fluorescent residue, brightest at the heart of the beam and fading at its edge and with range.
// The lantern writes the globals every frame (Lantern.cs).
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

global uniform vec3 uv_light_pos;
global uniform vec3 uv_light_dir;
global uniform float uv_light_on;
global uniform float uv_light_range;

uniform sampler2D mask_tex : filter_linear_mipmap, repeat_disable;
uniform sampler2D grain_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 ink : source_color = vec3(0.55, 0.85, 1.0);
uniform float strength = 1.6;
/// Cosines of the beam's half-angles: full inside `inner`, gone outside `outer`.
uniform float cos_inner = 0.974;
uniform float cos_outer = 0.9135;   // the lantern's 24 degree cone (Lantern.UvAngle)

varying vec3 v_world;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 to = v_world - uv_light_pos;
	float d = length(to);
	float cone = smoothstep(cos_outer, cos_inner, dot(to / max(d, 0.001), normalize(uv_light_dir)));
	float fall = 1.0 - smoothstep(uv_light_range * 0.45, uv_light_range, d);
	float lit = uv_light_on * cone * fall;
	float m = texture(mask_tex, UV).a;
	float g = texture(grain_tex, v_world.xz * 3.1 + v_world.y * 2.3).r;
	float speck = 0.65 + 0.7 * g;
	ALBEDO = ink * m * lit * strength * speck;
}
