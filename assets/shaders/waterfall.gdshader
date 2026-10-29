// (2026-10-29, the owner's friend's playtest: the waterfall "looks like molten metal") Act 1's waterfall, its own
// shader now (pour.gdshader stays as it was for the pours elsewhere). Water, not metal:
//  - lighter: a pale green-grey glass body, not the near-black olive that read as poured iron under a hot highlight;
//  - white: aerated foam from just over the lip, most of the curtain white water lower down, on the strands' crests
//    and the broken edges;
//  - see-through: thin where it is smooth glass (the rock face shows through it), thick only where it is white;
//  - fast: fine streaks stretched hard down it and sliding at more than the fall's speed, two layers past each other;
//  - matte where it's foam, a soft wet sheen where it's glass: a low specular (the old 0.6 and a 0.08 roughness were
//    the metal), no sharp moving glints.
// UV.x runs across the sheet (0..1) or round a tube; UV.y down the pour in metres from the lip (0).
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_never, specular_schlick_ggx;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 tint : source_color = vec3(0.36, 0.44, 0.42);      // the water's body (glass over the lip)
uniform vec3 foam_color : source_color = vec3(0.82, 0.86, 0.86);
uniform float speed = 4.2;                                      // metres a second down it
uniform float opacity : hint_range(0.0, 1.0) = 1.0;
uniform float aerate_start = 0.0;                               // metres down it before it starts to whiten (a lip)
uniform float aerate_len = 1.2;                                 // metres of fall to go (nearly) all white
uniform float rope = 1.0;                                       // how broken its edges are
uniform float wobble = 0.05;                                    // metres the surges swell out
uniform float strands = 7.0;                                    // strands across it
uniform float tube = 0.0;                                       // 1: a tube (edges are its silhouette)
uniform float glass_alpha = 0.5;                               // how much of the rock shows through the smooth water
uniform float glow = 0.012;                                     // the foam lit a little from within (it scatters light)

float fall_t() { return TIME * speed; }

void vertex() {
	float t = fall_t();
	float surge = textureLod(noise_tex, vec2(UV.x * 0.5 + 0.13, UV.y * 0.22 - t * 0.22), 0.0).r - 0.45;
	float knot = textureLod(noise_tex, vec2(UV.x * 1.3, UV.y * 0.5 - t * 0.5), 0.0).r - 0.5;
	float grow = clamp((UV.y - aerate_start) * 0.8, 0.15, 1.0);
	VERTEX += NORMAL * wobble * grow * (surge * 1.5 + knot * 0.8);
	VERTEX.x += sin(TIME * 1.7 + UV.y * 1.1) * wobble * 0.4 * grow;
}

void fragment() {
	float t = fall_t();
	float ax = UV.x * strands;
	// the streaks: noise stretched very hard down the pour, fine across it, two layers sliding at different speeds
	float s1 = texture(noise_tex, vec2(ax * 0.8, UV.y * 0.05 - t * 0.075)).r;
	float s2 = texture(noise_tex, vec2(ax * 1.3 + 0.37, UV.y * 0.09 - t * 0.14)).r;
	float fine = texture(noise_tex, vec2(ax * 2.9 + 0.11, UV.y * 0.12 - t * 0.24)).r;   // the fine fast hair of it
	float s3 = texture(noise_tex, vec2(ax * 0.15 + 0.71, UV.y * 0.3 - t * 0.3)).r;   // blobs: gaps and surges
	float strand = s1 * 0.45 + s2 * 0.35 + fine * 0.2;
	float core = smoothstep(0.3, 0.75, strand);
	float e = tube > 0.5 ? abs(dot(NORMAL, VIEW)) * 1.3 : min(UV.x, 1.0 - UV.x) * 2.0;
	float fall = clamp((UV.y - aerate_start) / max(0.01, aerate_len), 0.0, 1.0);
	float bite = (s3 - 0.5) * rope * (0.5 + 0.7 * fall) + (strand - 0.5) * rope * 0.5;
	float body = smoothstep(0.02, 0.3, e + bite * 0.6);   // (fewer holes torn in the middle: they showed the dark rock as black blots)
	// aeration: white from just over the lip, most of it white lower down; the crests and the ragged edges first
	float aer = clamp(fall * 0.75 + (1.0 - body) * 0.35 + (1.0 - e) * 0.2, 0.0, 1.0);
	float white = smoothstep(0.66 - 0.32 * aer, 0.8 - 0.18 * aer, strand * 0.55 + s3 * 0.2 + fine * 0.25);
	white = max(white, smoothstep(0.6, 0.85, fine) * (0.25 + 0.6 * aer));   // the fast streaks always catch a little white
	vec3 glass = tint * (0.75 + 0.45 * core);
	vec3 c = mix(glass, foam_color, white);
	ALBEDO = c;
	EMISSION = foam_color * glow * white;
	ROUGHNESS = mix(0.32, 0.85, white);
	SPECULAR = mix(0.32, 0.15, white);
	METALLIC = 0.0;
	// soft relief only (a hard one slid bright glints down it, the look of poured metal)
	float dx = texture(noise_tex, vec2(ax * 1.3 + 0.37 + 0.03, UV.y * 0.09 - t * 0.14)).r - s2;
	float dy = texture(noise_tex, vec2(ax * 1.3 + 0.37, UV.y * 0.09 - t * 0.14 + 0.03)).r - s2;
	NORMAL = normalize(NORMAL + (VIEW_MATRIX * vec4(dx * 1.2, dy * 0.6, 0.0, 0.0)).xyz);
	// see-through where it's glass, near opaque where it's white water
	float a = mix(glass_alpha * (0.7 + 0.3 * core), 0.95, white);
	ALPHA = clamp(opacity * body * a * COLOR.a, 0.0, 1.0);
}
