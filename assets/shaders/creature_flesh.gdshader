// A creature's starved, dead-pale flesh (2026-10-13i, the owner: "the monster models look too smooth, add more detail"):
// the crawler's hide and the wendigo's arm through the crawlspace wall. Over the model's baked maps:
// - the skin's detail (creature_detail.gdshaderinc): folds and wrinkles, cracked dry patches, raised veins, welted
//   scars, pores, riding the flesh in its rest pose; the mesh's own creases (between the ribs, round the knuckles and
//   joints) darkened, grime settled in them;
// - its tones: a corpse's pallor, mottled and bruised in patches, the veins blue-black under the thin skin, sores, old
//   blood (none by default), frostbite creeping up from one end (the arm's hand);
// - wet and dry: slick in the creases and the wet patches, dull and rough on the dry, cracked skin;
// - light through the skin: a wrapped, reddened terminator where the light grazes (thin flesh over bone), so it reads
//   as flesh, not plastic; a cold rim;
// - the blacklight: the hide glows a cold violet-white where the lantern's violet cone falls (as the crawler's flat glow
//   did), but the veins and old fluids glow brighter and the creases stay dark, so the detail still reads by it.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

#include "res://assets/shaders/creature_detail.gdshaderinc"

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap, repeat_enable;
uniform bool use_baked = true;
/// How much of the baked colour is kept under the drawn tones (its shapes: the bones' pale, the hollows' dark).
uniform float baked = 0.45;
uniform float baked_normal = 0.8;
uniform vec3 tint : source_color = vec3(1.0, 1.0, 1.0);
/// Its palette: pallor, the sallow patches, the bruising
uniform vec3 base_a : source_color = vec3(0.58, 0.56, 0.52);
uniform vec3 base_b : source_color = vec3(0.52, 0.5, 0.4);
uniform vec3 base_c : source_color = vec3(0.36, 0.28, 0.32);
uniform vec3 vein_color : source_color = vec3(0.16, 0.16, 0.26);
uniform vec3 grime_color : source_color = vec3(0.12, 0.1, 0.08);
uniform float mottle = 1.0;
uniform float vein_amt = 0.8;
uniform float sores = 0.35;
uniform float blood = 0.0;
/// Frostbite from one end: where along `bite_axis` (rest pose) it starts black (bite_from) and where it's gone (bite_to).
uniform vec3 bite_axis = vec3(0.0, 1.0, 0.0);
uniform float bite_from = -100.0;
uniform float bite_to = -99.0;
uniform float roughness = 0.78;
uniform float wet_roughness = 0.42;
/// Light through the thin flesh (the terminator warmed and softened) and its colour.
uniform float scatter = 0.45;
uniform vec3 scatter_color : source_color = vec3(0.75, 0.25, 0.18);
uniform float rim = 0.22;
uniform vec3 rim_color : source_color = vec3(0.6, 0.65, 0.75);
/// The blacklight's answer (0: none).
uniform float uv_glow = 0.55;
uniform vec3 uv_color : source_color = vec3(0.55, 0.5, 0.75);

varying vec3 v_world;

