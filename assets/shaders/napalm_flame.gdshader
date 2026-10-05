// The flamethrower's fire (Act 24; the owner: "a napalm stream of fire just like ww2 ... the flame needs to be really
// well done"): each glob of burning fuel in the stream drawn as a billboard of flame, its shape a noise-torn plume
// licking upward, its colour by its heat: a deep yellow core at the nozzle, orange as it flies and swells, dull red at
// its ragged edges as it burns out; the burnt-out globs rolling up as thick black smoke. Soft where it meets a wall or
// the floor (no hard line). All in one draw (a MultiMesh): per instance, INSTANCE_CUSTOM = (age 0..1, heat, seed, alpha);
// heat below zero is smoke. Its motion comes from the stream, not from flicker: nothing flashes.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform float soft = 0.6;

varying vec4 v_custom;

void vertex() {
	v_custom = INSTANCE_CUSTOM;
	// (turned to the eye and drawn out along its flight by the stream itself, per instance)
}

vec3 fire_ramp(float t) {
	// dull red, orange, a deep yellow at its hottest (over 1: it glows through the finish's halation; not white: a white-hot
	// core glared)
	vec3 c = mix(vec3(0.3, 0.025, 0.0), vec3(1.0, 0.26, 0.03), smoothstep(0.0, 0.35, t));
	c = mix(c, vec3(1.45, 0.62, 0.12), smoothstep(0.35, 0.7, t));
	c = mix(c, vec3(1.7, 1.05, 0.35), smoothstep(0.78, 1.0, t));
	return c;
}

void fragment() {
	float age = v_custom.x, heat = v_custom.y, seed = v_custom.z, a_mul = v_custom.w;
	// the sprite turned by its seed, so no two globs look alike
	float ang = seed * 6.2831;
	vec2 uv = UV - 0.5;
	uv = mat2(vec2(cos(ang), sin(ang)), vec2(-sin(ang), cos(ang))) * uv;
	float alpha;
	vec3 col;
	if (heat >= 0.0) {
		// a plume: rounder below, torn into tongues above, the noise climbing through it
		vec2 q = uv;
		q.y += 0.08;
		// (broad licks, not grain: drawn out and overlapping, fine noise turned the stream to orange gravel)
		float n1 = texture(noise_tex, vec2(q.x * 0.8 + seed * 7.0, q.y * 0.7 - TIME * 1.2 + seed * 3.0)).r;
		float n2 = texture(noise_tex, vec2(q.x * 1.7 - seed * 5.0, q.y * 1.5 - TIME * 2.0)).r;
		float r = length(q * vec2(1.0, 0.82 + 0.3 * smoothstep(0.0, 0.5, -q.y)));
		float shape = 1.0 - smoothstep(0.18, 0.5, r + (n1 - 0.5) * 0.34 + (n2 - 0.5) * 0.16 + max(0.0, -q.y) * 0.25);
		float t = clamp(shape * (0.45 + 0.6 * heat) + (n2 - 0.5) * 0.3, 0.0, 1.0) * (1.0 - age * 0.75);
		col = fire_ramp(t);
		alpha = smoothstep(0.02, 0.35, shape) * (1.0 - smoothstep(0.75, 1.0, age));
		// burning out: its edges going dark, smoky
		col = mix(col, vec3(0.08, 0.03, 0.015), smoothstep(0.35, 1.0, age) * (1.0 - shape) * 0.9);
		alpha *= 0.55 + 0.45 * smoothstep(0.1, 0.6, shape + n1 * 0.3);
	} else {
		// smoke: a soft, lumpy puff, black-brown, thinning as it rises and spreads
		float n = texture(noise_tex, uv * 1.4 + vec2(seed * 9.0, -TIME * 0.08)).r;
		float r = length(uv);
		float puff = 1.0 - smoothstep(0.2, 0.5, r + (n - 0.5) * 0.3);
		col = vec3(0.04, 0.035, 0.03) * (0.8 + 0.5 * n);
		alpha = puff * 0.75 * (1.0 - smoothstep(0.3, 1.0, age)) * smoothstep(0.0, 0.08, age);
	}
	// soft where it meets the cave
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 up = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	float scene_z = -up.z / up.w;
	alpha *= clamp((scene_z + VERTEX.z) / soft, 0.0, 1.0);
	ALBEDO = col;
	ALPHA = clamp(alpha * a_mul, 0.0, 1.0);
}
