// The giant's hide (Act 7's crossing beyond the burning cabin, Act 11's top of the stairs): stalker_skin.gdshader
// with the fog switched off and a `haze` blend in its place. The Act 7 giant walks 150 m off behind the
// far tree line, where the scene's fog would swallow it entirely; drawn fog-free it stays a dark shape
// over the trees, `haze` lifts it toward the fog colour by hand and `contrast` flattens the shading so it
// looms as a shadow in the murk rather than a lit model. Everything else (dither, face plane, eyes) is the same.
shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_schlick_ggx, fog_disabled;

// (2026-10-13i, the owner: "too smooth") the skin's detail (creature_detail.gdshaderinc) and the model's baked maps
// (StalkerBody hands them over as it does to stalker_skin)
#include "res://assets/shaders/creature_detail.gdshaderinc"
uniform sampler2D detail_albedo : source_color, hint_default_black, filter_linear_mipmap;
uniform sampler2D detail_normal : hint_normal, filter_linear_mipmap;
uniform bool use_maps = false;

// (2026-10-15, "blend more with the background") in the scene's air: its edges go over to the haze (and thin out in a
// dither at the very rim) so no hard cut-out line meets the sky; it lifts a little toward the air with height (aerial
// perspective); the eyes keep most of their glow through the haze, the one crisp thing on it. GiantStalkerEvent.Air.
uniform float edge_soft : hint_range(0.0, 1.0) = 0.0;
uniform float height_lift : hint_range(0.0, 1.0) = 0.0;
uniform float eye_keep : hint_range(0.0, 1.0) = 0.0;
uniform float base_y = 0.0;
uniform float top_y = 50.0;
varying float world_y;

void vertex() {
	CD_VERTEX;
	world_y = (MODEL_MATRIX * vec4(VERTEX, 1.0)).y;
}

uniform vec3 albedo : source_color = vec3(0.026, 0.028, 0.034);
uniform vec3 face_tint : source_color = vec3(0.21, 0.215, 0.22);
uniform float visibility : hint_range(0.0, 1.0) = 1.0;
// the cold rim (2026-10-04: its outline standing out of the dark; see rim_fresnel.gdshader), faded close up and far off
uniform float rim = 0.09;
uniform vec3 rim_color : source_color = vec3(0.55, 0.62, 0.75);
uniform float rim_far = 40.0;
float rim_term(vec3 n, vec3 v, vec3 vtx) {
	float f = pow(1.0 - clamp(abs(dot(normalize(n), normalize(v))), 0.0, 1.0), 3.0);
	float d = length(vtx);
	return f * rim * smoothstep(0.6, 2.4, d) * (1.0 - smoothstep(rim_far * 0.5, rim_far, d));
}
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
	float graze = 1.0 - clamp(abs(dot(normalize(NORMAL), normalize(VIEW))), 0.0, 1.0);
	// the very rim thinned to a dither: the silhouette frays into the air instead of cutting it
	if (pow(graze, 4.0) * edge_soft * 0.9 > threshold) discard;
	if (!FRONT_FACING) NORMAL = -NORMAL;
	vec3 base = mix(albedo, face_tint, COLOR.r) * COLOR.g;
	// (2026-10-13i) its hide's detail: the skin stretched paler over its bones, black down in the creases between them,
	// the relief of its wrinkles and cracked skin in the light that grazes it (fading as the haze takes it)
	CdSample d = cd_sample();
	float occ = d.occ * cd_crease_occ(d);
	float ridge = smoothstep(0.1, 0.9, -d.curv);
	base *= (0.55 + 0.45 * occ) * (1.0 + 0.9 * ridge);
	float hgt = clamp((world_y - base_y) / max(top_y - base_y, 1.0), 0.0, 1.0);
	float hz = clamp(haze + (1.0 - haze) * height_lift * hgt + (1.0 - haze) * edge_soft * pow(graze, 1.5), 0.0, 1.0);
	ALBEDO = mix(base, haze_color, hz);
	// A shadow in the murk: most of the lighting is flattened away, so it reads as a shape, not a model.
	ALBEDO = mix(haze_color * 0.9, ALBEDO, contrast);
	if (use_maps && FRONT_FACING) {
		vec3 bk = cd_unpack(texture(detail_normal, UV).xy);
		NORMAL_MAP = cd_pack(cd_blend(bk, d.ts));
	} else if (FRONT_FACING) {
		NORMAL_MAP = cd_pack(d.ts);
	}
	NORMAL_MAP_DEPTH = 1.0 - haze * 0.7;
	AO = occ;
	float wet = (wetness * COLOR.b + d.wet * 0.35 + (1.0 - occ) * 0.3) * (1.0 - haze);
	// (a dull sheen over all of it, so its wrinkles and cracks catch the light that grazes them; wetter in the creases)
	ROUGHNESS = mix(0.78, 0.32, clamp(wet, 0.0, 1.0));
	SPECULAR = (0.22 + 0.3 * clamp(wet, 0.0, 1.0)) * (1.0 - haze);
	float eye = 1.0 - COLOR.a;
	EMISSION = eye_color * eye_glow * eye * (1.0 - hz * (1.0 - eye_keep)) + haze_color * hz * (0.35 + 0.35 * contrast);
	EMISSION += rim_color * rim_term(NORMAL, VIEW, VERTEX);
}
