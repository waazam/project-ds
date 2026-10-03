// The snowflake generator (the owner, 2026-10-03: "people use star texture generators", their shader to start from; made
// our own, so the snow owes no one a credit). Their star traced one outline round the middle from a curve; a snowflake
// is more than that: six arms, each the mirror of itself (twelve-fold), and off each arm the branches that make it a
// crystal. So here, in the fundamental wedge (the angle folded six ways and mirrored: their `mirrored` sampling, taken
// all the way), each flake is built as distances to its parts:
//   - the arm: a tapering line from the middle to its tip;
//   - its branches: lines off it at 60 degrees, longest near the middle or the middle of the arm, and their own
//     sub-branches parallel to the arm (the fern-like dendrites);
//   - plates: a hexagon at the heart with ridges running out along the arms; small plates at the arms' tips; or the
//     arm itself broadened into a sector (the sectored plate);
//   - the ice: a little more opaque along every edge and ridge than across a plate's face.
// The type and every proportion come from `seed`, so no two are alike. Baked once into the snow's atlas
// (CreaturePreview -- --bake-snow): it's far too much to work out for forty thousand flakes a frame.
shader_type canvas_item;
render_mode blend_mix;

uniform float seed = 1.0;
/// 0 stellar dendrite (branched), 1 fern (many fine branches), 2 plate (a hexagon, ridged), 3 sectored plate,
/// 4 star with tip plates, 5 needle-like simple star.
uniform int kind = 0;
/// Defocus (0 sharp .. 1 soft): the far and the middle flakes.
uniform float blur = 0.0;
/// The whole flake's opacity.
uniform float strength = 1.0;
/// A soft round clump instead (the wet flakes, the specks): 0 off, 1 on.
uniform float round_clump = 0.0;

float hash(float n) { return fract(sin(n * 127.1 + seed * 311.7) * 43758.5453); }

float seg(vec2 p, vec2 a, vec2 b, float r0, float r1) {
	vec2 pa = p - a, ba = b - a;
	float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
	return length(pa - ba * h) - mix(r0, r1, h);
}

float hexagon(vec2 v, float apothem) {
	// a hexagon whose corners point along the arms (as a crystal's do): folded six ways and mirrored, then the distance
	// to the side whose normal is at 30 degrees
	float s = 1.0471976;
	float b = abs(mod(atan(v.y, v.x) + s * 0.5, s) - s * 0.5);
	return dot(vec2(cos(b), sin(b)) * length(v), vec2(0.8660254, 0.5)) - apothem;
}

