// The flamethrower's fire with depth (2026-10-10c, the owner: "fix the flames for the flamethrower and burning things to
// look more 3d and less 2d"). Each burning glob of the stream is a small sphere, stretched along its flight by the stream
// (its basis: x along the flight, y up, z across), its surface pushed about by 3D noise climbing through it, so the rope
// of fire has a body from every side and boils as it goes; clinging to a wall it's drawn up into a licking tongue. The
// fragment reads how square-on the surface is to the eye as how much fire is looked through there (thick and hot through
// its middle, thin, red and torn at its rim), and burns out dark at its edges as it ages. Soft where it meets a wall.
// INSTANCE_CUSTOM = (age 0..1, heat, seed, alpha). Its motion comes from the stream, not from flicker: nothing flashes.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_back, shadows_disabled, fog_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform float soft = 0.6;

varying vec4 v_custom;
varying vec3 v_obj;

float hash3(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float noise3(vec3 x) {
	vec3 i = floor(x), f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash3(i), hash3(i + vec3(1, 0, 0)), f.x), mix(hash3(i + vec3(0, 1, 0)), hash3(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash3(i + vec3(0, 0, 1)), hash3(i + vec3(1, 0, 1)), f.x), mix(hash3(i + vec3(0, 1, 1)), hash3(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

vec3 fire_ramp(float t) {
	vec3 c = mix(vec3(0.3, 0.025, 0.0), vec3(1.0, 0.26, 0.03), smoothstep(0.0, 0.35, t));
	c = mix(c, vec3(1.45, 0.62, 0.12), smoothstep(0.35, 0.7, t));
	c = mix(c, vec3(1.7, 1.05, 0.35), smoothstep(0.78, 1.0, t));
	return c;
}

void vertex() {
	v_custom = INSTANCE_CUSTOM;
	float seed = v_custom.z, age = v_custom.x;
	vec3 p = VERTEX;   // (the unit sphere, radius 0.5)
	vec3 n = normalize(p + vec3(1e-4));
	// boiling: the surface pushed in and out by noise rising through it
	float t = TIME * 1.6;
	float b = noise3(p * 3.2 + vec3(seed * 19.0, -t, seed * 7.0));
	float b2 = noise3(p * 6.5 + vec3(-seed * 11.0, -t * 1.7, 3.0));
	p += n * ((b - 0.5) * 0.34 + (b2 - 0.5) * 0.14);
	// the top drawn up into a lick (more as it ages and slows, the heat rising off it)
	float up = smoothstep(0.0, 0.5, p.y);
	p.y += up * (0.25 + 0.35 * age) * (0.6 + b);
	p.xz *= 1.0 - up * 0.35;
	VERTEX = p;
	v_obj = p;
}

void fragment() {
	float age = v_custom.x, heat = v_custom.y, seed = v_custom.z, a_mul = v_custom.w;
	float facing = clamp(abs(dot(normalize(NORMAL), normalize(VIEW))), 0.0, 1.0);
	float n1 = noise3(v_obj * 3.0 + vec3(seed * 9.0, -TIME * 1.9, 0.0));
	float n2 = noise3(v_obj * 6.0 + vec3(0.0, -TIME * 2.8, seed * 5.0));
	float shape = smoothstep(0.05, 0.7, facing + (n1 - 0.5) * 0.55 + (n2 - 0.5) * 0.25 - max(v_obj.y, 0.0) * 0.5);
	float t = clamp(shape * (0.45 + 0.6 * heat) + (n2 - 0.5) * 0.25, 0.0, 1.0) * (1.0 - age * 0.75);
	vec3 col = fire_ramp(t);
	float alpha = smoothstep(0.02, 0.4, shape) * (1.0 - smoothstep(0.75, 1.0, age));
	// burning out: its edges going dark, smoky
	col = mix(col, vec3(0.08, 0.03, 0.015), smoothstep(0.35, 1.0, age) * (1.0 - shape) * 0.9);
	alpha *= 0.6 + 0.4 * smoothstep(0.1, 0.6, shape + n1 * 0.3);
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 up = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	float scene_z = -up.z / up.w;
	alpha *= clamp((scene_z + VERTEX.z) / soft, 0.0, 1.0);
	ALBEDO = col;
	ALPHA = clamp(alpha * a_mul, 0.0, 1.0);
}
