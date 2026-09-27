shader_type spatial;
render_mode unshaded, cull_disabled;
#include "res://assets/shaders/dais_ring.gdshaderinc"

void fragment() {
	float band = 1.0 - pow(abs(UV.y * 2.0 - 1.0), 4.0);
	ALBEDO = ring_colour(UV.x) * (0.55 + 0.9 * band);
}
