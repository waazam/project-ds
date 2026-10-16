// Act 7's smoke filling the cabin from the ceiling down (2026-10-16): one sheet of a stack of horizontal sheets under the
// roof, each a drifting veil of dark smoke, warmed from below by the fire. Thickness grows with the sheet's depth into
// the smoke (density). Slow, soft; never a flicker.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, blend_mix;

uniform float density = 0.2;
uniform vec3 smoke_color : source_color = vec3(0.07, 0.06, 0.055);
uniform vec3 warm_color : source_color = vec3(0.35, 0.12, 0.04);
uniform float warm = 0.3;
uniform float seed = 0.0;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p)
{
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

void fragment()
{
	vec2 p = UV * 3.0 + vec2(seed * 1.7, seed * 0.9);
	float n = noise(p + vec2(TIME * 0.05, TIME * 0.03)) * 0.6 + noise(p * 2.3 - vec2(TIME * 0.04, 0.0)) * 0.4;
	// soft at the walls, so no sheet's edge shows
	vec2 e = abs(UV - 0.5) * 2.0;
	float rim = 1.0 - smoothstep(0.85, 1.0, max(e.x, e.y));
	ALBEDO = mix(smoke_color, warm_color, warm * n);
	ALPHA = clamp(density * (0.55 + 0.7 * n) * rim, 0.0, 0.95);
}
