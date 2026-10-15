// (the gushy-water pass, 2026-10-15) Water (or blood) pouring: a waterfall's curtain, a torrent through a window, a
// pipe's outflow, a mouth bleeding. UV.x runs across a sheet (0..1) or round a tube; UV.y down the pour in metres from
// where it leaves (0 at the lip). What it does:
//  - ropes: the body is combed into thick strands running down it, pulled apart into fingers and gaps toward its
//    edges (a sheet's sides, a tube's silhouette), which open and close as it falls; never a clean flat sheet;
//  - lumps: the surface swells out in surges carried down it at the fall's speed (vertex displacement along the
//    normal), and the whole of it sways a little;
//  - aeration: it goes whiter as it falls (more foam lower down and along the broken edges), with darker green-glass
//    water at the lip where it is still smooth;
//  - glints: the strands' wet highlights slide down with them.
// `viscous` (0..1) turns it into blood: slower, heavier lumps, glossy and dark, no white water, thicker edges.
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_never, specular_schlick_ggx;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 tint : source_color = vec3(0.1, 0.13, 0.1);       // the water's body
uniform vec3 foam_color : source_color = vec3(0.72, 0.76, 0.74);
uniform float speed = 2.4;                                      // metres a second down it
uniform float opacity : hint_range(0.0, 1.0) = 1.0;
uniform float foam = 1.0;                                       // how white it gets
uniform float aerate_start = 0.0;                               // metres down it before it starts to whiten (a lip)
uniform float aerate_len = 1.6;                                 // metres of fall to go fully white
uniform float rope = 1.0;                                       // how broken its edges are
uniform float wobble = 0.05;                                    // metres the surges swell out
uniform float strands = 6.0;                                    // strands across it (a sheet's width / a tube's round)
uniform float tube = 0.0;                                       // 1: a tube (edges are its silhouette)
uniform float viscous : hint_range(0.0, 1.0) = 0.0;
uniform float glow = 0.05;
uniform float flow = 1.0;                                       // 1 down, negative: drawn back up

float fall_t() { return TIME * speed * flow * (1.0 - 0.7 * viscous); }

void vertex() {
	float t = fall_t();
	float surge = textureLod(noise_tex, vec2(UV.x * 0.5 + 0.13, UV.y * 0.22 - t * 0.22), 0.0).r - 0.45;
	float knot = textureLod(noise_tex, vec2(UV.x * 1.3, UV.y * 0.5 - t * 0.5), 0.0).r - 0.5;
	float grow = clamp((UV.y - aerate_start) * 0.8, 0.15, 1.0); // smooth at the lip, lumpier as it falls
	VERTEX += NORMAL * wobble * grow * (surge * 1.5 + knot * 0.8) * (1.0 + 0.6 * viscous);
	// a slow sway of the whole pour, more at its foot
	VERTEX.x += sin(TIME * 1.3 + UV.y * 0.9) * wobble * 0.5 * grow;
}

void fragment() {
	float t = fall_t();
	float ax = UV.x * strands;
	// the strands: noise stretched hard down the pour (thin across, long along), two speeds so they slide past each other
	float s1 = texture(noise_tex, vec2(ax * 0.23, UV.y * 0.09 - t * 0.09)).r;
	float s2 = texture(noise_tex, vec2(ax * 0.41 + 0.37, UV.y * 0.16 - t * 0.12)).r;
	float s3 = texture(noise_tex, vec2(ax * 0.15 + 0.71, UV.y * 0.3 - t * 0.3)).r;   // blobs: gaps and surges
	float strand = s1 * 0.55 + s2 * 0.45;
	float core = smoothstep(0.35, 0.75, strand);
	// the edges: a sheet's sides or a tube's silhouette, eaten into fingers by the moving blobs
	float e = tube > 0.5 ? abs(dot(NORMAL, VIEW)) * 1.3 : min(UV.x, 1.0 - UV.x) * 2.0;
	float fall = clamp((UV.y - aerate_start) / max(0.01, aerate_len), 0.0, 1.0);
	float bite = (s3 - 0.5) * rope * (0.5 + 0.7 * fall) + (strand - 0.5) * rope * 0.5;
	float body = smoothstep(0.05, 0.3 + 0.1 * (1.0 - viscous), e + bite * (1.0 - 0.5 * viscous));
	// aeration: white water down the fall, on the strands' crests and the broken edges
	float aer = clamp(fall * 0.75 + (1.0 - body) * 0.4 + (1.0 - e) * 0.25, 0.0, 1.0);
	float white = smoothstep(0.64 - 0.3 * aer, 0.88 - 0.2 * aer, strand * 0.6 + s3 * 0.4) * foam * (1.0 - viscous);
	vec3 deep = tint * (0.35 + 0.8 * core);
	vec3 c = mix(deep, foam_color, white);
	// blood: dark and thick at the middle, a little lighter where it's thin over the edges
	c = mix(c, tint * (0.3 + 0.4 * core + 0.25 * (1.0 - e)), viscous);
	ALBEDO = c;
	EMISSION = mix(foam_color, tint, viscous) * glow * (0.4 + 0.6 * white);
	// (a tube's own highlight kept soft: sharp ones banded the ropes like pipes)
	ROUGHNESS = mix(mix(0.08 + 0.3 * tube, 0.55, white), 0.04 + 0.08 * tube, viscous);
	SPECULAR = mix(0.6, 0.9, viscous);
	// the strands' relief, sliding down with them
	float dx = texture(noise_tex, vec2(ax * 0.41 + 0.37 + 0.03, UV.y * 0.16 - t * 0.12)).r - s2;
	float dy = texture(noise_tex, vec2(ax * 0.41 + 0.37, UV.y * 0.16 - t * 0.12 + 0.03)).r - s2;
	NORMAL = normalize(NORMAL + (VIEW_MATRIX * vec4(dx * 3.0, dy * 1.5, 0.0, 0.0)).xyz * (1.0 + viscous));
	ALPHA = clamp(opacity * body * mix(0.75 + 0.25 * max(core, white), 1.0, viscous) * COLOR.a, 0.0, 1.0);
}
