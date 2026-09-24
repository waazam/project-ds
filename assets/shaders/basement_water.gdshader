// Act 13's flooded basement: brown-green standing water with a slow scum of ripples, which the drain
// turns into a whirlpool (`swirl` 0..1): the surface winds round the drain faster toward the middle
// and darkens into a funnel. Mesh local XZ is the room's floor plan; `drain` is in the same space.
shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec2 drain = vec2(0.0);
uniform float swirl : hint_range(0.0, 1.0) = 0.0;
uniform vec3 water_color : source_color = vec3(0.08, 0.1, 0.07);
uniform vec3 scum_color : source_color = vec3(0.22, 0.22, 0.15);

varying vec2 v_local;

void vertex() {
	v_local = VERTEX.xz;
	// the funnel: the surface sags toward the drain as it spins
	float r = length(VERTEX.xz - drain);
	VERTEX.y -= swirl * 0.35 * exp(-r * r / 1.8);
}

void fragment() {
	vec2 d = v_local - drain;
	float r = length(d);
	float ang = atan(d.y, d.x) + swirl * TIME * (2.5 / (r + 0.35));
	vec2 p = drain + vec2(cos(ang), sin(ang)) * r;
	float t = TIME * 0.03;
	float n1 = texture(noise_tex, p * 0.35 + vec2(t, -t * 0.7)).r;
	float n2 = texture(noise_tex, p * 1.1 + vec2(-t * 1.3, t)).r;
	float scum = smoothstep(0.55, 0.8, n1 * 0.7 + n2 * 0.3);
	vec3 c = mix(water_color, scum_color, scum * 0.6);
	// spiralling foam lines when it drains
	float lines = smoothstep(0.75, 1.0, sin(ang * 5.0 + r * 3.0)) * swirl * smoothstep(3.5, 0.4, r);
	c = mix(c, vec3(0.5, 0.48, 0.4), lines * 0.5);
	c *= 1.0 - swirl * 0.7 * exp(-r * r / 0.6);
	float dx = texture(noise_tex, p * 1.1 + vec2(-t * 1.3 + 0.02, t)).r - n2;
	float dy = texture(noise_tex, p * 1.1 + vec2(-t * 1.3, t + 0.02)).r - n2;
	NORMAL = normalize(NORMAL + (VIEW_MATRIX * vec4(dx * 1.5, 0.0, dy * 1.5, 0.0)).xyz);
	ALBEDO = c;
	ROUGHNESS = mix(0.08, 0.6, scum);
	SPECULAR = 0.55;
}
