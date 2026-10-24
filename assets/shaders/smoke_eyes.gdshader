// (2026-10-24, the cabin fire) Smoke in the eyes: standing up into the smoke layer, the picture blurs and greys toward a
// warm brown, the edges close in and swim a little. amount 0..1 (set from the fire's exposure, eased).
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float amount : hint_range(0.0, 1.0) = 0.0;
uniform float time_s = 0.0;

void fragment() {
	vec2 uv = SCREEN_UV;
	// the eyes watering: the picture swims a little
	uv += vec2(sin(uv.y * 9.0 + time_s * 1.7), cos(uv.x * 7.0 + time_s * 1.3)) * 0.004 * amount;
	float r = 0.012 * amount;
	vec3 c = vec3(0.0);
	float w = 0.0;
	for (int i = -2; i <= 2; i++) {
		for (int j = -2; j <= 2; j++) {
			vec2 o = vec2(float(i), float(j)) * r * 0.5;
			float k = 1.0 / (1.0 + float(i * i + j * j));
			c += textureLod(screen_tex, uv + o, 1.5 * amount).rgb * k;
			w += k;
		}
	}
	c /= w;
	float l = dot(c, vec3(0.3, 0.59, 0.11));
	vec3 smoke = mix(vec3(l), vec3(0.42, 0.33, 0.24) * (0.4 + l), 0.55);
	c = mix(c, smoke, 0.6 * amount);
	vec2 d = SCREEN_UV - 0.5;
	c *= 1.0 - dot(d, d) * 2.2 * amount;
	COLOR = vec4(c, amount > 0.001 ? 1.0 : 0.0);
}
