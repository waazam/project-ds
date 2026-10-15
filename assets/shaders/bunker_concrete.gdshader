// Bunker concrete (hallway, CRT room, maze, the outside facade).
// - World-space triplanar mapping of a low-res texture: every face, curved or
//   flat, gets the same metre scale, so nothing smears or stretches.
// - Vertex colour tints; vertex alpha is dryness (0 = standing water: darker
//   and glossy, so wet floor strips catch the lights).
// - Grime from a second low-frequency texture, heavier near the floor.
// - ambient_floor: a faint self-light so walls still read in near darkness
//   (fogged like everything else).
// - breathe: an optional slow warp of the maze walls ("distorted realities").
//   The offset depends only on world position, so faces never crack apart.
// - (2026-10-14h, the bunker pass) painted: the cold-war institutional coat over the concrete, measured in the mesh's
//   own space (height above its floor, distance along the wall): a dark green dado to `dado`, a black stripe, a pale
//   grey-green above; the paint chalked, flaked off in patches (bare concrete and a lifted lip round each), rust bled
//   down from the ribs' fixings, a dried tide line of water at the foot, calcite weeping from the vault, ring stains on
//   the ceiling. lino: a floor of old linoleum tiles in two dull colours, worn, scuffed, whole patches gone to the
//   concrete and its black adhesive. floor_lines: two faded yellow lines down a corridor floor. concrete_desat: the
//   scan's colour (a pink-brown) taken toward grey.
shader_type spatial;
render_mode diffuse_lambert, specular_schlick_ggx;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D grime_tex : filter_linear_mipmap, repeat_enable;
// (2026-10-13g, the texture-mood pass) the scan's relief and roughness, so the walls have the early acts' weathered
// depth under the lantern instead of a flat print of concrete; off (0) when there is no scan
uniform sampler2D nor_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rough_tex : filter_linear_mipmap, repeat_enable;
uniform float normal_strength = 0.0;
uniform float rough_map = 0.0;
uniform float tile = 0.5;
uniform float grime_tile = 0.11;
uniform float grime_amount = 0.5;
uniform vec3 tint : source_color = vec3(1.0);
uniform float ambient_floor = 0.05;
uniform float base_rough = 0.92;
uniform float wet_rough = 0.14;
uniform float low_grime = 0.35;
uniform float breathe = 0.0;
uniform float breathe_speed = 0.3;

// ---- (2026-10-14h) the bunker pass
uniform float concrete_desat = 0.0;
uniform vec3 concrete_tint : source_color = vec3(1.0);
uniform sampler2D paint_noise : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform bool painted = false;
uniform vec3 paint_low : source_color = vec3(0.2, 0.24, 0.2);
uniform vec3 paint_high : source_color = vec3(0.33, 0.37, 0.31);
uniform vec3 stripe_col : source_color = vec3(0.09, 0.1, 0.09);
uniform float dado = 1.15;
uniform float stripe = 0.05;
uniform float paint_top = 99.0;
uniform bool paint_ceiling = false;   // the coat carried over the ceiling too (a vault)
uniform float peel = 0.5;
uniform float rust = 0.7;
uniform float rust_from = 2.35;
uniform float rib_spacing = 2.5;
uniform float rib_offset = -1.25;
uniform float tide_h = 0.42;
uniform float tide = 0.6;
uniform float calcite = 0.5;
uniform float seed = 0.0;
uniform bool lino = false;
uniform vec3 lino_a : source_color = vec3(0.25, 0.26, 0.23);
uniform vec3 lino_b : source_color = vec3(0.32, 0.31, 0.26);
uniform float lino_tile = 0.3;
uniform float lino_missing = 0.4;
uniform float floor_lines = 0.0;   // half-width where a corridor's two yellow lines run (0: none)

varying vec3 wpos;
varying vec3 wnrm;
varying float lheight;
varying vec3 lpos;
varying vec3 lnrm;

void vertex() {
	vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	if (breathe > 0.0) {
		float h = smoothstep(0.1, 2.6, VERTEX.y);
		float a = TIME * breathe_speed + wp.x * 0.37 + wp.z * 0.29;
		float b = TIME * breathe_speed * 0.71 + wp.z * 0.41 - wp.x * 0.23;
		VERTEX += vec3(sin(a), 0.0, cos(b)) * breathe * h;
		wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	}
	wpos = wp;
	wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	lheight = VERTEX.y;
	lpos = VERTEX;
	lnrm = NORMAL;
}

