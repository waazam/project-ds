// Act 13's flooded basement: brown-green standing water with a slow scum of ripples, which the drain
// turns into a whirlpool (`swirl` 0..1): the surface winds round the drain faster toward the middle
// and darkens into a funnel. Mesh local XZ is the room's floor plan; `drain` is in the same space.
// (the gushy-water pass, 2026-10-15) Also the sewer's running water and the pit's blood:
//  - `flow` (local metres a second): a current. The scum drifts with it, flow-aligned streaks are combed through it and
//    small ripples travel along it (vertex), so the sewer reads as running, not standing;
//  - `viscous` 0..1: blood. Everything slows, the surface heaves in slow heavy swells, it goes glossy (low roughness,
//    strong highlights that slide over the swells), darker in its troughs and clotting in dark patches; the drain's
//    spiral lines are dark red rolls, not pale foam.
shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec2 drain = vec2(0.0);
uniform float swirl : hint_range(0.0, 1.0) = 0.0;
uniform vec3 water_color : source_color = vec3(0.08, 0.1, 0.07);
uniform vec3 scum_color : source_color = vec3(0.22, 0.22, 0.15);
uniform vec2 flow = vec2(0.0);
uniform float viscous : hint_range(0.0, 1.0) = 0.0;

varying vec2 v_local;
varying float v_swell;

void vertex() {
	v_local = VERTEX.xz;
	// the funnel: the surface sags toward the drain as it spins
	float r = length(VERTEX.xz - drain);
	VERTEX.y -= swirl * 0.35 * exp(-r * r / 1.8);
	// a current's ripples, running along it
	float fl = length(flow);
	if (fl > 0.001) {
		vec2 fd = flow / fl;
		float along = dot(VERTEX.xz, fd), acr = dot(VERTEX.xz, vec2(-fd.y, fd.x));
		VERTEX.y += 0.011 * sin(along * 4.3 - TIME * fl * 11.0 + sin(acr * 2.7) * 1.6) + 0.007 * sin(along * 1.9 - TIME * fl * 5.0 + acr * 2.3);
	}
	// blood: slow heavy swells
	float sa = VERTEX.x * 1.0 + TIME * 0.45, sb = VERTEX.z * 0.8 - TIME * 0.36;
	v_swell = sin(sa) * sin(sb);
	VERTEX.y += viscous * 0.06 * v_swell;
	// the swells tilt the surface, so the lamps' highlights roll over them
	NORMAL = normalize(NORMAL + vec3(-viscous * 0.06 * 1.0 * cos(sa) * sin(sb), 0.0, -viscous * 0.06 * 0.8 * sin(sa) * cos(sb)) * 1.3);
}

void fragment() {
	vec2 d = v_local - drain;
	float r = length(d);
	float slow = 1.0 - 0.65 * viscous;
	float ang = atan(d.y, d.x) + swirl * TIME * slow * (2.5 / (r + 0.35));
	vec2 p = drain + vec2(cos(ang), sin(ang)) * r - flow * TIME;
	float t = TIME * 0.03 * slow;
	float n1 = texture(noise_tex, p * 0.35 + vec2(t, -t * 0.7)).r;
	float n2 = texture(noise_tex, p * 1.1 + vec2(-t * 1.3, t)).r;
	float scum = smoothstep(0.55, 0.8, n1 * 0.7 + n2 * 0.3);
	vec3 c = mix(water_color, scum_color, scum * 0.6);
	// the current's streaks: combed out along it, sliding with it
	float fl = length(flow);
	if (fl > 0.001) {
		vec2 fd = flow / fl;
		vec2 q = vec2(dot(v_local, vec2(-fd.y, fd.x)) * 1.1, dot(v_local, fd) * 0.22 - TIME * fl * 0.3);
		float streak = smoothstep(0.58, 0.82, texture(noise_tex, q).r * 0.7 + n2 * 0.3);
		c = mix(c, scum_color * 1.35, streak * clamp(fl * 2.5, 0.0, 1.0) * 0.55);
	}
	// spiralling foam lines when it drains (blood: dark red rolls)
	float lines = smoothstep(0.75, 1.0, sin(ang * 5.0 + r * 3.0)) * swirl * smoothstep(3.5, 0.4, r);
	c = mix(c, mix(vec3(0.5, 0.48, 0.4), scum_color * 1.6, viscous), lines * 0.5);
	c *= 1.0 - swirl * 0.7 * exp(-r * r / 0.6);
	// blood: darker in the swells' troughs and in clots
	float clot = smoothstep(0.62, 0.8, texture(noise_tex, p * 0.6 + vec2(0.41, 0.17) + t * 0.5).r);
	c *= mix(1.0, (0.9 + 0.3 * v_swell) * (1.0 - 0.3 * clot), viscous);
	float dx = texture(noise_tex, p * 1.1 + vec2(-t * 1.3 + 0.02, t)).r - n2;
	float dy = texture(noise_tex, p * 1.1 + vec2(-t * 1.3, t + 0.02)).r - n2;
	float bump = mix(1.5, 1.3, viscous) + clamp(fl * 3.0, 0.0, 1.5);
	NORMAL = normalize(NORMAL + (VIEW_MATRIX * vec4(dx * bump, 0.0, dy * bump, 0.0)).xyz);
	ALBEDO = c;
	ROUGHNESS = mix(mix(0.08, 0.6, scum), mix(0.1, 0.22, clot), viscous);   // (blood: broad soft highlights rolling on the swells, not pin glints)
	SPECULAR = mix(0.55, 0.85, viscous);
}
