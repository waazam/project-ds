// Packed snow (2026-10-13c, the look audit: Act 24's tunnel out of the lodge read as white plastic: the fresh snow's
// smooth, even shading over the vault's big flat facets, its sheen the same everywhere). The storm's snow packed hard
// and carved by the thing that dug through it:
// - texture: the maze's own baked trodden-snow set (its grain and its normal), world-projected three ways at two scales;
// - relief: scallops gouged out of it a hand to a forearm across (a ridged noise read as height, its slope turned into the
//   normal), so no facet is ever flat; the light catches their rims and the hollows hold the dark;
// - colour: bluish grey in its hollows and in the shadow, the cold light that gets into snow and comes back out blue,
//   paler on the crests, a dirtier grey in blotches where the storm's grit packed into it;
// - roughness: matte almost everywhere (no sheen on it at all), with streaks and patches of ice glaze where it thawed
//   and froze again: a little smoother and darker, blue, a dull glint, never a gloss;
// - a crystal here and there catching the eye as it moves (fewer than the fresh snow's: packed snow is duller).
// Everything world-projected (the CSG has no useful UVs).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

uniform sampler2D snow_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D snow_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;   // broad, smooth, seamless
uniform vec3 crest_color : source_color = vec3(0.8, 0.83, 0.88);
uniform vec3 hollow_color : source_color = vec3(0.42, 0.5, 0.63);
uniform vec3 grit_color : source_color = vec3(0.5, 0.5, 0.5);
uniform vec3 glaze_color : source_color = vec3(0.46, 0.55, 0.66);
uniform float grain_scale = 0.8;
uniform float scallop_scale = 0.3;     // the gouges: about 3 m of noise a tile, its ridges a forearm or so apart
uniform float scallop_depth = 0.75;
uniform float normal_strength = 0.75;
uniform float glisten = 0.25;

varying vec3 w_pos;
varying vec3 w_nrm;

