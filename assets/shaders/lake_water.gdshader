// Still lake water: gentle multi-directional ripples at rest (never a one-way flow, unlike the
// creek's terrain_water.gdshader). wave_intensity (0..1) drives the creature's breach: faster,
// bigger ripples and pale whitecaps bleeding in. UV is the lake plane's own world-scaled axes.
shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 deep_color : source_color = vec3(0.03, 0.05, 0.055);
uniform vec3 foam_color : source_color = vec3(0.55, 0.58, 0.58);
uniform vec3 sky_tint : source_color = vec3(0.35, 0.32, 0.28);
uniform float ripple_speed = 0.06;
uniform float wave_intensity : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec2 uv = UV;
	float t = TIME * ripple_speed * (1.0 + wave_intensity * 5.0);
	// Three drifting layers at different scales/directions so the surface never reads as flowing one way.
	float n1 = texture(noise_tex, uv * 0.5 + vec2(t, t * 0.6)).r;
	float n2 = texture(noise_tex, uv * 1.3 + vec2(-t * 0.7, t * 0.9)).r;
	float n3 = texture(noise_tex, uv * 3.1 + vec2(t * 1.4, -t * 1.1)).r;
	float n = n1 * 0.5 + n2 * 0.35 + n3 * 0.15;

	vec3 c = deep_color * (0.85 + 0.3 * n);
	// Whitecaps only appear once wave_intensity rises; the threshold sharpens with it.
	float cap = smoothstep(0.62 - wave_intensity * 0.28, 0.8 - wave_intensity * 0.3, n2 * 0.6 + n3 * 0.4);
	c = mix(c, foam_color, cap * wave_intensity * 0.9);

	ALBEDO = c;
	float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);
	EMISSION = sky_tint * (0.04 + fres * 0.22) * (0.7 + 0.6 * n1);
	ROUGHNESS = 0.15 + wave_intensity * 0.35;
	SPECULAR = 0.6;
	float amp = 1.5 + wave_intensity * 6.0;
	float dx = texture(noise_tex, uv * 1.3 + vec2(-t * 0.7 + 0.02, t * 0.9)).r - n2;
	float dy = texture(noise_tex, uv * 1.3 + vec2(-t * 0.7, t * 0.9 + 0.02)).r - n2;
	NORMAL = normalize(NORMAL + (VIEW_MATRIX * vec4(dx * amp, 0.0, dy * amp, 0.0)).xyz);
}
