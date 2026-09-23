// The giant's hide (Act 7's crossing beyond the burning cabin, Act 11's top of the stairs): stalker_skin.gdshader
// with the fog switched off and a `haze` blend in its place. The Act 7 giant walks 150 m off behind the
// far tree line, where the scene's fog would swallow it entirely; drawn fog-free it stays a dark shape
// over the trees, `haze` lifts it toward the fog colour by hand and `contrast` flattens the shading so it
// looms as a shadow in the murk rather than a lit model. Everything else (dither, face plane, eyes) is the same.
shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_schlick_ggx, fog_disabled;

uniform vec3 albedo : source_color = vec3(0.026, 0.028, 0.034);
uniform vec3 face_tint : source_color = vec3(0.21, 0.215, 0.22);
uniform float visibility : hint_range(0.0, 1.0) = 1.0;
uniform float wetness : hint_range(0.0, 1.0) = 0.6;
uniform vec3 eye_color : source_color = vec3(1.0, 0.78, 0.22);
uniform float eye_glow : hint_range(0.0, 8.0) = 0.3;
// How much of the fog's colour lies over it (0 = as if beside you, 1 = gone into the fog).
uniform float haze : hint_range(0.0, 1.0) = 0.35;
uniform vec3 haze_color : source_color = vec3(0.18, 0.17, 0.21);
// Contrast left in the shape (0 = a flat cut-out shadow, 1 = fully shaded).
uniform float contrast : hint_range(0.0, 1.0) = 1.0;

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
	vec3 base = mix(albedo, face_tint, COLOR.r) * COLOR.g;
	ALBEDO = mix(base, haze_color, haze);
	// A shadow in the murk: most of the lighting is flattened away, so it reads as a shape, not a model.
	ALBEDO = mix(haze_color * 0.9, ALBEDO, contrast);
	float wet = wetness * COLOR.b * (1.0 - haze);
	ROUGHNESS = mix(1.0, 0.32, wet);
	SPECULAR = 0.5 * wet;
	float eye = 1.0 - COLOR.a;
	EMISSION = eye_color * eye_glow * eye * (1.0 - haze) + haze_color * haze * (0.35 + 0.35 * contrast);
}
