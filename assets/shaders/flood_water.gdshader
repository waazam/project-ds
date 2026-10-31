// Act 13's Room 2 flood: the basement water's surface (lake water, then blood), but see-through, so
// the cryptex on the table stays readable once the flood is over it. `opacity` sets how much.
// (the gushy-water pass, 2026-10-15) `inflow` / `inflow_strength`: where the torrent lands, rings heave out across the
// surface from it (vertex and normal) and a current spreads from it; `viscous` 0..1 is the blood: slower, heavier,
// glossier, darker and less see-through (still enough to read the cryptex).
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_never, specular_schlick_ggx;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec2 drain = vec2(0.0);
uniform float swirl : hint_range(0.0, 1.0) = 0.0;
uniform vec3 water_color : source_color = vec3(0.08, 0.1, 0.07);
uniform vec3 scum_color : source_color = vec3(0.22, 0.22, 0.15);
uniform float opacity : hint_range(0.0, 1.0) = 0.6;
uniform vec2 inflow = vec2(99.0, 99.0);
uniform float inflow_strength : hint_range(0.0, 1.0) = 0.0;
uniform float viscous : hint_range(0.0, 1.0) = 0.0;
/// (2026-10-31, the owner: the blood "a little too water ... make it feel more viscous") 0..1: the blood thick: its surface
/// barely moving, slow heavy folds instead of ripples, a sticky skin with a sheen, and nearly opaque.
uniform float thick : hint_range(0.0, 1.0) = 0.0;

varying vec2 v_local;

float rings(vec2 xz) {
	float r = length(xz - inflow);
	float sp = mix(mix(5.0, 2.0, viscous), 0.7, thick);
	return sin(r * 5.0 - TIME * sp) * exp(-r * 0.45) * inflow_strength;
}

void vertex() {
	v_local = VERTEX.xz;
	// the funnel: the surface sags toward the drain as it spins
	float r = length(VERTEX.xz - drain);
	VERTEX.y -= swirl * 0.35 * exp(-r * r / 1.8);
	VERTEX.y += rings(VERTEX.xz) * mix(mix(0.03, 0.045, viscous), 0.02, thick);
	// thick: slow, heavy folds rolling across it
	VERTEX.y += thick * 0.025 * sin(VERTEX.x * 1.3 + TIME * 0.35) * sin(VERTEX.z * 1.1 - TIME * 0.28);
}

void fragment() {
	vec2 d = v_local - drain;
	float r = length(d);
	float slow = (1.0 - 0.6 * viscous) * (1.0 - 0.75 * thick);
	float ang = atan(d.y, d.x) + swirl * TIME * (2.5 / (r + 0.35));
	vec2 p = drain + vec2(cos(ang), sin(ang)) * r;
	// a current spreading out from where the torrent lands
	vec2 fromIn = v_local - inflow;
	float ri = length(fromIn);
	p -= fromIn / max(ri, 0.3) * TIME * 0.25 * inflow_strength * slow * exp(-ri * 0.2);
	float t = TIME * 0.03 * slow;
	float n1 = texture(noise_tex, p * 0.35 + vec2(t, -t * 0.7)).r;
	float n2 = texture(noise_tex, p * 1.1 + vec2(-t * 1.3, t)).r;
	float scum = smoothstep(0.55, 0.8, n1 * 0.7 + n2 * 0.3);
	vec3 c = mix(water_color, scum_color, scum * 0.6);
	// spiralling foam lines when it drains
	float lines = smoothstep(0.75, 1.0, sin(ang * 5.0 + r * 3.0)) * swirl * smoothstep(3.5, 0.4, r);
	c = mix(c, vec3(0.5, 0.48, 0.4), lines * 0.5);
	c *= 1.0 - swirl * 0.7 * exp(-r * r / 0.6);
	// froth stirred up round the torrent's foot (water only)
	float ring = rings(v_local);
	c = mix(c, vec3(0.45, 0.48, 0.44), smoothstep(0.3, 0.9, ring + n2 * 0.4) * exp(-ri * 0.6) * (1.0 - viscous) * inflow_strength);
	// blood: darker in the troughs, glossier
	c *= mix(1.0, 0.75 + 0.3 * n1, viscous);
	float dx = texture(noise_tex, p * 1.1 + vec2(-t * 1.3 + 0.02, t)).r - n2;
	float dy = texture(noise_tex, p * 1.1 + vec2(-t * 1.3, t + 0.02)).r - n2;
	vec2 rg = fromIn / max(ri, 0.05) * (rings(v_local + fromIn / max(ri, 0.05) * 0.05) - ring) * 8.0;
	NORMAL = normalize(NORMAL + (VIEW_MATRIX * vec4(dx * 1.5 + rg.x, 0.0, dy * 1.5 + rg.y, 0.0)).xyz);
	ALBEDO = c;
	// thick: a skin on it, dark and sticky, the light catching in a wet sheen along its folds
	float skin = smoothstep(0.35, 0.75, n1 * 0.6 + n2 * 0.4);
	c = mix(c, c * 0.55, thick * skin * 0.6);
	ROUGHNESS = mix(mix(mix(0.08, 0.6, scum), 0.04, viscous), mix(0.02, 0.25, skin), thick);
	SPECULAR = mix(mix(0.55, 0.85, viscous), 0.95, thick);
	CLEARCOAT = thick;
	CLEARCOAT_ROUGHNESS = 0.05;
	ALPHA = clamp(opacity + scum * 0.15 + viscous * 0.08 + thick * 0.3, 0.0, 1.0);
}