vec3 tri_normal(sampler2D t, vec3 p, vec3 n, float s, float k) {
	vec3 w = pow(abs(n), vec3(6.0));
	w /= (w.x + w.y + w.z);
	vec3 nx = texture(t, p.zy * s).xyz * 2.0 - 1.0;
	vec3 ny = texture(t, p.xz * s).xyz * 2.0 - 1.0;
	vec3 nz = texture(t, p.xy * s).xyz * 2.0 - 1.0;
	nx.xy *= k; ny.xy *= k; nz.xy *= k;
	vec3 tx = vec3(nx.xy + n.zy, abs(nx.z) * n.x);
	vec3 ty = vec3(ny.xy + n.xz, abs(ny.z) * n.y);
	vec3 tz = vec3(nz.xy + n.xy, abs(nz.z) * n.z);
	return normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
}

vec3 triplanar(sampler2D t, vec3 p, vec3 n, float s) {
	vec3 w = pow(abs(n), vec3(6.0));
	w /= (w.x + w.y + w.z);
	return texture(t, p.zy * s).rgb * w.x
		+ texture(t, p.xz * s).rgb * w.y
		+ texture(t, p.xy * s).rgb * w.z;
}

float hash2(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7)) + seed * 13.7) * 43758.5453); }
float nz(vec2 p) { return texture(paint_noise, p + vec2(seed * 0.173, seed * 0.311)).r; }

