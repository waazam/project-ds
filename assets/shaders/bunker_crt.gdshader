// A CRT tube face for the bunker's screen room. The screen mesh is a slightly
// bulged grid with UV 0..1 across the visible picture.
// - picture: what the tube shows (surveillance of the woods, the smouldering
//   cabin, the stairs). With use_atlas the texture is a 2x2 atlas and COLOR.g
//   picks the cell, so one material can show different cameras.
// - clarity: 0 = pure snow, 1 = a clean (still grainy) picture. At low clarity
//   the picture is also blurred (lower mip), so "fuzzy, then clears" reads.
// - fire: animates the picture's alpha channel as flickering flame.
// - power: 1 = on; tweening to 0 collapses the picture to a bright line, then
//   black glass, like a real tube switching off (and back the other way).
// - COLOR.r: per-screen seed so screens don't snow in lockstep.
shader_type spatial;
render_mode unshaded, cull_back;

uniform float power : hint_range(0.0, 1.0) = 1.0;
uniform float clarity : hint_range(0.0, 1.0) = 1.0;
uniform sampler2D picture : source_color, filter_linear_mipmap, repeat_disable;
uniform float use_atlas = 0.0;
uniform vec3 tint : source_color = vec3(0.78, 0.82, 0.86);
uniform float fire = 0.0;
uniform float brightness = 1.25;
uniform float noise_amount = 0.1;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	float seed = COLOR.r;
	vec2 c = UV - 0.5;
	vec2 uv = 0.5 + c * (1.0 + 0.16 * dot(c, c));
	float squash = max(power, 0.012);
	float widen = mix(0.35, 1.0, smoothstep(0.0, 0.3, power));
	vec2 suv = vec2(0.5 + (uv.x - 0.5) / widen, 0.5 + (uv.y - 0.5) / squash);
	float inside = step(abs(suv.x - 0.5), 0.5) * step(abs(suv.y - 0.5), 0.5);

	float t = floor(TIME * 12.0);
	float n = hash(floor(suv * vec2(40.0, 30.0)) + vec2(t * 7.13, t * 3.71) + seed * 97.0);
	vec2 puv = suv;
	puv.x += (hash(vec2(floor(suv.y * 30.0), t + seed * 13.0)) - 0.5) * 0.008 * (1.5 - clarity);
	puv = clamp(puv, 0.004, 0.996);
	if (use_atlas > 0.5) {
		float cell = floor(COLOR.g * 3.999);
		puv = vec2(mod(cell, 2.0), floor(cell / 2.0)) * 0.5 + puv * 0.5;
	}
	vec4 pic = textureLod(picture, puv, (1.0 - clarity) * 3.5);
	float fl = hash(floor(suv * vec2(24.0, 18.0)) + floor(TIME * 9.0) + seed);
	vec3 p = pic.rgb * (1.0 + pic.a * fire * (fl * 1.4 - 0.35));
	vec3 snow = vec3(n) * tint;
	vec3 col = mix(snow, p + (n - 0.5) * noise_amount, smoothstep(0.0, 0.85, clarity));

	float roll = 0.86 + 0.14 * sin((suv.y + TIME * 0.19 + seed * 3.0) * 6.2831);
	float scan = 0.85 + 0.15 * sin(suv.y * 22.0 * 6.2831);
	float vig = smoothstep(0.66, 0.28, length(c * vec2(1.0, 1.2)));
	col *= roll * scan * (0.5 + 0.5 * vig);

	float on = inside * step(0.002, power);
	float flash = 1.0 + (1.0 - power) * 2.5;
	vec3 glass = vec3(0.010, 0.012, 0.014) + vec3(0.03) * (1.0 - smoothstep(0.0, 0.35, length(c - vec2(-0.2, -0.22))));
	ALBEDO = glass + col * on * brightness * flash;
}
