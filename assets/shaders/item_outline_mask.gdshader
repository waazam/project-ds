// The first half of a pickup's white outline (ItemOutline): drawn as the item's material_overlay, it marks the
// item's own visible pixels in the stencil buffer (where it passes the depth test, so a wall in front marks nothing),
// and the outline pass (its next_pass, item_outline.gdshader) is then drawn only outside them. It adds nothing to the
// colour unless the item is under the crosshair: then it's the old focus highlight (interact_highlight.gdshader's dim
// bone rim and faint fill), so a pickup keeps that cue too.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;
stencil_mode write, compare_always, 64;

uniform vec3 tint : source_color = vec3(0.86, 0.82, 0.70);
uniform float fill = 0.05;
uniform float rim = 0.35;
uniform float strength : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	float fres = pow(1.0 - clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0), 2.5);
	ALBEDO = tint * (fill + rim * fres) * strength;
}
