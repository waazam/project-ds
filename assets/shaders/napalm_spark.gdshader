// The flamethrower's embers and burning droplets (2026-10-14d): a short glowing streak each (the stream turns the quad to
// the eye and draws it out along its flight), white-yellow when it's thrown off, cooling through orange to a dull red
// and out. Additive. INSTANCE_CUSTOM = (age 0..1, heat 0..1, seed, -).
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

varying vec4 v_custom;

void vertex() { v_custom = INSTANCE_CUSTOM; }

void fragment() {
	float age = v_custom.x, heat = v_custom.y, seed = v_custom.z;
	vec2 d = UV - 0.5;
	// a streak: long along x, its ends tapered
	float r = length(d * vec2(1.0, 2.6));
	float a = 1.0 - smoothstep(0.0, 0.5, r);
	a *= a;
	float t = clamp(heat * (1.0 - age), 0.0, 1.0);
	vec3 c = mix(vec3(0.5, 0.06, 0.0), vec3(1.4, 0.45, 0.06), smoothstep(0.1, 0.5, t));
	c = mix(c, vec3(2.2, 1.6, 0.8), smoothstep(0.6, 1.0, t));
	// a little twinkle as it tumbles (slow enough never to strobe)
	float tw = 0.8 + 0.2 * sin(TIME * 9.0 + seed * 40.0);
	ALBEDO = c * a * tw * (1.0 - smoothstep(0.7, 1.0, age)) * smoothstep(0.3, 0.9, -VERTEX.z);
}
