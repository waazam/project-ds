// Stream water ribbon: murky, opaque, a slow scrolling sheen. UV.x across, UV.y along (downstream).
shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 deep_color : source_color = vec3(0.045, 0.06, 0.055);
uniform vec3 foam_color : source_color = vec3(0.30, 0.33, 0.31);
uniform float flow_speed = 0.35;
uniform vec3 sky_tint : source_color = vec3(0.26, 0.3, 0.28);

void fragment() {
	vec2 uv = UV;
	float n1 = texture(noise_tex, vec2(uv.x * 0.45, uv.y * 0.35 - TIME * flow_speed)).r;
	float n2 = texture(noise_tex, vec2(uv.x * 0.8 + 0.37, uv.y * 0.7 - TIME * flow_speed * 1.6)).r;
	float n = n1 * 0.6 + n2 * 0.4;
	float streak = smoothstep(0.58, 0.78, n);
	ALBEDO = mix(deep_color, foam_color, streak * 0.55);
	float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	// dull sky sheen so the water reads as water under an overcast sky
	EMISSION = sky_tint * (0.08 + fres * 0.45) * (0.8 + 0.4 * n);
	ROUGHNESS = 0.18 + n * 0.25;
	SPECULAR = 0.6;
	// fake ripple normal from the noise
	float dx = texture(noise_tex, vec2(uv.x * 0.8 + 0.37 + 0.02, uv.y * 0.7 - TIME * flow_speed * 1.6)).r - n2;
	float dy = texture(noise_tex, vec2(uv.x * 0.8 + 0.37, uv.y * 0.7 - TIME * flow_speed * 1.6 + 0.02)).r - n2;
	NORMAL = normalize(NORMAL + (VIEW_MATRIX * vec4(dx * 3.0, 0.0, dy * 3.0, 0.0)).xyz);
}
