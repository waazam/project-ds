// The creatures' surface detail (2026-10-13i, the owner: "the monster models look too smooth, add more detail").
// Shared by every creature hide (crawler, wendigo and its arm and head, the stalker and the giants): the skin's detail
// maps (tools/Textures/make_creature_detail.py: folds, crossing wrinkles, cracked dry patches, veins, scars, pores)
// projected three ways in the body's rest pose (CreatureDetail.cs: CUSTOM0 rest position + curvature, CUSTOM1 rest
// normal, CUSTOM2 rest tangent), at two sizes (the fine skin and, three times larger, its folds), so it rides the flesh
// as it moves; and the mesh's own creases darkened from its curvature (between the ribs, round the knuckles).
//
// In vertex(): CD_VERTEX; (or, for a mesh without the channels, CD_VERTEX_OBJECT;)
// In fragment(): CdSample d = cd_sample(); then d.ts (a tangent-space normal for NORMAL_MAP, the detail alone),
// d.occ (1 open .. 0 deep), d.vein, d.wet, d.curv (+ crease, - ridge); cd_blend(baked_ts, d.ts) lays it over a baked map.

uniform sampler2D cd_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D cd_mask : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
/// Tiles of the fine detail per unit of the rest pose (the folds' layer is a third of it).
uniform float cd_scale = 3.5;
/// How deep the relief: the fine layer and the folds.
uniform float cd_strength = 0.8;
uniform float cd_fold_strength = 0.7;
/// How dark the mesh's own creases go (its curvature), and the detail's own (its cracks and pores).
uniform float cd_crease = 0.55;
uniform float cd_occlusion = 0.6;
/// Off: the detail is left out (a far-off or flat use).
uniform float cd_amount : hint_range(0.0, 1.0) = 1.0;

varying vec3 cd_p;
varying vec3 cd_n;
varying vec4 cd_t;
varying float cd_k;

/// True when the mesh carries its rest pose (CreatureDetail.Prepare; set by CreatureDetail.Hook); else the posed
/// VERTEX stands in (a mesh built in code: the detail swims a little as it bends, but nothing breaks).
uniform bool cd_rest = false;

#define CD_VERTEX_OBJECT cd_p = VERTEX; cd_k = 0.0; cd_n = NORMAL; cd_t = vec4(TANGENT, 1.0)
#define CD_VERTEX bool cd_r = cd_rest && dot(CUSTOM1.xyz, CUSTOM1.xyz) > 0.01; cd_p = cd_r ? CUSTOM0.xyz : VERTEX; cd_k = cd_r ? CUSTOM0.w : 0.0; cd_n = cd_r ? normalize(CUSTOM1.xyz) : NORMAL; cd_t = cd_r ? CUSTOM2 : vec4(TANGENT, 1.0)

struct CdSample {
	vec3 ts;
	float occ;
	float vein;
	float wet;
	float curv;
};

/// A normal map's texel to a unit normal (its z rebuilt: an imported normal map keeps only x and y).
vec3 cd_unpack(vec2 rg) {
	vec2 xy = rg * 2.0 - 1.0;
	return vec3(xy, sqrt(max(0.0, 1.0 - dot(xy, xy))));
}

// whiteout-blended triplanar normal (Ben Golus' method), in the rest pose's space
vec3 cd_tri(vec3 p, vec3 n, vec3 w, float s, float strength, vec2 off) {
	vec3 tx = cd_unpack(texture(cd_normal, p.zy * s + off).xy);
	vec3 ty = cd_unpack(texture(cd_normal, p.xz * s + off.yx).xy);
	vec3 tz = cd_unpack(texture(cd_normal, p.xy * s - off).xy);
	tx.xy *= strength; ty.xy *= strength; tz.xy *= strength;
	tx = vec3(tx.xy + n.zy, abs(tx.z) * n.x);
	ty = vec3(ty.xy + n.xz, abs(ty.z) * n.y);
	tz = vec3(tz.xy + n.xy, abs(tz.z) * n.z);
	return normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
}

vec3 cd_trimask(vec3 p, vec3 w, float s, vec2 off) {
	return texture(cd_mask, p.zy * s + off).rgb * w.x + texture(cd_mask, p.xz * s + off.yx).rgb * w.y + texture(cd_mask, p.xy * s - off).rgb * w.z;
}

