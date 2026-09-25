// Something going out of the world (Act 19's puzzle box): it burns away through a noise pattern, a
// warm glow at the edge of what's left. progress 0 = whole, 1 = gone.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec4 tint : source_color = vec4(1.0);
uniform float progress : hint_range(0.0, 1.0) = 0.0;
uniform vec4 edge_color : source_color = vec4(1.0, 0.85, 0.55, 1.0);
uniform float edge_width = 0.06;

varying vec3 v_obj;

void vertex() {
	v_obj = VERTEX;
}

void fragment() {
	vec3 c = texture(albedo_tex, UV).rgb * tint.rgb * COLOR.rgb;
	float n = texture(noise_tex, v_obj.xz * 6.0 + v_obj.y * 3.0).r * 0.7 + texture(noise_tex, UV * 2.0).r * 0.3;
	float t = progress * 1.15 - 0.05;
	if (n < t) discard;
	float edge = 1.0 - smoothstep(0.0, edge_width, n - t);
	ALBEDO = mix(c, edge_color.rgb, edge * step(0.001, progress));
	EMISSION = edge_color.rgb * edge * 3.0 * step(0.001, progress);
	ROUGHNESS = 0.6;
}
