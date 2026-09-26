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

void vertex() {
	if (sway) VERTEX = tree_wind(VERTEX, MODEL_MATRIX, TIME);
}

void fragment() {
	ALBEDO = texture(albedo_tex, UV).rgb * tint * COLOR.rgb;
	ROUGHNESS = roughness;
	SPECULAR = specular;
}
