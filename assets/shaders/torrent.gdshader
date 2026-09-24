// Room 2's torrent: a sheet of water pouring in through the broken window, streaking downward
// (or, with `flow` negative, being drawn back up and out). `tint` goes from lake water to blood.
shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 tint : source_color = vec3(0.1, 0.13, 0.1);
uniform float flow = 1.0;
uniform float opacity : hint_range(0.0, 1.0) = 1.0;

void fragment() {
	vec2 uv = UV * vec2(2.0, 1.0);
	float t = TIME * 1.6 * flow;
	float n1 = texture(noise_tex, uv * vec2(1.0, 0.4) + vec2(0.0, -t)).r;
	float n2 = texture(noise_tex, uv * vec2(2.3, 0.7) + vec2(0.3, -t * 1.7)).r;
	float streak = smoothstep(0.45, 0.8, n1 * 0.6 + n2 * 0.4);
	vec3 c = mix(tint, tint * 2.8 + vec3(0.12), streak);
	float edge = smoothstep(0.0, 0.12, UV.x) * smoothstep(1.0, 0.88, UV.x);
	ALBEDO = c;
	ROUGHNESS = 0.1;
	SPECULAR = 0.6;
	ALPHA = opacity * edge * (0.65 + 0.35 * streak);
}
