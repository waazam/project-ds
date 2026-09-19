// Stream water ribbon: dark, opaque, with a faint scrolling ripple sheen and a
// few white-water flecks toward the banks. UV.x across, UV.y along (downstream).
shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 deep_color : source_color = vec3(0.025, 0.03, 0.03);
uniform vec3 foam_color : source_color = vec3(0.36, 0.38, 0.38);
uniform float flow_speed = 0.35;
uniform vec3 sky_tint : source_color = vec3(0.12, 0.13, 0.145);
uniform float foam_amount = 0.5;

void fragment() {
	vec2 uv = UV;
	float t = TIME * flow_speed;
	float n1 = texture(noise_tex, vec2(uv.x * 0.45, uv.y * 0.35 - t)).r;
	float n2 = texture(noise_tex, vec2(uv.x * 0.8 + 0.37, uv.y * 0.7 - t * 1.6)).r;
	float n3 = texture(noise_tex, vec2(uv.x * 2.1 + 0.11, uv.y * 1.9 - t * 2.3)).r;
	float n = n1 * 0.6 + n2 * 0.4;
	// faint darker/lighter flow streaks
	float streak = smoothstep(0.55, 0.8, n);
	vec3 c = deep_color * (0.8 + 0.5 * streak);
	// white-water flecks, mostly near the banks (where the rocks are)
	float edge = 1.0 - smoothstep(0.12, 0.38, min(uv.x, 1.0 - uv.x));
	float fleck = smoothstep(0.7, 0.84, n3 * 0.7 + n * 0.3 + edge * 0.18 * foam_amount);
	c = mix(c, foam_color, fleck * (0.35 + 0.5 * edge) * foam_amount * 1.4);
	ALBEDO = c;
	float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);
	// dull overcast sheen, kept low so the stream reads dark, not as a pale sheet
	EMISSION = sky_tint * (0.03 + fres * 0.2) * (0.7 + 0.6 * n2);
	ROUGHNESS = 0.3 + n * 0.3;
	SPECULAR = 0.35;
	// ripple normal from the noise
	float dx = texture(noise_tex, vec2(uv.x * 0.8 + 0.37 + 0.02, uv.y * 0.7 - t * 1.6)).r - n2;
	float dy = texture(noise_tex, vec2(uv.x * 0.8 + 0.37, uv.y * 0.7 - t * 1.6 + 0.02)).r - n2;
	NORMAL = normalize(NORMAL + (VIEW_MATRIX * vec4(dx * 2.5, 0.0, dy * 2.5, 0.0)).xyz);
}
