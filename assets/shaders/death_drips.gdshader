// (2026-10-22, the style pass) Dying: blood runs down the screen from the top as it goes black.
// (2026-10-31, the owner: "the background should remain black and not have the gray, the blood should start at the top and
// drip down at different paces per blood line, and there needs to be double the blood drips, also the balls (or bubbles)
// at the bottom of the drip line needs to be removed and the blood just needs to pool on the bottom of the screen ...
// like it is starting to flood the loading screen") Now: black behind it all (fading in with it); twice the runs (44 across),
// each leaving the film along the top at its own moment and running at its own pace, some crawling, some quick, each
// ending in a rounded tip (no bead); and what reaches the foot of the screen gathers there in a pool that rises, its
// surface rocking, flooding up the screen. Glossy (a glint off the crowns), darker where it's thick. t: seconds since.
shader_type canvas_item;

uniform float t = 0.0;
uniform float amount : hint_range(0.0, 1.0) = 1.0;

const vec3 RICH = vec3(0.5, 0.012, 0.03);
const vec3 THICK = vec3(0.13, 0.0, 0.008);
const float COLS = 44.0;

float hash(float n) { return fract(sin(n * 12.9898) * 43758.5453); }

// how far down the screen run i has got by now (0..1.3: past 1 it's into the pool)
float run_len(float i) {
	float delay = hash(i * 5.3) * 1.6;
	float speed = 0.08 + 0.55 * pow(hash(i), 1.6);           // most crawl, a few run quick
	float s = max(t - delay, 0.0) * speed;
	float reach = 0.55 + 0.85 * hash(i * 9.1);               // some give out partway, most make the floor
	return reach * (1.0 - exp(-s * 1.4)) * 1.35;
}

// the pool along the bottom: how high it's risen (screen heights), from when the first runs arrive
float pool_level() {
	return clamp((t - 1.6) * 0.045, 0.0, 0.32) * smoothstep(1.4, 2.4, t);
}

// the blood's height at p (x in screen heights, y 0..1 down)
float field(vec2 p, float aspect) {
	float h = 0.0;
	// the film along the top they leave from
	float film = 0.03 + 0.022 * sin(p.x * 23.0) * sin(p.x * 7.0 + 1.3) + 0.05 * min(t, 2.0) / 2.0;
	h = max(h, smoothstep(film, film - 0.03, p.y));
	float x = p.x / aspect * COLS;
	float id = floor(x);
	for (int k = -1; k <= 1; k++) {
		float i = id + float(k);
		float cx = (i + 0.2 + 0.6 * hash(i * 3.1)) / COLS * aspect;
		float w = 0.004 + 0.011 * hash(i * 7.7);
		float len = run_len(i);
		if (len <= 0.001) continue;
		if (p.y < len) {
			float k2 = p.y / max(len, 1e-3);
			// tapering toward its tip, wavering a little; the last bit rounded off (no bead)
			float ww = w * (1.0 - 0.35 * k2) * (0.9 + 0.1 * sin(p.y * 60.0 + i));
			float tip = smoothstep(len, len - ww * 2.2, p.y);
			ww *= sqrt(max(tip, 0.0));
			float dx = abs(p.x - cx) / max(ww, 1e-4);
			h = max(h, sqrt(max(1.0 - dx * dx, 0.0)) * 0.75);
		}
	}
	// the pool: its surface rocking where the runs pour in
	float lv = pool_level();
	if (lv > 0.0) {
		float surf = 1.0 - lv + 0.006 * sin(p.x * 31.0 + t * 2.1) + 0.004 * sin(p.x * 77.0 - t * 3.3);
		h = max(h, smoothstep(surf, surf + 0.025, p.y));
	}
	return h;
}

void fragment() {
	float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
	vec2 p = vec2(UV.x * aspect, UV.y);
	float h = field(p, aspect);
	// black behind it (no grey: the world's gone)
	float bg = smoothstep(0.0, 0.7, t) * amount;
	if (h <= 0.001) { COLOR = vec4(0.0, 0.0, 0.0, bg); }
	else {
		float e = 0.002;
		float hx = field(p + vec2(e, 0.0), aspect) - field(p - vec2(e, 0.0), aspect);
		float hy = field(p + vec2(0.0, e), aspect) - field(p - vec2(0.0, e), aspect);
		vec3 n = normalize(vec3(-hx, -hy, 2.0 * e * 6.0));
		float spec = pow(max(dot(n, normalize(vec3(-0.4, -0.7, 0.6))), 0.0), 30.0);
		vec3 col = mix(RICH, THICK, smoothstep(0.2, 1.0, h)) + vec3(1.0, 0.8, 0.75) * spec * 0.7;
		float a = smoothstep(0.0, 0.12, h) * amount;
		COLOR = vec4(col * a, max(a, bg));
	}
}
