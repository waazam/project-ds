// Alpha-scissor foliage cards (grass tufts, ferns, conifer boughs). Gentle wind
// sway weighted by UV.y (0 = tip of card, 1 = rooted). Normals bent upward so
// crossed cards shade softly. Alpha is boosted with distance (mip level) so
// cards don't thin out to nothing far away.
shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_disabled;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_disable;
uniform vec3 tint : source_color = vec3(1.0);
uniform float alpha_cut = 0.45;
uniform float sway = 0.05;
uniform float sway_speed = 1.3;
uniform float normal_up = 0.7;
uniform float mip_boost = 0.35;
uniform vec2 tex_size = vec2(64.0, 64.0);
/// Extra darkening of the back (under) side of cards.
uniform float back_shade = 0.0;

void vertex() {
	float w = 1.0 - UV.y;
	vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float t = TIME * sway_speed + wp.x * 0.35 + wp.z * 0.27;
	VERTEX.x += sin(t) * sway * w;
	VERTEX.z += cos(t * 0.8) * sway * 0.6 * w;
	NORMAL = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), normal_up));
}

void fragment() {
	vec4 c = texture(albedo_tex, UV);
	vec2 d = UV * tex_size;
	float lod = 0.5 * log2(max(dot(dFdx(d), dFdx(d)), dot(dFdy(d), dFdy(d))));
	float a = c.a * (1.0 + max(lod, 0.0) * mip_boost);
	float back = FRONT_FACING ? 1.0 : 1.0 - back_shade;
	ALBEDO = c.rgb * tint * COLOR.rgb * back;
	ALPHA = a;
	ALPHA_SCISSOR_THRESHOLD = alpha_cut;
	ROUGHNESS = 1.0;
}
