// Act 22's trees (2026-09-30): the bare winter broadleaves' bark (Poly Haven's, made small and cold by
// tools/Textures/make_winter_forest.py), with the snow where snow settles: along the tops of the limbs, and plastered
// up the windward side of the trunks (the owner's references). Near the lodge, the ice: a dull cold glaze, set per
// tree through the MultiMesh's custom data (r: 0 none .. 1 caked). Never a glare.
shader_type spatial;
render_mode diffuse_lambert, specular_schlick_ggx;

uniform sampler2D bark_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D bark_nor : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D snow_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec3 wind_dir = vec3(0.55, 0.0, 0.83);
uniform float snow_amount = 1.0;
uniform vec3 snow_color : source_color = vec3(0.7, 0.73, 0.8);

varying vec3 wnorm;
varying vec3 wpos;
varying float ice;

void vertex() {
	wnorm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	ice = clamp(INSTANCE_CUSTOM.r, 0.0, 1.0);
}

void fragment() {
	vec3 bark = texture(bark_tex, UV).rgb * COLOR.rgb;
	float n = texture(snow_tex, wpos.xz * 0.7 + vec2(wpos.y * 0.37)).r;
	// on top of the limbs
	float top = smoothstep(0.3, 0.72, wnorm.y + (n - 0.55) * 0.55);
	// blown against the windward side of the trunks (not their undersides)
	float side = smoothstep(0.5, 0.9, dot(wnorm, normalize(wind_dir)) + (n - 0.55) * 0.6) * (1.0 - smoothstep(-0.1, -0.5, wnorm.y)) * 0.85;
	float m = clamp(max(top, side), 0.0, 1.0) * snow_amount;
	vec3 snow = snow_color * (0.86 + 0.18 * n);
	vec3 c = mix(bark, snow, m);
	// the ice: bark and snow alike dulled under a cold glaze
	c = mix(c, c * vec3(0.72, 0.82, 0.96) + vec3(0.04, 0.06, 0.09), ice * 0.75);
	ALBEDO = c;
	NORMAL_MAP = mix(texture(bark_nor, UV).rgb, vec3(0.5, 0.5, 1.0), m * 0.8);
	NORMAL_MAP_DEPTH = 0.9;
	ROUGHNESS = mix(0.95, 0.42, ice);
	SPECULAR = mix(0.18, 0.4, ice);
}
