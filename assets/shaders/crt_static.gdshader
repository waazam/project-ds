// A CRT screen showing noisy static. `power` cuts it to black (screen off);
// `clarity` blends the noise toward a stepped silhouette pattern (used for
// the moment every screen in the room clears to show the same image).
shader_type spatial;
render_mode unshaded, cull_back;

uniform float power : hint_range(0.0, 1.0) = 1.0;
uniform float clarity : hint_range(0.0, 1.0) = 0.0;
uniform vec3 tint : source_color = vec3(0.72, 0.76, 0.8);
uniform float noise_speed = 13.0;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

void fragment() {
	vec2 uv = UV;
	float t = floor(TIME * noise_speed);
	float n = hash(uv * vec2(140.0, 100.0) + t * 13.7);
	float scan = 0.82 + 0.18 * sin(uv.y * 220.0);

	// A stepped, staircase-like silhouette the noise resolves into once clarity rises.
	float band = floor(uv.y * 6.0) * 0.16;
	float silhouette = step(fract(uv.x * 1.4 + band), 0.58);

	float img = mix(n, silhouette, clarity);
	vec3 c = tint * img * scan;
	ALBEDO = c * power;
	EMISSION = c * power * 1.7;
}
