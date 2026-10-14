// The wendigo's hide (2026-10-04, the owner: "figuring out incredible shaders for our monsters (bosses) so they look
// imposing"; reskinned 2026-10-10c, the owner: "we really need to reskin the wendigo model it is just so gray and bland right
// now ... it needs to be very high res for up close encounters, it needs to be horrific and intimating").
//
// Its baked maps (tools/Blender/wendigo.py) are thousands of small islands on one sheet: up close there was next to
// nothing in them. Now its skin is drawn here, at any distance as fine as the screen, in the body's rest pose (CUSTOM0
// position, CUSTOM1 normal: Wendigo.RestSpace), so it stays on the flesh as it moves:
// - a starved corpse's hide: grey-blue, gone sallow and yellow in patches, bruised purple in the hollows;
// - the veins standing up under it, dark and branching; creases and splits across it, the skin cracked like old leather;
// - raw places: a few wounds, wet and red at the middle, black at their edges;
// - old blood run down from its mouth over its chest and down its arms, dried brown-black, still wet in streaks;
// - frostbite: its hands and feet gone black and wet, creeping up the limbs; hoarfrost on everything facing up;
// - all of it raised and sunk into the light (its bumps from the same fields), a cold wet glint, and the hard light.
// Nothing flashes; nothing glows but what already did.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

// (2026-10-13i, the owner: "the monster models look too smooth") the skin's detail maps and the mesh's own creases
// (CreatureDetail.cs, creature_detail.gdshaderinc): wrinkles bunched in the creases and drawn tight over the bones,
// cracked dry patches, raised veins, scars and pores, at the screen's own fineness; grime and shadow in every crease.
#include "res://assets/shaders/creature_detail.gdshaderinc"

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform float roughness = 0.72;
uniform float frost = 0.18;
uniform vec3 frost_color : source_color = vec3(0.7, 0.75, 0.82);
uniform float bite_low = 0.35;
uniform float bite_high = 1.15;
uniform vec3 bite_color : source_color = vec3(0.03, 0.025, 0.035);
uniform float hard_light = 0.75;
uniform float glint = 0.3;
uniform float char_amount = 0.0;
/// The rest pose's units per metre (the model's own scale), and whether CUSTOM0/1 carry it (else the posed VERTEX).
uniform float detail_scale = 1.0;
uniform bool rest_space = true;
uniform float bump = 1.0;
/// How much of the baked map is kept under the drawn hide (its shapes: the bone, the hair).
uniform float baked = 0.35;
uniform float blood = 0.85;
/// Its palette: the hide's dead grey, its sallow patches, its bruising (bone: ivory, yellowed, stained)
uniform vec3 base_a : source_color = vec3(0.36, 0.35, 0.34);
uniform vec3 base_b : source_color = vec3(0.44, 0.37, 0.24);
uniform vec3 base_c : source_color = vec3(0.3, 0.17, 0.21);
uniform float vein_amt = 1.0;
uniform float crack_amt = 1.0;
/// Where its mouth is (rest-pose height, model units): the blood runs down from round there.
uniform float mouth_y = 3.5;
/// A faint pallor of its own (the bone's: so the skull shows in the murk), as emission.
uniform float pallor = 0.0;

varying vec3 v_p;
varying vec3 v_n;
varying vec3 v_world_n;