void vertex() {
	w_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	w_nrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

float hash3(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }

// (2026-10-15, the scan: long horizontal smears down the vault's walls) The weights come from the surface's own facet (its
// world-space derivatives), not the interpolated normal: the CSG's smoothed normals swing well off the true face at its
// seams and ribs, and a projection laid edge-on to the face it lands on stretches into streaks. Sharper, too.
vec3 tri_w(vec3 n) { vec3 w = pow(abs(n), vec3(8.0)); return w / (w.x + w.y + w.z + 1e-4); }

// the scallops' height at a point of one projection: ridged noise (sharp rims where the gouges meet, round hollows)
float gouge(vec2 p) {
	float a = texture(noise_tex, p * scallop_scale).r;
	float b = texture(noise_tex, p * scallop_scale * 2.3 + vec2(0.37, 0.71)).r;
	float ra = 1.0 - abs(a * 2.0 - 1.0);
	float rb = 1.0 - abs(b * 2.0 - 1.0);
	return ra * 0.7 + rb * 0.3;
}

// its slope (per metre) in that projection
vec2 gouge_slope(vec2 p, out float h) {
	const float e = 0.02;
	h = gouge(p);
	return vec2(gouge(p + vec2(e, 0.0)) - h, gouge(p + vec2(0.0, e)) - h) / e;
}

// a projection's tangent-space normal: the snow's grain (two scales) and the gouges' slope
vec3 tnormal(vec2 p, out float h) {
	vec3 g1 = texture(snow_normal, p * grain_scale).xyz * 2.0 - 1.0;
	vec3 g2 = texture(snow_normal, p * grain_scale * 0.31 + vec2(0.5)).xyz * 2.0 - 1.0;
	vec2 s = gouge_slope(p, h);
	vec2 xy = (g1.xy * 0.45 + g2.xy * 0.6) * normal_strength - s * scallop_depth * 0.15;
	return vec3(xy, 1.0);
}

void fragment() {
	vec3 gn = cross(dFdx(w_pos), dFdy(w_pos));
	gn = dot(gn, gn) > 1e-12 ? normalize(gn) : w_nrm;
	if (dot(gn, w_nrm) < 0.0) gn = -gn;
	vec3 w = tri_w(gn);
	vec3 n = w_nrm;
	float hx, hy, hz;
	vec3 nx = tnormal(w_pos.zy, hx);
	vec3 ny = tnormal(w_pos.xz, hy);
	vec3 nz = tnormal(w_pos.xy, hz);
	// (blended "whiteout", as the ice cave does)
	vec3 tx = vec3(nx.xy + n.zy, abs(nx.z) * n.x);
	vec3 ty = vec3(ny.xy + n.xz, abs(ny.z) * n.y);
	vec3 tz = vec3(nz.xy + n.xy, abs(nz.z) * n.z);
	vec3 wn = normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
	NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
	float h = hx * w.x + hy * w.y + hz * w.z;   // 1 on a gouge's rim, 0 deep in a hollow

	// the snow's own grain, its brightness only (the colour is the light's and the hollows')
	vec3 grain = texture(snow_albedo, w_pos.zy * grain_scale).rgb * w.x + texture(snow_albedo, w_pos.xz * grain_scale).rgb * w.y
		+ texture(snow_albedo, w_pos.xy * grain_scale).rgb * w.z;
	float lum = dot(grain, vec3(0.3, 0.5, 0.2));
	float broad = texture(noise_tex, w_pos.xz * 0.11 + w_pos.y * 0.07).r;
	vec3 col = mix(hollow_color, crest_color, smoothstep(0.15, 0.85, h));
	col *= 0.86 + 0.26 * lum;
	col *= 0.86 + 0.22 * broad;
	// grey grit packed in low down (boots, the storm's dirt), blotchy
	float grit = smoothstep(0.45, 0.75, texture(noise_tex, w_pos.xz * 0.6 + vec2(w_pos.y * 0.3)).r);
	col = mix(col, col * grit_color * 1.4, grit * 0.35);
	// the glaze: streaks down the walls (stretched along y) and patches on the roof, where it thawed and froze
	// (per projection: one diagonal x + z sample went flat along any wall running across that diagonal, and the glaze there
	// came out as bands the whole length of the wall)
	float streak = texture(noise_tex, vec2(w_pos.z * 0.45, w_pos.y * 0.2)).r * w.x + texture(noise_tex, w_pos.xz * 0.3 + vec2(0.13, 0.58)).r * w.y
		+ texture(noise_tex, vec2(w_pos.x * 0.45, w_pos.y * 0.2) + vec2(0.61, 0.27)).r * w.z;
	float glaze = smoothstep(0.62, 0.74, streak * 0.75 + broad * 0.35) * (1.0 - w.y * 0.6);
	col = mix(col, glaze_color * (0.7 + 0.4 * lum), glaze * 0.6);
	ALBEDO = col;

	float rough = 0.9 + 0.08 * (1.0 - lum) + 0.04 * (1.0 - h);
	ROUGHNESS = clamp(mix(rough, 0.42, glaze), 0.0, 1.0);
	SPECULAR = mix(0.18, 0.32, glaze);
	// the cold light got into it and back out, more in the hollows
	BACKLIGHT = hollow_color * (0.12 + 0.12 * (1.0 - h));
	// a crystal catching the eye, on the crests
	vec3 cell = floor(w_pos * 60.0);
	if (hash3(cell) > 0.997 && h > 0.5) {
		vec3 facet = normalize(vec3(hash3(cell + 1.3), hash3(cell + 4.7), hash3(cell + 9.1)) * 2.0 - 1.0 + wn * 1.2);
		vec3 v = normalize((INV_VIEW_MATRIX * vec4(VIEW, 0.0)).xyz);
		EMISSION = vec3(0.85, 0.92, 1.0) * pow(clamp(dot(facet, v), 0.0, 1.0), 120.0) * glisten;
	}
}
