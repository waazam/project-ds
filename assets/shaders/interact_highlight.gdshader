// Dim highlight for the interactable the player is looking at. Applied as a
// material_overlay on the object's meshes: a faint bone-coloured rim plus a
// barely-there fill, additive, so it reads in fog and darkness without glowing.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back, fog_disabled;

uniform vec3 tint : source_color = vec3(0.86, 0.82, 0.70);
uniform float fill = 0.05;
uniform float rim = 0.35;
uniform float strength : hint_range(0.0, 1.0) = 1.0;

void fragment() {
	float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.5);
	vec3 c = tint * (fill + rim * fres) * strength;
	ALBEDO = c;
}