float h3(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float vn(vec3 x) {
	vec3 i = floor(x), f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(h3(i), h3(i + vec3(1, 0, 0)), f.x), mix(h3(i + vec3(0, 1, 0)), h3(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(h3(i + vec3(0, 0, 1)), h3(i + vec3(1, 0, 1)), f.x), mix(h3(i + vec3(0, 1, 1)), h3(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}
float fb(vec3 p) { return vn(p) * 0.55 + vn(p * 2.07 + 7.1) * 0.3 + vn(p * 4.3 + 3.3) * 0.15; }

void vertex() {
	CD_VERTEX;
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	CdSample d = cd_sample();
	vec3 p = cd_p;
	vec3 bakedCol = use_baked ? texture(albedo_tex, UV).rgb : vec3(0.5);
	float n1 = fb(p * 2.2);
	float n2 = fb(p * 5.5 + 3.7);
	// the pallor, sallow and bruised in patches
	vec3 col = mix(base_a, base_b, smoothstep(0.4, 0.75, n1) * mottle);
	col = mix(col, base_c, smoothstep(0.55, 0.8, n2) * 0.75 * mottle);
	// the baked shapes kept underneath (their light and dark, not their colour)
	float bl = dot(bakedCol, vec3(0.3, 0.59, 0.11));
	col = mix(col, col * (0.55 + 0.9 * bl), baked);
	col *= tint;
	// the veins under the thin skin
	col = mix(col, vein_color, d.vein * vein_amt * 0.55);
	// sores: rare raw places, wet and dark red at the middle, black-edged
	float sore = smoothstep(0.72, 0.8, fb(p * 3.1 + 11.0)) * sores;
	float soreCore = smoothstep(0.78, 0.86, fb(p * 3.1 + 11.0)) * sores;
	col = mix(col, vec3(0.08, 0.04, 0.035), sore * 0.7);
	col = mix(col, vec3(0.32, 0.05, 0.04), soreCore);
	// old blood, in runs
	float run = smoothstep(0.6, 0.8, vn(vec3(p.x * 14.0, p.y * 1.5, p.z * 14.0)) * (0.5 + 0.5 * n2)) * blood;
	col = mix(col, vec3(0.2, 0.03, 0.02), run);
	// frostbite: black and glossy from one end, ragged at its edge
	float along = dot(p, normalize(bite_axis)) + (n1 - 0.5) * 0.12;
	float bite = 1.0 - smoothstep(bite_from, bite_to, along);
	col = mix(col, vec3(0.03, 0.025, 0.035), bite * 0.9);
	// grime and shadow in the creases: the mesh's own and the skin's
	float occ = d.occ * cd_crease_occ(d);
	col = mix(grime_color * col * 2.0, col, occ);
	ALBEDO = col;
	vec3 base_ts = use_baked ? cd_unpack(texture(normal_tex, UV).xy) : vec3(0.0, 0.0, 1.0);
	base_ts.xy *= baked_normal;
	NORMAL_MAP = cd_pack(cd_blend(normalize(base_ts), d.ts));
	// wet in the creases and the slick patches, dry and rough on the cracked skin
	float wet = clamp(max(d.wet * 0.8, (1.0 - occ) * 0.6) + soreCore + run * 0.6 + bite * 0.5, 0.0, 1.0);
	ROUGHNESS = mix(roughness, wet_roughness, wet);
	SPECULAR = mix(0.15, 0.45, wet);
	AO = occ;
	AO_LIGHT_AFFECT = 0.6;
	// the blacklight: the veins and the old fluids brightest, the creases dark
	float uv = cd_uv(v_world) * uv_glow;
	EMISSION = uv_color * uv * (0.45 + 0.9 * d.vein + 0.5 * d.wet) * mix(0.35, 1.0, occ);
}

void light() {
	float nl = dot(NORMAL, LIGHT);
	// light through thin flesh: the terminator wrapped round and warmed
	float wrap = clamp((nl + scatter) / (1.0 + scatter), 0.0, 1.0);
	float lam = clamp(nl, 0.0, 1.0);
	vec3 sc = mix(scatter_color, vec3(1.0), lam);
	DIFFUSE_LIGHT += mix(vec3(lam), sc * wrap, scatter) * ATTENUATION * LIGHT_COLOR / PI;
	vec3 h = normalize(VIEW + LIGHT);
	float gloss = mix(12.0, 90.0, 1.0 - ROUGHNESS);
	float sp = pow(clamp(dot(NORMAL, h), 0.0, 1.0), gloss) * (gloss + 2.0) / 8.0 * SPECULAR_AMOUNT * 0.12 * (1.0 - ROUGHNESS * 0.7);
	SPECULAR_LIGHT += sp * ATTENUATION * LIGHT_COLOR * step(0.0, nl);
	// a cold rim, where the light comes from behind it
	float fr = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);
	SPECULAR_LIGHT += rim * rim_color * fr * clamp(dot(LIGHT, -VIEW) * 0.5 + 0.5 + lam * 0.3, 0.0, 1.0) * ATTENUATION * LIGHT_COLOR * 0.3;
}
