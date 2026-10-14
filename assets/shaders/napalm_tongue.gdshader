// Napalm burning where it landed (2026-10-14d): tongues of flame standing up off a splash of sticky fuel (on the snow, on
// the ice, on a post, on the wendigo), the way flame_volume.gdshader draws a fire's tongues (a sphere drawn up into a
// writhing teardrop), but napalm's: a low, rich, orange body, the hottest yellow only down in its root, its tips tearing
// off into sooty black (premultiplied: the flame adds its light, the soot darkens). Slow licks, nothing flashes.
// INSTANCE_CUSTOM = (seed, speed, sway, strength 0..1: how much fuel is left).
shader_type spatial;
render_mode unshaded, blend_premul_alpha, cull_back, depth_draw_never, shadows_disabled, fog_disabled;

uniform float brightness = 1.25;
uniform float near_start = 0.35;
uniform float near_end = 1.3;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;

varying float v_seed;
varying float v_h;
varying float v_k;
varying vec3 v_obj;

float hash3(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float noise3(vec3 x) {
	vec3 i = floor(x), f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash3(i), hash3(i + vec3(1, 0, 0)), f.x), mix(hash3(i + vec3(0, 1, 0)), hash3(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash3(i + vec3(0, 0, 1)), hash3(i + vec3(1, 0, 1)), f.x), mix(hash3(i + vec3(0, 1, 1)), hash3(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

void vertex() {
	v_seed = INSTANCE_CUSTOM.x;
	v_k = INSTANCE_CUSTOM.w;
	float t = TIME * (0.9 + INSTANCE_CUSTOM.y);
	float h = clamp(VERTEX.y + 0.5, 0.0, 1.0);
	vec2 xz = VERTEX.xz * 2.0;
	float r = 0.5 * pow(1.0 - h, 0.55) * (0.75 + 0.6 * sin(3.14159 * min(h * 1.7, 1.0)));
	vec3 p = vec3(xz.x * r, h, xz.y * r);
	float n = noise3(vec3(xz * 1.6, h * 2.6 - t * 2.3) + v_seed * 31.0);
	float n2 = noise3(vec3(xz * 3.2 + 7.0, h * 4.2 - t * 3.6) + v_seed * 17.0);
	p.xz += normalize(xz + vec2(1e-4)) * ((n - 0.5) * 0.8 + (n2 - 0.5) * 0.4) * r * (0.6 + h);
	// the whole lick bending and kinking as it climbs (no candle flame)
	float bend = noise3(vec3(v_seed * 9.0, h * 1.6 - t * 1.8, 0.0)) - 0.5;
	float bend2 = noise3(vec3(0.0, h * 1.6 - t * 1.5, v_seed * 7.0)) - 0.5;
	p.xz += vec2(bend, bend2) * 0.5 * h * h;
	p.y += (n2 - 0.45) * 0.3 * h;
	float sw = INSTANCE_CUSTOM.z * 4.0;
	p.x += sin(t * 1.5 + v_seed * 20.0) * sw * h * h;
	p.z += cos(t * 1.2 + v_seed * 13.0) * sw * h * h;
	VERTEX = p;
	v_h = h;
	v_obj = vec3(xz, h);
}

void fragment() {
	float facing = clamp(abs(dot(normalize(NORMAL), normalize(VIEW))), 0.0, 1.0);
	float n = noise3(vec3(v_obj.xy * 2.2, v_obj.z * 3.2 - TIME * 2.6) + v_seed * 11.0);
	float n2 = noise3(vec3(v_obj.xy * 5.0, v_obj.z * 6.0 - TIME * 4.0) + v_seed * 5.0);
	float body = smoothstep(0.2, 0.85, facing + (n - 0.5) * 0.55 + (n2 - 0.5) * 0.2);
	float f = body * clamp(1.15 - v_h * (1.2 - n * 0.55), 0.0, 1.0) * smoothstep(0.0, 0.08, v_h);
	// torn: ragged holes and loose wisps toward its top
	f *= smoothstep(0.2, 0.5, n2 + 0.62 - v_h * 0.55);
	f = clamp(f, 0.0, 1.0);
	float tmp = f * (0.55 + 0.45 * v_k) * (1.0 - v_h * 0.45);
	vec3 col = mix(vec3(0.4, 0.05, 0.005), vec3(1.05, 0.3, 0.04), smoothstep(0.08, 0.4, tmp));
	col = mix(col, vec3(1.55, 0.78, 0.18), smoothstep(0.4, 0.7, tmp));
	col = mix(col, vec3(1.85, 1.35, 0.65), smoothstep(0.75, 0.95, tmp) * (1.0 - v_h));
	float fa = f * smoothstep(0.0, 0.3, v_k);
	// the soot torn off its tips
	float soot = smoothstep(0.45, 0.95, v_h) * body * (1.0 - f) * 0.7 * smoothstep(0.0, 0.3, v_k);
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 up = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	float scene_z = -up.z / up.w;
	float k = clamp((scene_z + VERTEX.z) / 0.25, 0.0, 1.0) * smoothstep(near_start, near_end, -VERTEX.z);
	fa *= k;
	ALBEDO = col * brightness * fa;
	ALPHA = clamp(fa * 0.65 + soot * k * (1.0 - fa), 0.0, 1.0);
}