void fragment() {
	vec2 q = (UV - 0.5) * 2.0;
	float r = length(q);
	float ang = atan(q.y, q.x);
	// a turn of its own, so the arms don't always point the same way in the atlas
	ang += hash(9.0) * 1.0471976;
	// fold: six arms (60 degrees each), each mirrored about itself
	float sector = 1.0471976;
	float a = mod(ang + sector * 0.5, sector) - sector * 0.5;
	a = abs(a);
	vec2 p = vec2(cos(a), sin(a)) * r;

	float R = 0.82 + 0.12 * hash(1.0);                      // the arm's reach
	if (kind == 2) R = 0.7 / 0.8660254 + 0.04;             // (a plate's arms end at its corners)
	float w = mix(0.022, 0.05, hash(2.0));                   // its thickness
	if (kind == 1) w *= 0.7;
	if (kind == 5) w *= 0.8;
	float d = 1e3;      // distance to the crystal (negative inside)
	float ridge = 1e3;  // distance to its ridges (they read brighter: the ice's edges)

	// the arm, tapering to its tip
	float arm = seg(p, vec2(0.0), vec2(R, 0.0), w, w * 0.45);
	d = min(d, arm);
	ridge = min(ridge, abs(p.y) - 0.004);

	// the heart: a hexagonal plate, its ridges running out along the arms
	float heart = mix(0.06, 0.22, hash(3.0));
	if (kind == 2) heart = 0.7;
	if (kind == 5) heart = 0.05;
	float hx = dot(p, vec2(0.8660254, 0.5)) - heart;   // (p is already folded: the heart's corners on the arms)
	d = min(d, hx);
	ridge = min(ridge, abs(hx + heart * 0.35) - 0.006);   // an inner hexagon traced in the plate

	if (kind == 0 || kind == 1 || kind == 4) {
		// the branches: at 60 degrees off the arm, the longest partway along, shorter toward the tip
		int n = kind == 1 ? 7 : 3 + int(hash(4.0) * 3.0);
		float peak = mix(0.3, 0.6, hash(5.0));
		for (int i = 0; i < 8; i++) {
			if (i >= n) break;
			float t = mix(heart / R + 0.1, 0.92, (float(i) + 0.5) / float(n));
			t += (hash(10.0 + float(i)) - 0.5) * 0.06;
			float len = R * (kind == 1 ? 0.32 : 0.42) * (1.0 - abs(t - peak) * 1.3) * mix(0.75, 1.15, hash(20.0 + float(i)));
			len = max(len, 0.03);
			vec2 root = vec2(t * R, 0.0);
			vec2 dir = vec2(0.5, 0.8660254);
			vec2 tip = root + dir * len;
			float bw = w * 0.75;
			d = min(d, seg(p, root, tip, bw, bw * 0.4));
			// their own sub-branches, parallel to the arm (the dendrite's fronds)
			if (kind != 4 && len > 0.12) {
				int m = kind == 1 ? 3 : 2;
				for (int j = 0; j < 3; j++) {
					if (j >= m) break;
					float u = (float(j) + 1.0) / (float(m) + 1.0);
					vec2 r2 = root + dir * len * u;
					float l2 = len * 0.35 * (1.0 - u * 0.5);
					d = min(d, seg(p, r2, r2 + vec2(1.0, 0.0) * l2, bw * 0.6, bw * 0.25));
					d = min(d, seg(p, r2, r2 + vec2(-0.5, 0.8660254) * l2 * 0.7, bw * 0.5, bw * 0.2));
				}
			}
		}
	}
	if (kind == 3) {
		// the sectored plate: the arm broadened into a blade, a rib down its middle
		float blade = seg(p, vec2(heart * 0.8, 0.0), vec2(R * 0.92, 0.0), R * 0.16, R * 0.05);
		d = min(d, blade);
		ridge = min(ridge, abs(p.y) - 0.006);
		ridge = min(ridge, abs(blade + R * 0.04) - 0.006);
	}
	if (kind == 4) {
		// small plates at the tips
		vec2 tp = p - vec2(R * 0.92, 0.0);
		float tipPlate = hexagon(tp, mix(0.06, 0.1, hash(6.0)));
		d = min(d, tipPlate);
		ridge = min(ridge, abs(tipPlate + 0.03) - 0.005);
	}
	if (kind == 2) {
		// a plate: solid ice, ridged out along the arms and once round inside (no more: rings on rings read as a web)
		ridge = min(abs(p.y) - 0.005, abs(hx + heart * 0.45) - 0.006);
	}
	// the crystal: crisp (a pixel and a half of edge), or defocused
	float px = fwidth(r) * 1.5 + blur * 0.12;
	float body = 1.0 - smoothstep(-px, px, d);
	// the ice: faces a little clear, edges and ridges fuller
	float edge = 1.0 - smoothstep(0.0, 0.035 + blur * 0.08, abs(d));
	float rid = 1.0 - smoothstep(0.0, px * 1.5 + 0.004, ridge);
	float face = kind == 2 ? 0.7 : kind == 3 ? 0.6 : 0.85;
	float alpha = body * mix(face, 1.0, max(edge, rid));
	// never quite to the cell's edge (the atlas's neighbours)
	alpha *= 1.0 - smoothstep(0.92, 1.0, r);
	if (round_clump > 0.5) {
		// a wet clump or a speck: soft and a little lumpy
		float lump = 0.95 + 0.05 * sin(ang * 3.0 + hash(7.0) * 6.0) + 0.03 * sin(ang * 5.0 + hash(8.0) * 6.0);
		alpha = 1.0 - smoothstep(0.15, 0.62 + blur * 0.3, r / lump);
		alpha = pow(alpha, 1.2);
	}
	COLOR = vec4(1.0, 1.0, 1.0, clamp(alpha * strength, 0.0, 1.0));
}
