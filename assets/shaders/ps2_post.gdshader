// Full-screen "late PS2 horror" finish (Silent Hill 2/3, Fatal Frame): a sickly split-tone grade
// (murky green-teal shadows, warm dirty highlights), a soft halation bleeding off anything bright,
// a little lens softness and colour fringing toward the edges, faint static scanlines, 16-bit-style
// ordered dither, animated grain and a vignette. Runs at the 640x360 internal resolution, so every
// effect lands on the chunky pixel grid. Nothing here flashes or strobes: the grain is fine and low,
// the scanlines are static, and the glow only follows what is already on screen.
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform sampler2D screen_soft : hint_screen_texture, filter_linear;
uniform float levels = 40.0;            // colour steps per channel after dither
uniform float dither_strength = 1.0;
uniform float grain = 0.035;
uniform float vignette = 0.38;
uniform float saturation = 0.95;
uniform vec3 shadow_tint = vec3(0.009, 0.010, 0.019);
uniform float contrast = 1.08;
/// The split-tone: shadows pulled toward a murky green-teal, highlights toward a dirty warm.
uniform vec3 shadow_tone = vec3(0.92, 1.0, 0.97);
uniform vec3 highlight_tone = vec3(1.04, 1.0, 0.92);
/// Halation: how much of the soft, blurred brights bleeds back over the image.
uniform float halation = 0.22;
uniform float halation_threshold = 0.55;
/// Lens softness and colour fringing toward the edges of the frame.
uniform float edge_soft = 0.35;
uniform float fringe = 0.0022;
uniform float scanlines = 0.035;

const float BAYER[16] = float[16](
	0.0, 8.0, 2.0, 10.0,
	12.0, 4.0, 14.0, 6.0,
	3.0, 11.0, 1.0, 9.0,
	15.0, 7.0, 13.0, 5.0);

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	vec2 uv = SCREEN_UV;
	vec2 d = uv - 0.5;
	float edge = dot(d, d) * 4.0;   // 0 in the middle, ~1 in the corners

	// the image, a hair fringed toward the edges (red out, blue in)
	vec3 c;
	c.r = texture(screen_tex, uv + d * fringe * edge * 4.0).r;
	c.g = texture(screen_tex, uv).g;
	c.b = texture(screen_tex, uv - d * fringe * edge * 4.0).b;
	// the lens goes soft toward the edges (a small tap blur: the screen has no mip levels to borrow)
	vec2 px = SCREEN_PIXEL_SIZE;
	vec3 soft1 = (texture(screen_soft, uv + px * vec2(1.5, 0.5)).rgb + texture(screen_soft, uv + px * vec2(-1.5, -0.5)).rgb
		+ texture(screen_soft, uv + px * vec2(-0.5, 1.5)).rgb + texture(screen_soft, uv + px * vec2(0.5, -1.5)).rgb) * 0.25;
	c = mix(c, soft1, clamp(edge * edge_soft, 0.0, 0.6));

	// halation: the brights, blurred wide (a ring of taps), bleed back over the image (warm)
	vec3 glow = vec3(0.0);
	for (int i = 0; i < 12; i++) {
		float a = float(i) * 0.5236 + 0.26;
		float r = (i % 2 == 0) ? 7.0 : 14.0;
		glow += texture(screen_soft, uv + vec2(cos(a), sin(a)) * px * r).rgb;
	}
	glow /= 12.0;
	float gl = dot(glow, vec3(0.299, 0.587, 0.114));
	c += max(glow - vec3(halation_threshold), vec3(0.0)) * halation * vec3(1.1, 0.95, 0.85) * smoothstep(halation_threshold, 1.0, gl + 0.3);

	// grade: pull saturation, add contrast, split-tone, and lift blacks toward a cool blue-violet
	float luma = dot(c, vec3(0.299, 0.587, 0.114));
	c = mix(vec3(luma), c, saturation);
	c = (c - 0.5) * contrast + 0.5;
	c *= mix(shadow_tone, highlight_tone, smoothstep(0.1, 0.7, luma));
	c = max(c, vec3(0.0)) + shadow_tint * (1.0 - luma);

	// vignette
	c *= 1.0 - vignette * dot(d, d) * 1.6;

	// faint static scanlines (every other line of the 360: the PS2's interlaced look, without the flicker)
	c *= 1.0 - scanlines * step(1.0, mod(FRAGCOORD.y, 2.0));

	// grain, re-rolled ~24 times a second, like film
	float t = floor(TIME * 24.0);
	c += (hash(FRAGCOORD.xy + t * 17.13) - 0.5) * grain;

	// ordered dither and quantise
	ivec2 p = ivec2(mod(FRAGCOORD.xy, 4.0));
	float b = (BAYER[p.y * 4 + p.x] + 0.5) / 16.0 - 0.5;
	c = floor(c * levels + 0.5 + b * dither_strength) / levels;

	COLOR = vec4(clamp(c, 0.0, 1.0), 1.0);
}
