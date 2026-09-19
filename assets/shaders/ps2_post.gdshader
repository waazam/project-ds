// Full-screen "late PS2" finish: mild grade, 16-bit-style ordered dither,
// animated grain, vignette. Runs at the 640x360 internal resolution, so every
// effect lands on the chunky pixel grid.
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform float levels = 40.0;            // colour steps per channel after dither
uniform float dither_strength = 1.0;
uniform float grain = 0.035;
uniform float vignette = 0.38;
uniform float saturation = 0.95;
uniform vec3 shadow_tint = vec3(0.009, 0.010, 0.019);
uniform float contrast = 1.08;

const float BAYER[16] = float[16](
	0.0, 8.0, 2.0, 10.0,
	12.0, 4.0, 14.0, 6.0,
	3.0, 11.0, 1.0, 9.0,
	15.0, 7.0, 13.0, 5.0);

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	vec3 c = texture(screen_tex, SCREEN_UV).rgb;

	// Grade: pull saturation, add contrast, and lift blacks toward a cool blue-violet.
	float luma = dot(c, vec3(0.299, 0.587, 0.114));
	c = mix(vec3(luma), c, saturation);
	c = (c - 0.5) * contrast + 0.5;
	c = max(c, vec3(0.0)) + shadow_tint * (1.0 - luma);

	// Vignette.
	vec2 d = SCREEN_UV - 0.5;
	c *= 1.0 - vignette * dot(d, d) * 1.6;

	// Grain, re-rolled ~24 times a second, like film.
	float t = floor(TIME * 24.0);
	c += (hash(FRAGCOORD.xy + t * 17.13) - 0.5) * grain;

	// Ordered dither and quantise.
	ivec2 p = ivec2(mod(FRAGCOORD.xy, 4.0));
	float b = (BAYER[p.y * 4 + p.x] + 0.5) / 16.0 - 0.5;
	c = floor(c * levels + 0.5 + b * dither_strength) / levels;

	COLOR = vec4(clamp(c, 0.0, 1.0), 1.0);
}
