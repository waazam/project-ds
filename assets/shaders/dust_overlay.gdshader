// Years of dust (the look pass, 2026-10-02: the lodge, the station and the rest left as they were, long ago): drawn
// over a mesh as its overlay. Dust settles only where it can lie, on what faces up (table tops, seats, mantels, the
// tops of frames and books), thick in patches and thin between, and a little on the slopes; the sides stay clean.
// Pale and matte: it dulls what it lies on. World-space, so it's continuous across pieces and never stretched.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_lambert, specular_disabled;

uniform float amount : hint_range(0.0, 1.0) = 0.55;
uniform vec3 dust_color : source_color = vec3(0.5, 0.48, 0.43);

varying vec3 wpos;
varying vec3 wnorm;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}
float fbm(vec2 p) { return vnoise(p) * 0.55 + vnoise(p * 2.3 + 7.1) * 0.3 + vnoise(p * 5.7 + 3.3) * 0.15; }

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnorm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	float up = smoothstep(0.45, 0.95, wnorm.y);
	float patches = fbm(wpos.xz * 2.2 + wpos.y * 0.7);
	float fine = vnoise(wpos.xz * 40.0);
	float d = up * amount * smoothstep(0.25, 0.75, patches) * (0.75 + 0.25 * fine);
	ALBEDO = dust_color;
	ROUGHNESS = 1.0;
	ALPHA = clamp(d, 0.0, 0.8);
}