float hash3(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float vnoise(vec3 x) {
	vec3 i = floor(x), f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash3(i), hash3(i + vec3(1, 0, 0)), f.x), mix(hash3(i + vec3(0, 1, 0)), hash3(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash3(i + vec3(0, 0, 1)), hash3(i + vec3(1, 0, 1)), f.x), mix(hash3(i + vec3(0, 1, 1)), hash3(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}
float fbm(vec3 p) { float s = 0.0, a = 0.5; for (int i = 0; i < 4; i++) { s += a * vnoise(p); p = p * 2.07 + 17.1; a *= 0.5; } return s; }
// ridged: thin raised lines (veins), branching
float ridged(vec3 p) { float s = 0.0, a = 0.55; for (int i = 0; i < 3; i++) { float n = 1.0 - abs(vnoise(p) * 2.0 - 1.0); s += a * n * n * n * n; p = p * 2.2 + 5.3; a *= 0.5; } return s; }
// cells: the distance to the nearest of a few jittered points per cell, and to the second (their difference: cracks)
vec2 cells(vec3 p) {
	vec3 i = floor(p), f = fract(p);
	float d1 = 8.0, d2 = 8.0;
	for (int z = -1; z <= 1; z++) for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++) {
		vec3 o = vec3(float(x), float(y), float(z));
		vec3 r = o + vec3(hash3(i + o), hash3(i + o + 3.1), hash3(i + o + 7.7)) - f;
		float d = dot(r, r);
		if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) { d2 = d; }
	}
	return vec2(sqrt(d1), sqrt(d2));
}

void vertex() {
	v_p = (rest_space ? CUSTOM0.xyz : VERTEX) * detail_scale;
	CD_VERTEX;
	v_n = rest_space ? normalize(CUSTOM1.xyz) : NORMAL;
	v_world_n = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

// the fields, from the rest-pose position: what's raised (veins, the ridges between creases) and what's sunk (creases, cracks)
float relief(vec3 p) {
	float veins = smoothstep(0.55, 0.95, ridged(p * 3.4 + vec3(0.0, p.y * 0.6, 0.0))) * vein_amt;
	vec2 c = cells(p * 24.0);
	float dry = smoothstep(0.5, 0.7, fbm(p * 2.1 + 5.0));
	float crack = (1.0 - smoothstep(0.0, 0.035, c.y - c.x)) * dry * crack_amt;
	float crease = 1.0 - smoothstep(0.0, 0.1, abs(vnoise(p * vec3(5.0, 22.0, 5.0)) - 0.5));
	float pores = vnoise(p * 90.0);
	return veins * 0.6 - crack * 0.45 - crease * 0.3 + pores * 0.1 + fbm(p * 6.0) * 0.3;
}

void fragment() {
	vec3 p = v_p;
	vec3 bakedCol = texture(albedo_tex, UV).rgb;
	float n = fbm(p * 1.3);
	float n2 = fbm(p * 4.0 + 3.7);
	// the hide: corpse grey-blue, sallow in patches, bruised in the hollows
	vec3 col = mix(base_a, base_b, smoothstep(0.42, 0.72, n) * 0.85);
	col = mix(col, base_c, smoothstep(0.52, 0.78, n2) * 0.7);
	col *= 0.75 + 0.4 * vnoise(p * 18.0);
	// darker in its hollows and creases (the grime in them)
	float creaseC = 1.0 - smoothstep(0.0, 0.1, abs(vnoise(p * vec3(5.0, 22.0, 5.0)) - 0.5));
	col *= 1.0 - creaseC * 0.35;
	// its veins, dark blue-black under the skin; the cracks dark; the creases shadowed
	float veins = smoothstep(0.55, 0.95, ridged(p * 3.4 + vec3(0.0, p.y * 0.6, 0.0))) * vein_amt;
	col = mix(col, vec3(0.1, 0.08, 0.16), veins * 0.6);
	vec2 c = cells(p * 24.0);
	float dry = smoothstep(0.5, 0.7, fbm(p * 2.1 + 5.0));
	float crack = (1.0 - smoothstep(0.0, 0.035, c.y - c.x)) * dry * crack_amt;
	col = mix(col, vec3(0.05, 0.03, 0.03), crack * 0.65);
	// the baked shapes kept faintly underneath (the bones' pale, the hair's dark)
	float bl = dot(bakedCol, vec3(0.3, 0.59, 0.11));
	col = mix(col, col * (0.8 + 0.6 * bl), baked);
	// wounds: rare raw places, wet red in the middle, black at the edge
	float wound = smoothstep(0.7, 0.8, fbm(p * 2.3 + 11.0));
	float woundCore = smoothstep(0.76, 0.86, fbm(p * 2.3 + 11.0));
	col = mix(col, vec3(0.06, 0.02, 0.02), wound * 0.8);
	col = mix(col, vec3(0.42, 0.04, 0.035), woundCore);
	// old blood run down from the mouth and chest, in long streaks, more higher up (the mouth's about 4 m up the model)
	float drip = smoothstep(0.62, 0.8, vnoise(vec3(p.x * 9.0, p.y * 0.8, p.z * 9.0)) * (0.6 + 0.4 * fbm(p * vec3(6.0, 1.2, 6.0))));
	float high = smoothstep(mouth_y - 2.0, mouth_y - 0.2, p.y) * (1.0 - smoothstep(mouth_y + 0.3, mouth_y + 0.6, p.y));
	float smear = smoothstep(0.55, 0.75, fbm(p * vec3(3.0, 0.9, 3.0) + 2.0));   // (dried smears over the rest of it)
	float bloodAmt = clamp(drip * (0.25 + 0.75 * high) + smear * 0.35, 0.0, 1.0) * blood;
	float wet = smoothstep(0.75, 0.95, vnoise(p * vec3(14.0, 3.0, 14.0)));
	col = mix(col, mix(vec3(0.12, 0.03, 0.02), vec3(0.3, 0.02, 0.02), wet), bloodAmt);
	// frostbite: black, glossy, ragged at its edge, from the hands and feet up
	float bite = 1.0 - smoothstep(bite_low, bite_high, p.y + (n - 0.5) * 0.6);
	col = mix(col, bite_color, bite * 0.9);
	// hoarfrost on what faces up (the posed world: snow settles on the shoulders as it stands)
	float up = clamp(v_world_n.y, 0.0, 1.0);
	float crust = smoothstep(0.4, 0.8, up + (n2 - 0.5) * 0.45) * frost * (1.0 - bite * 0.7) * (1.0 - bloodAmt * 0.6);
	col = mix(col, frost_color * (0.85 + 0.25 * n2), crust * 0.7);
	// burned (Act 24)
	float ch = smoothstep(1.0 - char_amount, 1.0 - char_amount + 0.25, n * 0.6 + n2 * 0.4 + char_amount * 0.3) * step(0.001, char_amount);
	col = mix(col, vec3(0.025, 0.02, 0.018), ch);
	EMISSION = vec3(0.9, 0.25, 0.05) * smoothstep(0.75, 0.95, n2) * ch * 0.25 + col * pallor;
	// (2026-10-13i) the skin's detail: the veins blue-black under it, grime and shadow down in every crease and crack
	CdSample d = cd_sample();
	float occ = d.occ * cd_crease_occ(d);
	col = mix(col, vec3(0.1, 0.08, 0.16), d.vein * vein_amt * 0.45 * (1.0 - bite) * (1.0 - crust));
	col = mix(col * vec3(0.32, 0.27, 0.24), col, occ);
	ALBEDO = col;
	AO = occ;
	AO_LIGHT_AFFECT = 0.5;
	vec2 bk = texture(normal_tex, UV).xy * 2.0 - 1.0;
	vec3 baked_ts = normalize(vec3(bk * 0.8, sqrt(max(0.0, 1.0 - dot(bk, bk)))));
	NORMAL_MAP = cd_pack(cd_blend(baked_ts, d.ts));
	NORMAL_MAP_DEPTH = 1.0;
	// its relief: the normal pushed by the fields' slope across the screen (the rest pose's fields, so it rides the skin)
	float h = relief(p) * bump * 0.012;
	vec3 dpx = dFdx(VERTEX), dpy = dFdy(VERTEX);
	vec3 r1v = cross(dpy, NORMAL), r2v = cross(NORMAL, dpx);
	float det = dot(dpx, r1v);
	vec3 sg = sign(det) * (dFdx(h) * r1v + dFdy(h) * r2v);
	NORMAL = normalize(abs(det) * NORMAL - sg);
	float wetness = max(max(smoothstep(0.5, 1.0, bite) * 0.8, woundCore), bloodAmt * wet);
	// (2026-10-13i) dry and dull on the open skin, slick down in the creases and the wet patches (it read as plastic)
	wetness = max(wetness, max(d.wet * 0.45, (1.0 - occ) * 0.5) * (1.0 - crust));
	ROUGHNESS = mix(mix(max(roughness, 0.8), 0.3, wetness), 0.95, crust);
	SPECULAR = mix(0.16, 0.5, wetness);
}

void light() {
	float nl = dot(NORMAL, LIGHT);
	float soft = clamp(nl, 0.0, 1.0);
	float hard = smoothstep(-0.04, 0.22, nl) * (0.55 + 0.45 * soft);
	DIFFUSE_LIGHT += mix(soft, hard, hard_light) * ATTENUATION * LIGHT_COLOR / PI;
	vec3 h = normalize(VIEW + LIGHT);
	float sp = pow(clamp(dot(NORMAL, h), 0.0, 1.0), mix(18.0, 72.0, 1.0 - ROUGHNESS)) * (1.0 - ROUGHNESS) * glint;
	SPECULAR_LIGHT += sp * ATTENUATION * LIGHT_COLOR * step(0.0, nl);
}
