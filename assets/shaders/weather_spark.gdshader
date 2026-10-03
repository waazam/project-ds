// An ember (the weather pass, 2026-10-03, for FireVfx): weather_billboard.gdshaderinc's card drawn out along its
// flight (a spark's short streak, not a square), glowing, adding its light. Its colour and fading come from the
// emitter's colour ramp (the particle's COLOR is its colour here, not a velocity: `velocity_from` points at its
// motion instead).
shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, unshaded, skip_vertex_transform, shadows_disabled;
#include "res://assets/shaders/weather_billboard.gdshaderinc"

uniform sampler2D atlas : source_color, filter_linear_mipmap, hint_default_white;
uniform float brightness = 2.2;

varying vec4 v_col;

void vertex() {
	v_col = COLOR;
	// (the process material aligns each ember's Y axis to its flight: that is the streak's way, its length its speed)
	vec3 flight = normalize(MODEL_MATRIX[1].xyz) * 3.0;
	float fade;
	vec2 cell;
	weather_vertex(VERTEX, NORMAL, MODEL_MATRIX, VIEW_MATRIX, INV_VIEW_MATRIX, vec4(flight, 1.0), vec4(0.0, 0.0, 14.0, 0.0), fade, cell);
	v_fade = fade;
	v_cell = cell;
}

void fragment() {
	float a = texture(atlas, weather_uv(UV, v_cell)).a;
	ALBEDO = v_col.rgb * brightness * a * v_col.a * v_fade;
}
