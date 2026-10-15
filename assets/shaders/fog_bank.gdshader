// A bank of mist (2026-10-15, the Act 7 giant "should blend more with the background ... maybe add fog"): a big soft
// quad of drifting noise in the scene's own fog colour, hung along the far tree line in front of the giant's legs and
// torso so only parts of it come up out of the murk. Unshaded, never written to depth, its edges fading to nothing (no
// card ever shows its rim); `amount` fades the whole bank in and out with the giant.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

uniform vec3 color : source_color = vec3(0.1, 0.11, 0.12);
uniform float amount : hint_range(0.0, 1.0) = 1.0;
uniform float density : hint_range(0.0, 1.0) = 0.75;
uniform float drift = 0.0;
uniform float seed = 0.0;
uniform sampler2D depth_tex : hint_depth_texture, filter_linear;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}
float fbm(vec2 p) {
	float v = 0.0, a = 0.5;
	for (int i = 0; i < 5; i++) { v += a * vnoise(p); p = p * 2.03 + vec2(1.7, 9.2); a *= 0.5; }
	return v;
}

void fragment() {
	vec2 uv = UV;
	// two layers drifting at their own pace: the mist rolls, never slides as one sheet
	float n = fbm(vec2(uv.x * 2.6 + drift + seed, uv.y * 1.4 + seed * 0.37));
	n = mix(n, fbm(vec2(uv.x * 4.1 - drift * 0.6 + seed * 1.9, uv.y * 2.2 + drift * 0.15)), 0.4);
	// soft all round: densest low and in the middle, thinning upward into the air
	float edge = smoothstep(0.0, 0.3, uv.x) * smoothstep(1.0, 0.7, uv.x) * smoothstep(0.0, 0.45, uv.y) * smoothstep(1.0, 0.8, uv.y);
	float a = smoothstep(0.28, 0.8, n) * edge * density * amount;
	// where it meets the trees: faded over the last few metres (no hard line where a card cuts a crown)
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	float scene = -view.z / view.w;
	a *= clamp((scene - (-VERTEX.z)) / 8.0, 0.0, 1.0);
	ALBEDO = color;
	ALPHA = a;
}
