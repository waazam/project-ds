// The stalker's hide and rags. A variant of entity_dither.gdshader: the same
// matte near-black, lit and fogged like everything else, with the same PS2
// screen-door dissolve (`visibility` below 1 punches out a growing share of
// pixels in a 4x4 ordered pattern). Two-sided so the hanging rag strips can be
// single sheets.
//
// Vertex colour carries per-part tone (set by StalkerBody):
//   r = paleness (0 = body, higher = the faint face plane)
//   g = brightness multiplier (low in the eye sockets and rag undersides)
//   b = wetness (a dim cold sheen on the hide)
//   a = 0 marks the eyes: a faint, sickly yellow glow (fogged like everything else)
shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_schlick_ggx;

uniform vec3 albedo : source_color = vec3(0.026, 0.028, 0.034);
uniform vec3 face_tint : source_color = vec3(0.21, 0.215, 0.22);
uniform float visibility : hint_range(0.0, 1.0) = 1.0;
uniform float wetness : hint_range(0.0, 1.0) = 0.6;
uniform vec3 eye_color : source_color = vec3(1.0, 0.78, 0.22);
// Kept low on purpose: at 15-30 m in fog the eyes should be barely there.
uniform float eye_glow : hint_range(0.0, 8.0) = 0.3;

const float BAYER[16] = float[16](
	0.0, 8.0, 2.0, 10.0,
	12.0, 4.0, 14.0, 6.0,
	3.0, 11.0, 1.0, 9.0,
	15.0, 7.0, 13.0, 5.0);

void fragment() {
	ivec2 p = ivec2(mod(FRAGCOORD.xy, 4.0));
	float threshold = (BAYER[p.y * 4 + p.x] + 0.5) / 16.0;
	if (visibility < threshold) discard;
	if (!FRONT_FACING) NORMAL = -NORMAL;
	ALBEDO = mix(albedo, face_tint, COLOR.r) * COLOR.g;
	float wet = wetness * COLOR.b;
	ROUGHNESS = mix(1.0, 0.32, wet);
	SPECULAR = 0.5 * wet;
	float eye = 1.0 - COLOR.a;
	EMISSION = eye_color * eye_glow * eye;
}
