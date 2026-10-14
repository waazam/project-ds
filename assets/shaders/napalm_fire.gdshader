// The flamethrower's rope of burning fuel (2026-10-14d, the graphics pass: "flamethrower scene needs some work it doesn't
// look great"). Replaces napalm_volume.gdshader, which drew each glob as a solid, saturated yellow ball two metres
// across: overlapping, the stream read as one flat yellow blob, and close to a wall it filled the screen.
// Modelled on footage of the M2-2 and of napalm: a narrow, bright, oily jet out of the nozzle that thickens as it flies
// into a rolling, torn, orange body, wrapped in and trailed by sooty black where the fuel burns rich; the hottest part a
// yellow-white core only where it is thick and young, deep red and then black at its torn edges as it ages.
// Each glob is a small sphere (radius 0.5) stretched along its flight by the stream (basis x along the flight).
// Premultiplied alpha: the fire adds its light, the soot darkens what's behind it; so the fire glows over the cave and
// the smoke round it occludes, and many overlapping globs saturate to a hot core, not to a white sheet.
// INSTANCE_CUSTOM = (age 0..1, heat 0..1, seed, kind: 0 the stream, 1 a splash's bloom).
shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_draw_never, cull_back, shadows_disabled, fog_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform float soft = 0.35;
uniform float brightness = 1.0;

varying vec4 v_custom;
varying vec3 v_obj;

float hash3(vec3 p) { p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float noise3(vec3 x) {
	vec3 i = floor(x), f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash3(i), hash3(i + vec3(1, 0, 0)), f.x), mix(hash3(i + vec3(0, 1, 0)), hash3(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash3(i + vec3(0, 0, 1)), hash3(i + vec3(1, 0, 1)), f.x), mix(hash3(i + vec3(0, 1, 1)), hash3(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}
float fbm(vec3 p) { return noise3(p) * 0.55 + noise3(p * 2.03 + 11.0) * 0.3 + noise3(p * 4.1 + 23.0) * 0.15; }

// colour temperature: soot, deep red, orange, yellow, a yellow-white only at the hottest (over 1: it carries through the
// finish's halation)
vec3 fire_ramp(float t) {
	vec3 c = mix(vec3(0.05, 0.008, 0.0), vec3(0.42, 0.05, 0.005), smoothstep(0.0, 0.22, t));
	c = mix(c, vec3(1.05, 0.28, 0.03), smoothstep(0.2, 0.48, t));
	c = mix(c, vec3(1.5, 0.72, 0.14), smoothstep(0.46, 0.75, t));
	c = mix(c, vec3(1.9, 1.45, 0.75), smoothstep(0.86, 1.0, t));
	return c;
}

void vertex() {
	v_custom = INSTANCE_CUSTOM;
	float seed = v_custom.z, age = v_custom.x;
	vec3 p = VERTEX;
	vec3 n = normalize(p + vec3(1e-4));
	float t = TIME * 2.2;
	// boiling: the surface pushed about by noise rolling back through it (more as it ages and billows)
	float b = noise3(p * 3.0 + vec3(seed * 19.0 + t, -t * 0.6, seed * 7.0));
	float b2 = noise3(p * 6.5 + vec3(-seed * 11.0, -t * 1.3, 3.0));
	float boil = 0.12 + 0.3 * age;
	p += n * ((b - 0.5) * boil * 1.6 + (b2 - 0.5) * boil * 0.7);
	// the hot gas lifting off its top as it slows
	float up = smoothstep(0.0, 0.5, p.y);
	p.y += up * age * 0.35 * (0.5 + b);
	VERTEX = p;
	v_obj = p;
}

void fragment() {
	float age = v_custom.x, heat = v_custom.y, seed = v_custom.z, kind = v_custom.w;
	float facing = clamp(abs(dot(normalize(NORMAL), normalize(VIEW))), 0.0, 1.0);
	vec3 q = v_obj * 2.6 + vec3(seed * 9.0 - TIME * 1.7, -TIME * 2.3, seed * 4.0);
	float n = fbm(q);
	float n2 = noise3(v_obj * 7.0 + vec3(0.0, -TIME * 3.4, seed * 5.0));
	// how much fire the eye looks through: thick through the middle, torn away at the rim (more torn with age)
	float thick = facing * facing * (1.15 - 0.25 * age) + (n - 0.5) * (0.6 + 0.6 * age) + (n2 - 0.5) * 0.18 - max(v_obj.y, 0.0) * 0.35 * age;
	float body = smoothstep(0.08 + 0.3 * age, 0.75, thick);
	// pockets where it burns rich and cool (dark red, sooty) rolling through the body: no glob an even glow
	float rich = smoothstep(0.42, 0.68, fbm(v_obj * 1.7 + vec3(seed * 13.0, -TIME * 1.2, TIME * 0.6)));
	// the burning: hot where thick and young; the rich fuel's soot gathering at its torn rim and on top as it ages
	float temp = clamp(body * (0.3 + 0.7 * heat) * mix(1.0, 0.45, rich * (0.3 + 0.7 * age)) + (n2 - 0.5) * 0.25, 0.0, 1.0) * (1.0 - age * 0.6);
	vec3 fire = fire_ramp(temp) * brightness;
	float fire_a = smoothstep(0.0, 0.4, body) * (1.0 - smoothstep(0.65, 1.0, age)) * mix(1.0, 0.55, rich * age);
	float soot = smoothstep(0.2, 0.85, age) * smoothstep(0.0, 0.3, thick + 0.15) * (1.0 - body * 0.7) * (0.55 + 0.45 * smoothstep(-0.1, 0.4, v_obj.y));
	soot = max(soot, rich * smoothstep(0.35, 0.8, age) * body * 0.6);
	soot *= 1.0 - smoothstep(0.85, 1.0, age);
	soot *= kind > 0.5 ? 0.5 : 1.0;
	// soft against the cave
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 up = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	float scene_z = -up.z / up.w;
	float s = clamp((scene_z + VERTEX.z) / soft, 0.0, 1.0);
	// never a sheet of fire over the eye: thinned to nothing in the last metre before it
	float near = smoothstep(0.35, 1.4, -VERTEX.z);
	float k = s * near;
	float fa = fire_a * k;
	float sa = soot * 0.8 * k * (1.0 - fa);
	ALBEDO = fire * fa * 0.85;
	ALPHA = clamp(fa * 0.6 + sa, 0.0, 1.0);
}
