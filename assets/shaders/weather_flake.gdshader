// A snowflake (or a raindrop's streak, a mote of dust, a spore): weather_billboard.gdshaderinc's card, lit (so in the
// dark the flakes show where a light falls on them: the lantern's glow round you, the moon on the open road) from
// both sides (a flake is thin: lit from behind it glows a little), with a breath of its own colour so the snow never
// goes wholly black. Cold and grey-blue, never white: no glare (the owner's rule).
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, skip_vertex_transform, specular_disabled, shadows_disabled;
#include "res://assets/shaders/weather_billboard.gdshaderinc"

uniform sampler2D atlas : source_color, filter_linear_mipmap, hint_default_white;
uniform vec3 tint : source_color = vec3(0.74, 0.77, 0.83);
uniform float opacity = 0.8;
/// Its own faint light (a fraction of the tint).
uniform float glow = 0.05;
/// The colour its glow takes: the scene's haze (Weather sets it from the fog), so the flakes stand just out of the
/// haze they fall through, at dusk and at night alike (bright only in a light: never a glare).
uniform vec3 glow_tint : source_color = vec3(0.3, 0.32, 0.36);
uniform float haze_lift = 0.0;
/// How much light passes through it from behind.
uniform float translucency = 0.5;

void vertex() {
	float fade;
	vec2 cell;
	weather_vertex(VERTEX, NORMAL, MODEL_MATRIX, VIEW_MATRIX, INV_VIEW_MATRIX, COLOR, INSTANCE_CUSTOM, fade, cell);
	v_fade = fade;
	v_cell = cell;
}

void fragment() {
	vec4 t = texture(atlas, weather_uv(UV, v_cell));
	ALBEDO = tint;
	ALPHA = t.a * opacity * v_fade;
	EMISSION = tint * glow + glow_tint * haze_lift;
	ROUGHNESS = 1.0;
}

void light() {
	float facing = dot(NORMAL, LIGHT);
	float lit = max(facing, 0.0) + translucency * max(-facing, 0.0);
	DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * (0.25 + 0.75 * lit) / 3.14159;
}
