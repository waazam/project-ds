shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded, fog_disabled;

// Act 6's menacing-forest overlay: a faint, slowly pulsing web of dark red
// veins laid over the ground once the player crosses the bridge. A separate
// transparent quad above the terrain, not a change to the terrain shader itself.
uniform vec4 vein_color : source_color = vec4(0.32, 0.04, 0.07, 1.0);
uniform float speed = 0.12;
uniform float scale = 3.2;
uniform float intensity : hint_range(0.0, 2.0) = 1.0;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(41.3, 289.1))) * 43758.5453);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	float a = hash(i);
	float b = hash(i + vec2(1.0, 0.0));
	float c = hash(i + vec2(0.0, 1.0));
	float d = hash(i + vec2(1.0, 1.0));
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

void fragment() {
	vec2 uv = UV * scale;
	float t = TIME * speed;
	float n1 = vnoise(uv + vec2(t, 0.0));
	float n2 = vnoise(uv * 1.7 - vec2(0.0, t * 0.6));
	float vein = smoothstep(0.46, 0.5, n1) - smoothstep(0.5, 0.54, n1);
	vein += (smoothstep(0.47, 0.5, n2) - smoothstep(0.5, 0.53, n2)) * 0.6;
	float pulse = 0.55 + 0.45 * sin(TIME * 1.1 + n1 * 6.0);
	ALBEDO = vein_color.rgb;
	ALPHA = clamp(vein * pulse * intensity, 0.0, 1.0);
}
