// The lodge's roofs (2026-10-10c, the owner: "the roof of the lodge also looks bland give it some color like a rustic royal
// blue thats been faded"): faded royal-blue standing-seam sheet (Poly Haven's box profile sheet, repainted:
// ph_lodge_roof_blue), with the snow lying on it in drifts: heaviest along the eaves where it's slid down and piled, in
// long tongues down between the seams, thin and patchy higher up where the wind scours it, the blue showing through. All
// in the lodge's own space (no seams between the roof's faces).
shader_type spatial;
render_mode blend_mix, cull_back;

uniform sampler2D roof_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D roof_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D roof_rough : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform sampler2D snow_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D snow_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform float eave_y = 8.5;     // world height of the eaves (lodge-local is fine: the lodge sits level)
uniform float peak_y = 26.0;
uniform float cover = 0.55;     // how much snow, overall
uniform vec3 tint : source_color = vec3(1.0);

varying vec3 v_world;

void vertex() {
	v_world = VERTEX;   // (the lodge's own space: its roofs are committed under it, level)
}

void fragment() {
	vec4 roof = texture(roof_albedo, UV);
	// the snow: noise in world space, drifting along the slope; more at the eaves, scoured toward the peak
	float h = clamp((v_world.y - eave_y) / max(peak_y - eave_y, 0.01), 0.0, 1.0);
	vec2 wp = v_world.xz * 0.11 + vec2(0.0, v_world.y * 0.18);
	float n = texture(noise_tex, wp).r * 0.65 + texture(noise_tex, wp * 3.1 + 0.37).r * 0.35;
	// tongues down between the seams (the sheet's ribs run along v)
	float tongues = 0.5 + 0.5 * sin(UV.x * 6.2831 * 8.0 + n * 3.0);
	float want = cover + (1.0 - smoothstep(0.0, 0.18, h)) * 0.55 - h * 0.35 + (tongues - 0.5) * 0.12;
	float snow = smoothstep(want - 0.06, want + 0.06, 1.0 - n);
	snow = 1.0 - snow;
	vec3 s = texture(snow_albedo, v_world.xz * 0.3).rgb * 0.95;
	ALBEDO = mix(roof.rgb * tint, s, snow);
	ROUGHNESS = mix(texture(roof_rough, UV).r * 0.9, 0.92, snow);
	METALLIC = mix(0.25, 0.0, snow);
	NORMAL_MAP = mix(texture(roof_normal, UV).rgb, texture(snow_normal, v_world.xz * 0.3).rgb, snow);
	NORMAL_MAP_DEPTH = 1.0;
}