CdSample cd_sample() {
	CdSample r;
	vec3 n = normalize(cd_n);
	vec3 w = pow(abs(n), vec3(4.0));
	w /= (w.x + w.y + w.z);
	float s = cd_scale;
	// skin drawn tight over a ridge (a bone pushing through: a knuckle, a rib, the spine) wrinkles less; it bunches in
	// the creases between
	float tight = mix(0.45, 1.25, smoothstep(-0.7, 0.6, cd_k));
	// the fine skin and its folds, three times larger and turned (so the two never line up)
	vec3 fine = cd_tri(cd_p, n, w, s, cd_strength * cd_amount * tight, vec2(0.0));
	vec3 fold = cd_tri(cd_p.zxy, n.zxy, w.zxy, s * 0.31, cd_fold_strength * cd_amount, vec2(0.37, 0.61)).yzx;
	// the two together: their tilts away from the plain normal added
	vec3 nr = normalize(n + (fine - n) + (fold - n));
	// into the mesh's own tangent frame (the posed one carries it: NORMAL_MAP)
	vec3 t = cd_t.xyz - n * dot(cd_t.xyz, n);
	t = dot(t, t) > 1e-6 ? normalize(t) : normalize(cross(n, abs(n.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0)));
	vec3 b = cross(n, t) * (cd_t.w < 0.0 ? -1.0 : 1.0);
	r.ts = normalize(vec3(dot(nr, t), dot(nr, b), dot(nr, n)));
	vec3 m = cd_trimask(cd_p, w, s, vec2(0.0));
	vec3 mf = cd_trimask(cd_p.zxy, w.zxy, s * 0.31, vec2(0.37, 0.61));
	r.occ = mix(1.0, m.r * mix(1.0, mf.r, 0.6), cd_occlusion * cd_amount);
	r.vein = max(m.g, mf.g * 0.8) * cd_amount;
	r.wet = mix(m.b, mf.b, 0.5);
	r.curv = cd_k;
	return r;
}

/// The masks alone (no normal: for a hide that bumps its own normal): occ, vein, wet; ts left flat.
CdSample cd_sample_masks() {
	CdSample r;
	vec3 n = normalize(cd_n);
	vec3 w = pow(abs(n), vec3(4.0));
	w /= (w.x + w.y + w.z);
	vec3 m = cd_trimask(cd_p, w, cd_scale, vec2(0.0));
	vec3 mf = cd_trimask(cd_p.zxy, w.zxy, cd_scale * 0.31, vec2(0.37, 0.61));
	r.ts = vec3(0.0, 0.0, 1.0);
	r.occ = mix(1.0, m.r * mix(1.0, mf.r, 0.6), cd_occlusion * cd_amount);
	r.vein = max(m.g, mf.g * 0.8) * cd_amount;
	r.wet = mix(m.b, mf.b, 0.5);
	r.curv = cd_k;
	return r;
}

/// The mesh's creases as an occlusion (1 open .. dark in a crease), from its curvature.
float cd_crease_occ(CdSample d) {
	return 1.0 - smoothstep(0.05, 1.0, d.curv) * cd_crease * cd_amount;
}

/// A tangent-space detail laid over a baked tangent-space normal (whiteout: the detail tilts the baked one).
vec3 cd_blend(vec3 base_ts, vec3 det_ts) {
	return normalize(vec3(base_ts.xy + det_ts.xy, base_ts.z * det_ts.z));
}

// the lantern's blacklight (Lantern.PublishUv), as Lantern.UvOn has it: in its cone, in its reach
global uniform vec3 uv_light_pos;
global uniform vec3 uv_light_dir;
global uniform float uv_light_on;
global uniform float uv_light_range;

float cd_uv(vec3 world) {
	if (uv_light_on < 0.5) return 0.0;
	vec3 to = world - uv_light_pos;
	float d = length(to);
	float cosOuter = cos(radians(24.0));
	float cone = smoothstep(cosOuter, mix(cosOuter, 1.0, 0.7), dot(to / max(d, 1e-3), uv_light_dir));
	return cone * (1.0 - smoothstep(uv_light_range * 0.45, uv_light_range, d));
}

/// As NORMAL_MAP wants it (0..1).
vec3 cd_pack(vec3 ts) {
	return ts * 0.5 + 0.5;
}
