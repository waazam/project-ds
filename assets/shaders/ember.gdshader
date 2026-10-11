// Logs burnt nearly through (2026-10-10b, the owner: "the fire place to be better and have a little smolderings of fire
// left"): charred black and split, the cracks in them glowing orange from inside, breathing slowly brighter and dimmer
// (a few seconds a breath, never a flicker), the glow crawling a little along the grain.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 char_col : source_color = vec3(0.04, 0.035, 0.03);
uniform vec3 ash_col : source_color = vec3(0.32, 0.3, 0.28);
uniform vec3 glow_col : source_color = vec3(1.0, 0.38, 0.08);
uniform float glow = 2.2;

varying vec3 v_obj;

void vertex() { v_obj = VERTEX; }

void fragment() {
	vec2 p = v_obj.xz * 3.1 + vec2(v_obj.y * 2.3, 0.0);
	float n1 = texture(noise_tex, p * 0.5 + vec2(TIME * 0.006, 0.0)).r;
	float n2 = texture(noise_tex, p * 1.7 + vec2(0.0, TIME * 0.011)).r;
	float cracks = smoothstep(0.58, 0.72, n1 * 0.6 + n2 * 0.4);
	float ash = smoothstep(0.35, 0.2, n2) * 0.6;
	float breath = 0.7 + 0.3 * sin(TIME * 0.9 + n1 * 6.0);
	ALBEDO = mix(mix(char_col, ash_col, ash), glow_col * 0.4, cracks);
	ROUGHNESS = 0.95;
	EMISSION = glow_col * cracks * glow * breath;
}
