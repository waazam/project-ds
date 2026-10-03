// Ivy leaves (the vine door, 2026-10-03; the owner: the vines "need to look way better and more tangible"). Each leaf
// is a card folded along its midrib, one leaf out of a 2 x 2 atlas (BunkerTextures.IvyLeafAtlas). Ivy's leaves are
// waxy: a soft sheen where the lamps and the lantern catch them, the undersides paler and duller, light coming through
// them a little. They tremble faintly in the tunnel's draught, the blade more than the stalk.
shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_schlick_ggx;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_disable;
uniform float alpha_cut = 0.5;
uniform float tremble = 0.004;
uniform float tremble_speed = 0.7;
/// The underside: paler and duller (ivy's is a lighter, matt green).
uniform vec3 under_tint : source_color = vec3(0.85, 1.0, 0.75);
uniform float gloss = 0.5;
/// Light through the leaf from behind.
uniform float translucency = 0.14;

void vertex() {
	// the blade's distance from the stalk: the cell's bottom (v = 1 within the cell) is the stalk
	float blade = 1.0 - fract(UV.y * 2.0 - 0.0001);
	vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float t = TIME * tremble_speed + wp.x * 3.1 + wp.y * 2.3 + wp.z * 1.7;
	VERTEX += NORMAL * (sin(t) + 0.5 * sin(t * 2.7)) * tremble * blade;
}

void fragment() {
	vec4 c = texture(albedo_tex, UV);
	ALBEDO = FRONT_FACING ? c.rgb * COLOR.rgb : c.rgb * COLOR.rgb * under_tint * 0.9;
	ALPHA = c.a;
	ALPHA_SCISSOR_THRESHOLD = alpha_cut;
	ROUGHNESS = FRONT_FACING ? gloss : 0.8;
	SPECULAR = FRONT_FACING ? 0.32 : 0.1;
	BACKLIGHT = c.rgb * translucency;
}
