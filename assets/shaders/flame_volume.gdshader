// A tongue of fire with depth (2026-10-10c, the owner: "fix the flames for the flamethrower and burning things to look
// more 3d and less 2d"). It replaces the flat cards turned to the eye: each tongue is a small sphere mesh drawn up into a
// teardrop in the vertex shader, its surface pushed about by 3D noise climbing through it (the licks writhe round it, so it
// has a shape from every side, from above too), its tip swaying. The fragment takes how square-on each bit of the surface
// is to the eye as how much fire you look through there: thick and hot through its middle, thin and red at its rim, torn
// away with height. Its flicker is slow (no flashing).
// INSTANCE_CUSTOM.x = seed (0..1), .y = speed scale, .z = sway.
shader_type spatial;
render_mode blend_add, unshaded, cull_back, depth_draw_never, shadows_disabled, fog_disabled;

uniform float intensity : hint_range(0.0, 1.0) = 1.0;
uniform vec3 core_color : source_color = vec3(1.0, 0.8, 0.42);
uniform vec3 body_color : source_color = vec3(1.0, 0.4, 0.07);
uniform vec3 edge_color : source_color = vec3(0.5, 0.07, 0.02);
uniform float brightness = 1.9;

varying float v_seed;
varying float v_h;      // 0 at the base, 1 at the tip
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
	float speed = 0.7 + INSTANCE_CUSTOM.y;
	float t = TIME * speed;
	// the unit sphere (radius 0.5) to a teardrop standing on y = 0: round below, drawn to a point above
	float h = clamp(VERTEX.y + 0.5, 0.0, 1.0);
	vec2 xz = VERTEX.xz * 2.0;   // (-1..1 round its ring)
	float r = 0.5 * pow(1.0 - h, 0.85) * (0.75 + 0.6 * sin(3.14159 * min(h * 1.6, 1.0)));
	float tall = mix(0.4, 1.0, intensity);
	vec3 p = vec3(xz.x * r, h * tall, xz.y * r);
	// the licks: the surface pushed in and out by noise climbing through it, more toward the top
	float n = noise3(vec3(xz * 1.6, h * 2.4 - t * 2.1) + v_seed * 31.0);
	float n2 = noise3(vec3(xz * 3.1 + 7.0, h * 4.0 - t * 3.3) + v_seed * 17.0);
	float push = (n - 0.5) * 0.5 + (n2 - 0.5) * 0.22;
	p.xz += normalize(xz + vec2(1e-4)) * push * r * (0.6 + h);
	p.y += (n2 - 0.45) * 0.18 * h * tall;
	// the tip swaying (slowly, each its own way)
	float sw = INSTANCE_CUSTOM.z * 4.0;
	p.x += sin(t * 1.4 + v_seed * 20.0) * sw * h * h;
	p.z += cos(t * 1.1 + v_seed * 13.0) * sw * h * h;
	VERTEX = p;
	v_h = h;
	v_obj = vec3(xz, h);
}

void fragment() {
	float t = TIME * 1.0;
	// how much fire the eye looks through here: square-on is through its middle
	float facing = clamp(abs(dot(normalize(NORMAL), normalize(VIEW))), 0.0, 1.0);
	float n = noise3(vec3(v_obj.xy * 2.2, v_obj.z * 3.0 - t * 2.4) + v_seed * 11.0);
	float body = smoothstep(0.08, 0.75, facing + (n - 0.5) * 0.5);
	// it thins and tears away with height
	float f = body * clamp(1.2 - v_h * (1.15 - n * 0.55), 0.0, 1.0);
	f *= smoothstep(0.0, 0.1, v_h);
	f = clamp(f, 0.0, 1.0);
	vec3 col = mix(edge_color, body_color, smoothstep(0.1, 0.5, f));
	col = mix(col, core_color, smoothstep(0.6, 0.95, f) * (1.0 - v_h * 0.7));
	float breathe = 0.92 + 0.08 * sin(TIME * 2.6 + v_seed * 40.0);
	ALBEDO = col * brightness * breathe;
	ALPHA = f * intensity;
}
