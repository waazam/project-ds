// Overcast dusk sky, "Mountain View (PS2 Style)": a cool purple-grey dome,
// brighter at the horizon with a faint warm glow toward the (hidden) sun,
// broken cloud shapes, and three layers of distant mountain ridges that fade
// into the valley haze. Static (no TIME), so the radiance map is built once.
shader_type sky;

uniform vec3 zenith_color : source_color = vec3(0.13, 0.135, 0.16);
uniform vec3 horizon_color : source_color = vec3(0.37, 0.375, 0.4);
uniform vec3 glow_color : source_color = vec3(0.46, 0.38, 0.36);
uniform float glow_strength = 0.55;
uniform vec3 cloud_dark : source_color = vec3(0.14, 0.14, 0.165);
uniform vec3 cloud_light : source_color = vec3(0.4, 0.39, 0.41);
uniform float cloud_cover = 0.55;
uniform vec3 ridge_far : source_color = vec3(0.27, 0.275, 0.31);
uniform vec3 ridge_mid : source_color = vec3(0.21, 0.215, 0.25);
uniform vec3 ridge_near : source_color = vec3(0.15, 0.155, 0.185);
uniform vec3 haze_color : source_color = vec3(0.33, 0.335, 0.36);
uniform vec3 below_color : source_color = vec3(0.31, 0.32, 0.345);
/// Scales every ridge height (radians of elevation).
uniform float ridge_scale = 2.0;
/// World-space horizontal direction where the tallest peaks stand.
uniform vec2 peak_dir = vec2(0.0, -1.0);

float hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash(i), b = hash(i + vec2(1.0, 0.0)), c = hash(i + vec2(0.0, 1.0)), d = hash(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p, int oct) {
	float s = 0.0, a = 0.5, n = 0.0;
	for (int i = 0; i < oct; i++) {
		s += a * vnoise(p);
		n += a;
		a *= 0.5;
		p = p * 2.03 + vec2(1.7, 9.2);
	}
	return s / n;
}

// ridged noise around the horizon circle (seamless in azimuth)
float ridge(vec2 hdir, float freq, vec2 off, float seed) {
	vec2 q = hdir * freq + off;
	float s = 0.0, a = 0.6, n = 0.0;
	for (int i = 0; i < 5; i++) {
		float v = vnoise(q + seed);
		// broad, rounded crests from the big octaves; small octaves only roughen the skyline
		if (i < 2) v = 1.0 - pow(abs(v * 2.0 - 1.0), 1.4);
		s += a * v;
		n += a;
		a *= 0.42;
		q = q * 2.1 + vec2(3.1, 7.7);
	}
	return smoothstep(0.15, 0.95, s / n);
}

void sky() {
	vec3 d = normalize(EYEDIR);
	float el = asin(clamp(d.y, -1.0, 1.0));
	vec2 hdir = normalize(d.xz + vec2(1e-5));

	// dome
	float t = clamp(el / 0.9, 0.0, 1.0);
	vec3 col = mix(horizon_color, zenith_color, pow(t, 0.55));
	vec2 sun_h = normalize(LIGHT0_DIRECTION.xz + vec2(1e-5));
	float toward = max(dot(hdir, sun_h), 0.0);
	float glow = pow(toward, 3.0) * exp(-max(el, 0.0) * 5.0) * glow_strength;
	col = mix(col, glow_color, glow);

	// clouds: projected on a flat ceiling, soft-edged low-res shapes
	if (d.y > 0.0) {
		vec2 cp = d.xz / (d.y + 0.15) * 1.1;
		float cn = fbm(cp + vec2(3.1, 1.7), 5);
		float cm = smoothstep(1.0 - cloud_cover - 0.05, 1.0 - cloud_cover + 0.25, cn);
		float body = fbm(cp * 2.2 + vec2(8.0, 2.0), 3);
		vec3 cc = mix(cloud_dark, cloud_light, clamp(body * 0.9 + glow * 1.2 - (cn - 0.5) * 0.6, 0.0, 1.0));
		cm *= smoothstep(0.0, 0.1, d.y);
		col = mix(col, cc, cm * 0.85);
	}

	// distant ridges: far (tallest, palest) to near (lowest, darkest)
	float bias = mix(0.55, 1.35, smoothstep(-0.3, 1.0, dot(hdir, normalize(peak_dir))));
	float rf = (0.012 + 0.105 * ridge(hdir, 2.2, vec2(0.0), 11.0) * bias) * ridge_scale;
	float rm = (0.004 + 0.075 * ridge(hdir, 3.4, vec2(5.0, 2.0), 37.0) * bias) * ridge_scale;
	float rn = (-0.004 + 0.05 * ridge(hdir, 5.1, vec2(9.0, 4.0), 71.0) * mix(0.8, 1.1, bias)) * ridge_scale;
	if (el < rf) col = mix(ridge_far, haze_color, smoothstep(rf - 0.005, rf - 0.07, el) * 0.8);
	if (el < rm) col = mix(ridge_mid, haze_color, smoothstep(rm - 0.005, rm - 0.07, el) * 0.7);
	if (el < rn) col = mix(ridge_near, haze_color, smoothstep(rn - 0.01, rn - 0.08, el) * 0.6);
	// fog lying in the valleys, then the ground
	col = mix(col, below_color, smoothstep(0.012, -0.03, el));

	COLOR = col;
}
