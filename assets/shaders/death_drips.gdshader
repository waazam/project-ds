// (2026-10-22, the style pass) Dying: blood runs down the screen from the top as it goes black. Thick columns that
// slow as they run (viscous: quick, then crawling), a bead swelling at each one's foot, a film along the top that
// they hang from; glossy (a glint off each run's crown), darker where it's thick. t: seconds since it began.
shader_type canvas_item;

uniform float t = 0.0;
uniform float amount : hint_range(0.0, 1.0) = 1.0;

const vec3 RICH = vec3(0.5, 0.012, 0.03);
const vec3 THICK = vec3(0.13, 0.0, 0.008);

float hash(float n) { return fract(sin(n * 12.9898) * 43758.5453); }

// the blood's height at p (x in screen heights, y 0..1 down)
float field(vec2 p, float aspect) {
	float h = 0.0;
	// the film along the top
	float film = 0.035 + 0.025 * sin(p.x * 23.0) * sin(p.x * 7.0 + 1.3) + 0.06 * min(t, 2.0) / 2.0;
	h = max(h, smoothstep(film, film - 0.03, p.y));
	float cols = 22.0;
	float x = p.x / aspect * cols;
	float id = floor(x);
	for (int k = -1; k <= 1; k++) {
		float i = id + float(k);
		float cx = (i + 0.2 + 0.6 * hash(i * 3.1)) / cols * aspect;
		float w = (0.006 + 0.014 * hash(i * 7.7));
		float speed = 0.25 + 0.6 * hash(i);
		float delay = hash(i * 5.3) * 0.7;
		float run = max(t - delay, 0.0) * speed;
		float len = run / (1.0 + run * 0.9) * (0.6 + 0.9 * hash(i * 9.1));
		// the body: tapering a little toward its foot, wavering
		if (p.y < len) {
			float k2 = p.y / max(len, 1e-3);
			float ww = w * (1.0 - 0.3 * k2) * (0.9 + 0.1 * sin(p.y * 60.0 + i));
			float dx = abs(p.x - cx) / ww;
			h = max(h, sqrt(max(1.0 - dx * dx, 0.0)) * 0.75);
		}
		// the bead at its foot, swelling as it runs
		vec2 o = p - vec2(cx, len);
		float r = w * (1.5 + 0.5 * min(run, 1.0));
		float q = dot(o, o) / (r * r);
		h = max(h, sqrt(max(1.0 - q, 0.0)));
	}
	return h;
}

void fragment() {
	float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
	vec2 p = vec2(UV.x * aspect, UV.y);
	float h = field(p, aspect);
	if (h <= 0.001) { COLOR = vec4(0.0); }
	else {
		float e = 0.002;
		float hx = field(p + vec2(e, 0.0), aspect) - field(p - vec2(e, 0.0), aspect);
		float hy = field(p + vec2(0.0, e), aspect) - field(p - vec2(0.0, e), aspect);
		vec3 n = normalize(vec3(-hx, -hy, 2.0 * e * 6.0));
		float spec = pow(max(dot(n, normalize(vec3(-0.4, -0.7, 0.6))), 0.0), 30.0);
		vec3 col = mix(RICH, THICK, smoothstep(0.2, 1.0, h)) + vec3(1.0, 0.8, 0.75) * spec * 0.7;
		COLOR = vec4(col, smoothstep(0.0, 0.12, h) * amount);
	}
}
