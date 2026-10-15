// Alpha-scissor foliage cards (grass tufts, ferns, conifer boughs). Gentle wind
// sway weighted by UV.y (0 = tip of card, 1 = rooted). Normals bent upward so
// crossed cards shade softly. Alpha is boosted with distance (mip level) so
// cards don't thin out to nothing far away.
shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_disabled;
#include "res://assets/shaders/tree_wind.gdshaderinc"

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_disable;
/// A leaf's micro-surface (DetailKit's foliage), faint.
uniform sampler2D detail_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float detail_tile = 2.0;
uniform vec3 tint : source_color = vec3(1.0);
uniform float alpha_cut = 0.45;
uniform float sway = 0.05;
uniform float sway_speed = 1.3;
uniform float normal_up = 0.7;
uniform float mip_boost = 0.35;
uniform vec2 tex_size = vec2(64.0, 64.0);
/// Extra darkening of the back (under) side of cards.
uniform float back_shade = 0.0;
/// (2026-10-13, the trees "like paper") depth in a crown: the ambient falls off toward a card's root (UV.y = 1, in by
/// the trunk, under the boughs above) so the tiers read as layered masses, not flat cut-outs one tone through.
uniform float inner_ao = 0.0;
/// Sun through the needles (seen from beneath against a lit sky, the boughs glow a little instead of going to black).
uniform float translucency = 0.0;
/// (2026-10-15, the owner: "some of the trees still have invisible lines around them, like they look pieced together")
/// A flat card seen edge-on is a hairline: a straight dashed line off the crown. The card thins out as it turns edge-on
/// (its true, unbent face against the view), gone below this facing.
uniform float edge_fade = 0.22;
global uniform float winter;

void vertex() {
	float w = 1.0 - UV.y;
	vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float t = TIME * sway_speed + wp.x * 0.35 + wp.z * 0.27;
	VERTEX.x += sin(t) * sway * w;
	VERTEX.z += cos(t * 0.8) * sway * 0.6 * w;
	// the whole tree's lean in the breeze (boughs move with their trunk)
	VERTEX = tree_wind(VERTEX, MODEL_MATRIX, TIME);
	NORMAL = normalize(mix(NORMAL, vec3(0.0, 1.0, 0.0), normal_up));
}

void fragment() {
	vec4 c = texture(albedo_tex, UV);
	vec2 d = UV * tex_size;
	float lod = 0.5 * log2(max(dot(dFdx(d), dFdx(d)), dot(dFdy(d), dFdy(d))));
	float a = c.a * (1.0 + max(lod, 0.0) * mip_boost);
	if (edge_fade > 0.0) {
		vec3 gn = normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
		a *= smoothstep(edge_fade * 0.35, edge_fade, abs(dot(gn, VIEW)));
	}
	float back = FRONT_FACING ? 1.0 : 1.0 - back_shade;
	ALBEDO = c.rgb * tint * COLOR.rgb * back * mix(1.0, texture(detail_tex, UV * detail_tile).r, 0.8);
	if (winter > 0.001) {
		// winter: snow lying along the tops of the boughs, frost on the rest of the needles
		vec3 wp = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
		float n = fract(sin(dot(floor(wp * 5.0), vec3(12.9898, 78.233, 37.719))) * 43758.5453);
		float top = FRONT_FACING ? smoothstep(0.35, 0.95, 1.0 - UV.y * 0.6 + 0.25 * n) : 0.15;
		ALBEDO = mix(mix(ALBEDO, vec3(0.5, 0.54, 0.56), 0.3 * winter), vec3(0.86, 0.9, 0.95), winter * top * 0.8);
	}
	ALPHA = a;
	ALPHA_SCISSOR_THRESHOLD = alpha_cut;
	ROUGHNESS = 1.0;
	if (inner_ao > 0.0) {
		AO = 1.0 - inner_ao * smoothstep(0.15, 1.0, UV.y);
		AO_LIGHT_AFFECT = 0.25;
	}
	if (translucency > 0.0) BACKLIGHT = ALBEDO * translucency;
}
