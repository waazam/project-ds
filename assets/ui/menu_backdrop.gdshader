// Main-menu backdrop: a pre-rendered frame of the bunker's TV room (the desk
// monitor and the wall of screens all showing the stairs), drifting slowly in
// toward the desk monitor, with a faint haze and the screens' glow flickering, then the same late-PS2 finish as the
// game: grade, grain, 4x4 ordered dither, vignette. Runs at 640x360.
shader_type canvas_item;

uniform sampler2D backdrop : filter_linear, repeat_disable;
uniform bool has_backdrop = true;
uniform float drift_seconds = 70.0;     // one slow push-in, then back
uniform float zoom_amount = 0.075;
uniform vec2 focus = vec2(0.5, 0.6);   // where the push-in heads (the desk monitor)
uniform vec3 fog_color = vec3(0.357, 0.380, 0.439);  // website --fog #5b6170
uniform float fog_near = 0.0;    // indoors: no fog banks crossing, just a little haze
uniform float fog_far = 0.035;
uniform float flicker = 0.05;    // the CRTs' uneven glow
uniform float levels = 40.0;
uniform float grain = 0.03;
uniform float vignette = 0.55;

const float BAYER[16] = float[16](
	0.0, 8.0, 2.0, 10.0,
	12.0, 4.0, 14.0, 6.0,
	3.0, 11.0, 1.0, 9.0,
	15.0, 7.0, 13.0, 5.0);

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

float noise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}

float fbm(vec2 p) {
	float v = 0.0, a = 0.5;
	for (int i = 0; i < 4; i++) { v += a * noise(p); p = p * 2.03 + 11.7; a *= 0.5; }
	return v;
}

void fragment() {
	vec2 uv = UV;
	vec2 aspect = vec2(16.0 / 9.0, 1.0);

	// Slow push toward the stairs and a faint sway, eased so it never visibly turns around.
	float phase = 0.5 - 0.5 * cos(TIME / drift_seconds * 6.28318);
	float zoom = 1.0 + zoom_amount * phase;
	vec2 sway = vec2(sin(TIME * 0.043) * 0.006, sin(TIME * 0.031) * 0.003);
	vec2 buv = (uv - focus) / zoom + focus + sway;

	vec3 c;
	if (has_backdrop) {
		c = texture(backdrop, buv).rgb;
	} else {
		// No capture yet: the website's night gradient.
		c = mix(vec3(0.102, 0.118, 0.145), vec3(0.043, 0.051, 0.063), smoothstep(0.0, 1.0, uv.y));
	}

	// Two fog layers: a far, slow, wide band and a nearer, faster, patchier one.
	vec2 p = uv * aspect;
	float far = fbm(p * 2.2 + vec2(TIME * 0.012, TIME * 0.002));
	float nearf = fbm(p * 4.5 + vec2(-TIME * 0.03, TIME * 0.004) + far);
	float band = smoothstep(0.25, 0.62, uv.y) * (1.0 - smoothstep(0.78, 1.0, uv.y) * 0.6);
	c = mix(c, fog_color, clamp(far * fog_far * (0.6 + band), 0.0, 1.0));
	c = mix(c, fog_color * 0.9, clamp(smoothstep(0.45, 0.85, nearf) * fog_near * band, 0.0, 1.0));

	// The screens' glow wavers: a slow unsteady swell with the odd quick dip, like old tubes.
	float fl = 0.6 * sin(TIME * 2.3) + 0.4 * sin(TIME * 5.7 + 1.3) + step(0.985, hash(vec2(floor(TIME * 12.0), 3.1))) * -2.5;
	c *= 1.0 + flicker * fl * 0.5;

	// Grade toward the game's cool blue-violet shadows.
	float luma = dot(c, vec3(0.299, 0.587, 0.114));
	c = mix(vec3(luma), c, 0.85);
	c = (c - 0.5) * 1.12 + 0.5;
	c *= 0.92;
	c = max(c, vec3(0.0));
	c += vec3(0.009, 0.010, 0.019) * (1.0 - luma);

	// Vignette, heavier at the bottom where the menu text sits.
	vec2 d = uv - vec2(0.5, 0.45);
	c *= 1.0 - vignette * dot(d, d) * 1.7;

	float t = floor(TIME * 24.0);
	c += (hash(FRAGCOORD.xy + t * 17.13) - 0.5) * grain;
	ivec2 q = ivec2(mod(FRAGCOORD.xy, 4.0));
	float b = (BAYER[q.y * 4 + q.x] + 0.5) / 16.0 - 0.5;
	c = floor(c * levels + 0.5 + b) / levels;

	COLOR = vec4(clamp(c, 0.0, 1.0), 1.0);
}
