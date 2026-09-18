// Alpha-scissor foliage cards (grass tufts, ferns). Gentle wind sway weighted by
// UV.y (0 = top of card, 1 = rooted). Normals bent upward so crossed cards shade softly.
shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_disabled;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_disable;
uniform vec3 tint : source_color = vec3(1.0);
uniform float alpha_cut = 0.45;
uniform float sway = 0.05;
uniform float sway_speed = 1.3;

void vertex() {
	float w = 1.0 - UV.y;
	vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float t = TIME * sway_speed + wp.x * 0.35 + wp.z * 0.27;
	VERTEX.x += sin(t) * sway * w;
	VERTEX.z += cos(t * 0.8) * sway * 0.6 * w;
	NORMAL = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), 0.7));
}

void fragment() {
	vec4 c = texture(albedo_tex, UV);
	ALBEDO = c.rgb * tint * COLOR.rgb;
	ALPHA = c.a;
	ALPHA_SCISSOR_THRESHOLD = alpha_cut;
	ROUGHNESS = 1.0;
}
