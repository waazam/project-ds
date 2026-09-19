// Boulders: dark stone with moss growing over the upward-facing faces
// (world-normal blend, so it stays on top however the instance is rotated).
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;

uniform sampler2D rock_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D moss_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform float moss_amount = 0.5;   // 0 bare .. 1 carpeted
uniform float moss_tile = 0.9;
uniform vec3 rock_tint : source_color = vec3(0.85, 0.85, 0.85);

varying vec3 wpos;
varying vec3 wnrm;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 rock = texture(rock_tex, UV).rgb * rock_tint;
	vec3 moss = texture(moss_tex, wpos.xz * moss_tile).rgb;
	float n = texture(noise_tex, wpos.xz * 0.21 + wpos.y * 0.13).r;
	float up = wnrm.y + (n - 0.5) * 0.7 + (moss_amount - 0.5) * 0.9;
	float m = smoothstep(0.25, 0.55, up);
	vec3 c = mix(rock, moss, m);
	// dark, damp base where the rock meets the ground
	c *= mix(0.55, 1.0, smoothstep(-0.6, 0.2, wnrm.y));
	ALBEDO = c * COLOR.rgb;
	ROUGHNESS = 1.0;
}