void fragment() {
	vec3 n = normalize(wnrm);
	vec3 ln = normalize(lnrm);
	vec3 c = triplanar(albedo_tex, wpos, n, tile);
	float lum = dot(c, vec3(0.3, 0.59, 0.11));
	c = mix(c, vec3(lum), concrete_desat) * concrete_tint;
	float g = triplanar(grime_tex, wpos + vec3(7.3, 0.0, 3.1), n, grime_tile).r;
	float wet = 1.0 - COLOR.a;
	float wall = 1.0 - abs(n.y);
	float low = 1.0 - smoothstep(0.0, 0.55, lheight);
	vec3 base = c;
	float rough_extra = 0.0;
	float h = lheight;
	float along = abs(ln.x) > abs(ln.z) ? lpos.z : lpos.x;

	if (painted && (ln.y > -0.6 || paint_ceiling) && h < paint_top) {
		// the coat: the dado's green, the stripe, the pale grey-green above, chalked and blotched
		float fine = nz(vec2(along, h) * 1.9);
		float blot = nz(vec2(along, h) * 0.31 + vec2(3.7, 1.1));
		vec3 pc = h < dado ? paint_low : paint_high;
		float st = 1.0 - smoothstep(stripe * 0.5, stripe * 0.5 + 0.006, abs(h - dado - stripe * 0.5));
		pc = mix(pc, stripe_col, st);
		pc *= (0.86 + 0.26 * fine) * (0.8 + 0.45 * lum);
		pc = mix(pc, pc * vec3(1.04, 1.02, 0.9), smoothstep(0.45, 0.8, blot) * 0.6);   // yellowed where it's oldest
		// flaking: more at the damp foot and up the vault, in clusters
		float bias = peel * (0.32 + 0.35 * (1.0 - smoothstep(0.0, 0.9, h)) + 0.3 * smoothstep(1.9, 3.2, h));
		float pm = blot * 0.62 + fine * 0.38;
		float t0 = 1.0 - bias;
		float bare = smoothstep(t0, t0 + 0.025, pm);
		float lip = smoothstep(t0 - 0.045, t0, pm) * (1.0 - bare);
		float rim = smoothstep(t0, t0 + 0.02, pm) - smoothstep(t0 + 0.02, t0 + 0.07, pm);
		vec3 conc = c * vec3(0.92, 0.9, 0.84);
		base = mix(pc, conc * (1.0 - 0.4 * rim), bare);
		base = mix(base, base * 1.18 + vec3(0.015), lip * 0.6);
		rough_extra = (1.0 - bare) * -0.25;   // the paint a little less dead than the concrete
		// rust bled down from the fixings: the ribs' bolts and hooks, and thin runs from odd places
		float d = abs(fract((along - rib_offset) / rib_spacing + 0.5) - 0.5) * rib_spacing;
		float wob = (nz(vec2(along * 0.6, h * 0.2)) - 0.5) * 0.18;
		float near_rib = (1.0 - smoothstep(0.03, 0.1, abs(d + wob - 0.2))) * step(0.45, nz(vec2(floor((along - rib_offset) / rib_spacing + 0.5) * 0.37, 0.9)));
		float cols = smoothstep(0.7, 0.8, nz(vec2(along * 0.33, 0.37))) * smoothstep(0.5, 0.68, nz(vec2(along * 9.0, 0.61)));
		float len = 0.5 + 1.6 * nz(vec2(along * 0.47, 0.71));
		float src = rust_from - 0.25 * nz(vec2(along * 0.9, 0.13));
		float fall = h < src ? exp(-(src - h) / len) : smoothstep(src + 0.08, src, h) * 0.6;
		float runs = 0.55 + 0.45 * nz(vec2(along * 9.0, h * 0.35));
		float r = rust * clamp(max(near_rib * 0.85, cols), 0.0, 1.0) * fall * runs;
		base = mix(base, vec3(0.22, 0.11, 0.05) * (0.7 + 0.5 * fine), clamp(r, 0.0, 0.8));
		// calcite and water weeping from above: paler runs on the upper wall and the vault
		float cw = smoothstep(0.7, 0.8, nz(vec2(along * 0.9, 5.3))) * smoothstep(0.8, 2.6, h) * (0.5 + 0.5 * nz(vec2(along * 6.0, h * 0.5)));
		base = mix(base, vec3(0.62, 0.62, 0.56) * (0.8 + 0.3 * lum), cw * calcite * 0.55);
		// the tide line of an old flood, dried: darker and browner below a ragged crisp edge
		float th = tide_h + (nz(vec2(along * 0.17, 2.9)) - 0.5) * 0.3;
		float below = 1.0 - smoothstep(th - 0.015, th + 0.015, h);
		float edge = exp(-abs(h - th) * 45.0);
		base *= mix(vec3(1.0), vec3(0.74, 0.69, 0.58), below * tide);
		base *= 1.0 - 0.3 * edge * tide;
	} else if (painted && ln.y <= -0.6) {
		// the bare ceiling: ring-edged water stains
		float s = nz(lpos.xz * 0.21 + vec2(1.3, 7.1));
		float inside = smoothstep(0.6, 0.63, s);
		float ring = smoothstep(0.57, 0.6, s) - smoothstep(0.62, 0.66, s);
		base *= mix(vec3(1.0), vec3(0.78, 0.72, 0.6), inside * 0.7);
		base *= 1.0 - 0.35 * ring;
		base = mix(base, vec3(0.58, 0.58, 0.52) * (0.7 + 0.4 * lum), smoothstep(0.75, 0.82, nz(lpos.xz * 0.6)) * calcite * 0.5);
	}

	if (lino && ln.y > 0.5) {
		vec2 gp = lpos.xz / lino_tile;
		vec2 cell = floor(gp);
		vec2 f = fract(gp);
		float chk = mod(cell.x + cell.y, 2.0);
		float hs = hash2(cell);
		vec3 tc = mix(lino_a, lino_b, chk) * (0.86 + 0.22 * hs);
		float wide = nz(lpos.xz * 0.07 + vec2(5.1, 2.3));
		float fine = nz(lpos.xz * 1.3);
		// worn: dull, paler, the pattern rubbed through where feet went (and the bake's wear in the alpha)
		tc = mix(tc, vec3(0.42, 0.41, 0.37) * (0.8 + 0.3 * lum), smoothstep(0.55, 0.85, wide) * 0.5);
		float edge_d = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y)) * lino_tile;
		float seam = 1.0 - smoothstep(0.002, 0.008, edge_d);
		tc *= (1.0 - 0.55 * seam) * (0.82 + 0.3 * lum);
		tc *= 0.9 + 0.2 * fine;
		// whole tiles gone in clusters: the concrete under them, combed black adhesive on it
		float gone = step(1.0 - lino_missing * (0.25 + 1.1 * smoothstep(0.35, 0.8, wide)), hs * 0.55 + wide * 0.45);
		float comb = smoothstep(0.35, 0.65, nz(vec2(lpos.x * 2.2 + sin(lpos.z * 9.0) * 0.08, lpos.z * 0.4)));
		vec3 glue = mix(c * 0.85, vec3(0.045, 0.04, 0.035), comb * 0.75);
		base = mix(tc, glue, gone);
		rough_extra = (1.0 - gone) * -0.3;
	}

	if (floor_lines > 0.0 && ln.y > 0.5) {
		float x = abs(lpos.x);
		float line = 1.0 - smoothstep(0.035, 0.045, abs(x - floor_lines));
		float worn = smoothstep(0.35, 0.7, nz(vec2(lpos.z * 0.5, x * 3.0))) * 0.8 + 0.2 * nz(lpos.xz * 4.0);
		base = mix(base, vec3(0.5, 0.41, 0.12) * (0.7 + 0.4 * lum), line * (1.0 - worn) * 0.8);
	}

	vec3 alb = base * COLOR.rgb * tint;
	alb *= mix(1.0, 0.55 + g * 0.7, grime_amount);
	alb *= 1.0 - low_grime * low * wall;
	alb *= mix(1.0, 0.45, wet);
	ALBEDO = alb;
	float r = base_rough;
	if (rough_map > 0.0) r = mix(base_rough, clamp(triplanar(rough_tex, wpos, n, tile).r * 1.05 + 0.08, 0.55, 1.0), rough_map);
	r = clamp(r + rough_extra, 0.35, 1.0);
	ROUGHNESS = mix(r, wet_rough, wet);
	if (normal_strength > 0.0) {
		vec3 wn = tri_normal(nor_tex, wpos, n, tile, normal_strength);
		NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
	}
	SPECULAR = mix(0.3, 0.7, wet);
	EMISSION = alb * ambient_floor;
}
