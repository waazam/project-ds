shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_lambert;

// Act 6's menacing-forest ground: laid on a thin skin that follows the terrain
// exactly (VeinyGround builds it from ForestTerrain.HeightAt, same triangulation),
// so it never floats or clips, and it is lit and fogged like the ground under it.
// Pattern is in world XZ. Toward the edge of the area it is only scattered
// worm-like squiggles; toward the centre they join into an interconnected,
// slowly pulsing mesh of veins ("first something that could be worms, then an
// interconnected, pulsating veiny mesh on the ground").
uniform vec4 vein_color : source_color = vec4(0.22, 0.025, 0.04, 1.0);
uniform vec4 vein_dark : source_color = vec4(0.10, 0.01, 0.02, 1.0);
uniform vec3 pulse_glow : source_color = vec3(0.45, 0.03, 0.05);
uniform float glow = 0.12;
uniform vec2 center = vec2(0.0, 0.0);
uniform float radius = 34.0;
uniform float cell = 1.7;            // vein network cell size, metres
uniform float speed = 0.12;
uniform float intensity : hint_range(0.0, 2.0) = 1.0;
uniform float lift = 0.03;           // metres above the terrain surface

varying vec3 wpos;

float hash1(vec2 p) { return fract(sin(dot(p, vec2(41.3, 289.1))) * 43758.5453); }
vec2 hash2(vec2 p) {
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return fract(sin(p) * 43758.5453);
}
float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash1(i), hash1(i + vec2(1.0, 0.0)), u.x), mix(hash1(i + vec2(0.0, 1.0)), hash1(i + vec2(1.0, 1.0)), u.x), u.y);
}

// distance to the nearest Voronoi cell border (the vein network)
float voronoi_edge(vec2 x) {
	vec2 n = floor(x), f = fract(x);
	vec2 mg, mr;
	float md = 8.0;
	for (int j = -1; j <= 1; j++)
	for (int i = -1; i <= 1; i++) {
		vec2 g = vec2(float(i), float(j));
		vec2 o = hash2(n + g);
		vec2 r = g + o - f;
		float d = dot(r, r);
		if (d < md) { md = d; mr = r; mg = g; }
	}
	md = 8.0;
	for (int j = -2; j <= 2; j++)
	for (int i = -2; i <= 2; i++) {
		vec2 g = mg + vec2(float(i), float(j));
		vec2 o = hash2(n + g);
		vec2 r = g + o - f;
		if (dot(mr - r, mr - r) > 0.00001)
			md = min(md, dot(0.5 * (mr + r), normalize(r - mr)));
	}
	return md;
}

void vertex() {
	VERTEX += NORMAL * lift;
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 p = wpos.xz;
	float t = TIME * speed;
	float dist = length(p - center);
	// 0 at the rim (worms only) .. 1 in the middle (full mesh)
	float depth = smoothstep(radius * 1.05, radius * 0.4, dist);

	// warp so the network wriggles rather than looking like tiles
	vec2 w = vec2(vnoise(p * 0.45 + t), vnoise(p * 0.45 - t + 7.3)) - 0.5;
	float e1 = voronoi_edge((p + w * 1.2) / cell);
	float e2 = voronoi_edge((p * 1.9 + w * 0.8 + 13.0) / cell);
	float thick = mix(0.016, 0.04, depth);
	float mesh_v = 1.0 - smoothstep(thick * 0.5, thick, e1);
	mesh_v = max(mesh_v, (1.0 - smoothstep(thick * 0.3, thick * 0.65, e2)) * 0.8 * depth);

	// worms: short squiggly strands (noise contour lines chopped up by a sparse mask)
	float wn = vnoise(p * 1.3 + vec2(t * 1.5, -t));
	float strand = 1.0 - smoothstep(0.0, 0.035, abs(wn - 0.5));
	float chop = smoothstep(0.62, 0.7, vnoise(p * 0.9 + 31.0 + t * 0.6));
	float worms = strand * chop;

	// near the rim only worms; the network fades in toward the middle and eats them
	float network_on = smoothstep(0.15, 0.75, depth);
	float v = max(worms * (1.0 - network_on * 0.6), mesh_v * network_on);
	// fade out entirely at the very rim so there is no hard circle
	v *= smoothstep(radius * 1.15, radius * 0.95, dist);

	// slow heartbeat travelling outward along the veins
	float beat = 0.5 + 0.5 * sin(TIME * 1.4 - dist * 0.35 + vnoise(p * 0.3) * 4.0);
	float swell = mix(0.85, 1.15, beat);

	float a = clamp(v * swell * intensity, 0.0, 1.0);
	ALBEDO = mix(vein_dark.rgb, vein_color.rgb, clamp(mesh_v * 0.7 + beat * 0.5, 0.0, 1.0));
	ROUGHNESS = 0.35;            // wet and glistening under the lantern
	SPECULAR = 0.6;
	EMISSION = pulse_glow * glow * beat * beat * a * depth;
	ALPHA = a;
}
