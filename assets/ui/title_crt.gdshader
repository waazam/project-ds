// The title screen's TV: an old tube playing a worn tape of the stairs.
// The picture (the stairs plus the VCR's on-screen text) comes from a 2D
// viewport; this adds everything that makes it an old tape on an old tube:
// washed-out colour, colour fringing, per-line jitter, a tracking band crawling
// down the picture, head-switching noise along the bottom, a slow rolling bar,
// scanlines, a bulged glass face, and (burst 0..1) the tape losing it
// completely: the picture rolls and tears into snow.
// glow: the tube's own brightness wobble (the menu drives the room light with
// the same value).
shader_type spatial;
render_mode unshaded, cull_back;

// Read raw (display-space) values: the grading below is done by eye in display space, and
// linearising first would crush the grey fog and the pale stairs into one flat grey.
uniform sampler2D picture : filter_linear, repeat_disable;
uniform float burst : hint_range(0.0, 1.0) = 0.0;
uniform float glow = 1.0;
uniform float brightness = 1.15;

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

vec3 tape(vec2 uv) {
	// Colour fringing: red and blue a hair either side of green.
	float r = texture(picture, uv + vec2(0.0035, 0.0)).r;
	float g = texture(picture, uv).g;
	float b = texture(picture, uv - vec2(0.0035, 0.0)).b;
	vec3 c = vec3(r, g, b);
	// The tape is a bright, contrasty copy of a dim, foggy shot: lift it so the stairs read.
	c = clamp((c - 0.17) * 2.0, 0.0, 1.0);
	c = pow(c, vec3(0.85));
	float l = dot(c, vec3(0.299, 0.587, 0.114));
	// Washed-out, a little cold: most of the colour gone.
	return mix(vec3(l), c, 0.3) * vec3(0.93, 0.98, 1.05);
}

void fragment() {
	vec2 c = UV - 0.5;
	vec2 uv = 0.5 + c * (1.0 + 0.14 * dot(c, c));
	float t = TIME;
	float frame = floor(t * 24.0);

	// Losing it: the frame rolls and the lines tear sideways.
	uv.y = fract(uv.y + burst * (0.3 * sin(t * 3.0) + frame * 0.07));
	float line = floor(uv.y * 240.0);
	float jitter = (hash(vec2(line, frame)) - 0.5) * 0.004;
	jitter += (hash(vec2(line * 0.37, frame)) - 0.5) * 0.12 * burst;

	// The tracking band: a soft strip crawling down, smearing the lines inside it.
	float bandY = fract(t * 0.045);
	float band = smoothstep(0.05, 0.0, abs(uv.y - bandY));
	jitter += band * (hash(vec2(line, frame * 1.3)) - 0.3) * 0.03;

	// Head-switching noise: the bottom few lines of a VHS frame, bent and noisy.
	float head = smoothstep(0.955, 0.985, uv.y);
	jitter += head * (0.02 + 0.03 * hash(vec2(frame, 1.0)));

	vec2 puv = clamp(vec2(uv.x + jitter, uv.y), 0.002, 0.998);
	vec3 col = tape(puv);

	float snow = hash(floor(UV * vec2(220.0, 180.0)) + frame * 1.7);
	col += (snow - 0.5) * (0.05 + band * 0.25 + head * 0.35);
	col = mix(col, vec3(snow) * vec3(0.82, 0.86, 0.9), clamp(burst * 1.2, 0.0, 1.0));
	col += band * 0.06;

	// Rolling bar, scanlines, the tube's own darker corners.
	float roll = 0.9 + 0.1 * sin((uv.y + t * 0.16) * 6.2831);
	float scan = 0.82 + 0.18 * sin(UV.y * 200.0 * 3.14159);
	float vig = smoothstep(0.8, 0.3, length(c * vec2(1.0, 1.15)));
	col *= roll * scan * (0.45 + 0.55 * vig) * glow;

	// Glass: a faint grey sheen top-left, even where the picture is black.
	vec3 glass = vec3(0.012, 0.014, 0.016) + vec3(0.035) * (1.0 - smoothstep(0.0, 0.4, length(c - vec2(-0.22, -0.24))));
	// Everything above is display-space; the spatial shader writes linear colour.
	ALBEDO = glass + pow(max(col, vec3(0.0)) * brightness, vec3(2.2));
}
