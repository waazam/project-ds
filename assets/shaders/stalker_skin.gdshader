// The stalker's hide and rags: true darkness. Unlit (Dan, 2026-09-22: it must not
// reflect light): no lamp, lantern or moon puts a highlight on it, it is a hole in
// the scene the shape of a thing, fogged like everything else so it still sinks
// into the distance. A variant of entity_dither.gdshader's PS2 screen-door dissolve
// (`visibility` below 1 punches out a growing share of pixels in a 4x4 ordered
// pattern). Two-sided so the hanging rag strips can be single sheets.
//
// Vertex colour carries per-part tone (set by StalkerBody):
//   r = paleness (0 = body, higher = the faint face plane: the one thing that reads)
//   g = brightness multiplier (low in the eye sockets and rag undersides)
//   b = wetness (unused now: nothing on it catches light)
//   a = 0 marks the eyes: a faint, sickly yellow glow (fogged like everything else)
//
// The remodel (2026-10-02, tools/Blender/stalker.py): its baked maps (the hide's albedo and its micro-detail's
// normal map) come in as detail_albedo / detail_normal. Far off nothing changes: a hole in the scene. Up close (the
// bunker's hallway, the shove in Act 11) a very faint cold edge light traces its outline, its ribs and knuckles and
// the grain of its hide (edge_*: gone by edge_far), so the model can be seen for what it is without it ever looking
// lit. eyeshine: its eyes catch the lantern like an animal's (set by StalkerBody from the lantern and the head's
// facing; brightest in the middle of each eye, as a glint).
shader_type spatial;
render_mode cull_disabled, unshaded;

uniform vec3 albedo : source_color = vec3(0.0, 0.0, 0.0);
uniform vec3 face_tint : source_color = vec3(0.05, 0.052, 0.055);
uniform float visibility : hint_range(0.0, 1.0) = 1.0;
// the cold rim (2026-10-04: its outline standing out of the dark; see rim_fresnel.gdshader), faded close up and far off
uniform float rim = 0.0;   // (the stalker is true darkness, a hole in the scene: no rim; the giant sets its own)
uniform vec3 rim_color : source_color = vec3(0.55, 0.62, 0.75);
uniform float rim_far = 40.0;
float rim_term(vec3 n, vec3 v, vec3 vtx) {
	float f = pow(1.0 - clamp(abs(dot(normalize(n), normalize(v))), 0.0, 1.0), 3.0);
	float d = length(vtx);
	return f * rim * smoothstep(0.6, 2.4, d) * (1.0 - smoothstep(rim_far * 0.5, rim_far, d));
}
uniform float wetness : hint_range(0.0, 1.0) = 0.0;
uniform vec3 eye_color : source_color = vec3(1.0, 0.78, 0.22);
// Kept low on purpose: at 15-30 m in fog the eyes should be barely there.
uniform float eye_glow : hint_range(0.0, 8.0) = 0.3;
uniform float eyeshine : hint_range(0.0, 4.0) = 0.0;
uniform sampler2D detail_albedo : source_color, hint_default_black, filter_linear_mipmap;
uniform sampler2D detail_normal : hint_normal, filter_linear_mipmap;
uniform bool use_maps = false;
uniform vec3 edge_color : source_color = vec3(0.32, 0.36, 0.42);
uniform float edge_light : hint_range(0.0, 1.0) = 0.15;
uniform float edge_near = 2.5;
uniform float edge_far = 6.5;

const float BAYER[16] = float[16](
	0.0, 8.0, 2.0, 10.0,
	12.0, 4.0, 14.0, 6.0,
	3.0, 11.0, 1.0, 9.0,
	15.0, 7.0, 13.0, 5.0);

void fragment() {
	ivec2 p = ivec2(mod(FRAGCOORD.xy, 4.0));
	float threshold = (BAYER[p.y * 4 + p.x] + 0.5) / 16.0;
	if (visibility < threshold) discard;
	// Unshaded: what is written here is what is drawn (then fogged). Black, a breath of the
	// face plane, and the eyes' own glow.
	float eye = 1.0 - COLOR.a;
	vec3 n = normalize(NORMAL);
	if (!FRONT_FACING) n = -n;
	float facing = clamp(dot(n, VIEW), 0.0, 1.0);
	vec3 col = mix(albedo, face_tint, COLOR.r) * COLOR.g;
	if (use_maps) {
		float near = 1.0 - smoothstep(edge_near, edge_far, length(VERTEX));
		if (near > 0.0) {
			vec3 nm = texture(detail_normal, UV).xyz * 2.0 - 1.0;
			vec3 nd = normalize(TANGENT * nm.x + BINORMAL * nm.y + n * nm.z);
			float fres = pow(1.0 - clamp(dot(nd, VIEW), 0.0, 1.0), 3.0);
			// the hide's own tone (its maps are nearly black: brought up to a 0..2 weight) shapes the edge
			float tone = clamp(dot(texture(detail_albedo, UV).rgb, vec3(0.33)) * 14.0, 0.4, 2.0);
			col += edge_color * edge_light * near * fres * tone * (1.0 - eye);
		}
	}
	ALBEDO = col + eye_color * (eye_glow + eyeshine * pow(facing, 3.0)) * eye;
	ALBEDO += rim_color * rim_term(NORMAL, VIEW, VERTEX);
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
}
