// (the gushy-water pass, 2026-10-15) Where a pour hits the water: a churning ring of white water on a flat disc
// (UV 0..1 across the disc, its middle at 0.5). Rings of foam boil out from the middle and break up as they spread,
// the water under them heaves (vertex), and the whole of it swirls slowly. `viscous` 1 is blood landing in blood:
// no white, dark glossy rings rolling out instead.
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_never, specular_schlick_ggx;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 foam_color : source_color = vec3(0.7, 0.73, 0.7);
uniform vec3 water_color : source_color = vec3(0.08, 0.1, 0.07);
uniform float speed = 1.0;           // how fast the rings run out
uniform float strength : hint_range(0.0, 1.0) = 1.0;
uniform float heave = 0.04;          // metres the middle boils up
uniform float viscous : hint_range(0.0, 1.0) = 0.0;
uniform vec2 drift = vec2(0.0, 0.0); // a current carrying the foam off (UV a second)

varying float v_r;

void vertex() {
	vec2 d = UV - 0.5;
	v_r = length(d) * 2.0;
	float boil = textureLod(noise_tex, UV * 0.8 + vec2(TIME * 0.31, -TIME * 0.27), 0.0).r - 0.4;
	VERTEX.y += heave * strength * boil * (1.0 - smoothstep(0.0, 0.8, v_r)) * 2.0;
}

void fragment() {
	vec2 d = UV - 0.5;
	float r = length(d) * 2.0;
	float ang = atan(d.y, d.x);
	float sp = speed * (1.0 - 0.6 * viscous);
	// the rings running out from the middle, broken by noise swirled round it
	float sw = ang + TIME * 0.25 + r * 1.5;
	vec2 q = vec2(cos(sw), sin(sw)) * r * 0.9 + 0.5 - drift * TIME;
	float n1 = texture(noise_tex, q * 0.9 + vec2(0.0, TIME * 0.05)).r;
	float n2 = texture(noise_tex, q * 2.0 + vec2(0.3, -TIME * 0.11)).r;
	float ring = 0.5 + 0.5 * sin((r * 7.0 - TIME * sp * 6.0) + n1 * 4.0);
	float spread = 1.0 - smoothstep(0.35, 1.0, r);
	float boil = smoothstep(0.35, 0.0, r);
	float froth = clamp((ring * 0.6 + n2 * 0.6) * spread + boil * (0.6 + 0.6 * n1) - 0.45, 0.0, 1.0) * 1.8;
	froth = clamp(froth, 0.0, 1.0) * strength;
	// (water: always froth-coloured, its thin parts just fainter; a dark disc of "water" read as a stain on the floor)
	vec3 c = mix(foam_color * 0.6, foam_color, froth);
	// blood: glossy dark rolls, the crests a little lighter
	c = mix(c, water_color * (0.7 + 0.8 * ring * spread), viscous);
	ALBEDO = c;
	ROUGHNESS = mix(mix(0.15, 0.7, froth), 0.05, viscous);
	SPECULAR = mix(0.5, 0.9, viscous);
	float dx = texture(noise_tex, q * 2.0 + vec2(0.33, -TIME * 0.11)).r - n2;
	float dy = texture(noise_tex, q * 2.0 + vec2(0.3, -TIME * 0.11 + 0.03)).r - n2;
	NORMAL = normalize(NORMAL + (VIEW_MATRIX * vec4(dx * 4.0, 0.0, dy * 4.0, 0.0)).xyz * (0.5 + strength));
	ALPHA = clamp(mix(froth * 0.95, spread * 0.85 * strength, viscous) * (1.0 - smoothstep(0.75, 1.0, r)), 0.0, 1.0);
}
