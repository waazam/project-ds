// (2026-10-22, the style pass) Blood on the lens: drops that hit the camera and run. Each drop is a round bead (refracting
// the picture behind it like a lens, dark red, a hot specular glint up and left) and the trail it leaves as it slides,
// thinner, fading. Drawn under the PS2 post, so it grains with the rest. StyleFx drives the drops (positions in 0..1 of
// the screen, y down).
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
// x, y, radius (in screen heights), alpha
uniform vec4 drops[24];
// x, top y, bottom y, width (screen heights) and its alpha packed in drops' order
uniform vec4 trails[24];
uniform float trail_alpha[24];
uniform int count = 0;

const vec3 RICH = vec3(0.52, 0.015, 0.03);
const vec3 THICK = vec3(0.16, 0.0, 0.01);

void fragment() {
	float aspect = SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x;
	vec2 uv = SCREEN_UV;
	vec2 p = vec2(uv.x * aspect, uv.y);
	float h = 0.0;       // the blood's height here (0 none .. 1 the bead's crown)
	vec2 grad = vec2(0.0);
	float a = 0.0;
	for (int i = 0; i < 24; i++) {
		if (i >= count) break;
		vec4 d = drops[i];
		if (d.w <= 0.001) continue;
		vec2 c = vec2(d.x * aspect, d.y);
		vec2 o = p - c;
		// a bead a little heavier at its foot (it hangs)
		o.y *= o.y > 0.0 ? 0.85 : 1.1;
		float r = d.z;
		float q = dot(o, o) / (r * r);
		if (q < 1.0) {
			float hh = sqrt(1.0 - q);
			if (hh > h) { h = hh; grad = o / r; }
			a = max(a, d.w * smoothstep(1.0, 0.85, q));
		}
		vec4 t = trails[i];
		float ta = trail_alpha[i];
		if (ta > 0.001 && uv.y > t.y && uv.y < t.z) {
			float k = (uv.y - t.y) / max(t.z - t.y, 1e-4);
			float w = t.w * (0.55 + 0.45 * k) * (0.85 + 0.15 * sin(uv.y * 90.0 + float(i)));
			float dx = abs(p.x - t.x * aspect) / max(w, 1e-4);
			if (dx < 1.0) {
				float hh = sqrt(1.0 - dx * dx) * 0.45;
				if (hh > h) { h = hh; grad = vec2((p.x - t.x * aspect) / w, 0.0); }
				a = max(a, ta * smoothstep(1.0, 0.7, dx));
			}
		}
	}
	if (a <= 0.001) { COLOR = vec4(0.0); }
	else {
		// the picture through it, bent (a lens), then stained
		vec2 bent = uv - grad * 0.035 * h;
		vec3 behind = texture(screen_tex, bent).rgb;
		vec3 blood = mix(RICH, THICK, smoothstep(0.1, 0.9, h));
		vec3 col = mix(behind * vec3(0.9, 0.25, 0.22), blood, 0.55 + 0.35 * h);
		// the glint (light from up and left) and a dark rim where it's thinnest
		vec3 n = normalize(vec3(-grad * h * 1.6, 1.0));
		float spec = pow(max(dot(n, normalize(vec3(-0.5, -0.6, 0.65))), 0.0), 38.0);
		col += vec3(1.0, 0.85, 0.8) * spec * 0.9;
		col *= 0.75 + 0.25 * smoothstep(0.0, 0.35, h);
		COLOR = vec4(col, a);
	}
}
