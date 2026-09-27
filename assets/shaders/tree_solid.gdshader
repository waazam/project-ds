// Tree trunks, stubs, broken tops and deciduous leaf masses: a plain opaque textured surface (vertex
// colour times the texture, as the StandardMaterials they replace) that sways with the woods' breeze
// (tree_wind.gdshaderinc), together with the boughs on the same tree.
shader_type spatial;
render_mode diffuse_lambert, specular_schlick_ggx;
#include "res://assets/shaders/tree_wind.gdshaderinc"

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec3 tint : source_color = vec3(1.0);
uniform float roughness = 1.0;
uniform float specular = 0.25;
uniform bool sway = true;
global uniform float winter;

float frost_noise(vec3 p) { return fract(sin(dot(floor(p * 7.0), vec3(12.9898, 78.233, 37.719))) * 43758.5453); }

void vertex() {
	if (sway) VERTEX = tree_wind(VERTEX, MODEL_MATRIX, TIME);
}

void fragment() {
	ALBEDO = texture(albedo_tex, UV).rgb * tint * COLOR.rgb;
	if (winter > 0.001) {
		// Act 21 on, it is winter: snow on every upward face, a grey hoarfrost over the bark
		vec3 wn = (INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz;
		vec3 wp = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
		float up = smoothstep(0.15, 0.65, wn.y);
		float speck = frost_noise(wp);
		float snow = winter * clamp(up * (0.75 + 0.25 * speck), 0.0, 1.0);
		vec3 hoar = mix(ALBEDO, vec3(0.62, 0.64, 0.68), 0.35 * winter * (0.6 + 0.4 * speck));
		ALBEDO = mix(hoar, vec3(0.88, 0.91, 0.96), snow);
	}
	ROUGHNESS = roughness;
	SPECULAR = specular;
}
