// The stalker's skin. Matte near-black, lit and fogged like everything else,
// with PS2-style screen-door transparency: `visibility` below 1 punches out a
// growing share of pixels in a 4x4 ordered pattern, so the figure dissolves
// into the fog instead of fading like glass.
shader_type spatial;
render_mode cull_back, diffuse_lambert, specular_disabled;

uniform vec3 albedo : source_color = vec3(0.03, 0.03, 0.035);
uniform float visibility : hint_range(0.0, 1.0) = 1.0;

const float BAYER[16] = float[16](
	0.0, 8.0, 2.0, 10.0,
	12.0, 4.0, 14.0, 6.0,
	3.0, 11.0, 1.0, 9.0,
	15.0, 7.0, 13.0, 5.0);

void fragment() {
	ivec2 p = ivec2(mod(FRAGCOORD.xy, 4.0));
	float threshold = (BAYER[p.y * 4 + p.x] + 0.5) / 16.0;
	if (visibility < threshold) discard;
	ALBEDO = albedo;
	ROUGHNESS = 1.0;
}
