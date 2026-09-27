shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
#include "res://assets/shaders/dais_ring.gdshaderinc"

// the glow of the ring on the floor round it: strongest at the inner edge (UV.y 0), gone at the outer
uniform float strength = 0.55;

void fragment() {
	float f = pow(1.0 - clamp(UV.y, 0.0, 1.0), 2.2);
	ALBEDO = ring_colour(UV.x) * f * strength;
}
