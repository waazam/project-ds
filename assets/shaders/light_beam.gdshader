// A visible shaft of light through dusty air (Act 18's teal spotlight): brightest at the lamp,
// fading along its length, soft at the edges, with slow drifting dust in it. Drawn on a cone mesh
// whose UV.y runs 0 at the lamp to 1 at the far end.
shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, unshaded, shadows_disabled;

uniform vec4 beam_color : source_color = vec4(0.3, 1.0, 0.9, 1.0);
uniform float strength = 0.35;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;

void fragment() {
	// soft edge: faces seen edge-on fade out, faces seen square-on stay
	float edge = pow(clamp(abs(dot(normalize(NORMAL), normalize(VIEW))), 0.0, 1.0), 1.6);
	float along = 1.0 - UV.y;
	float fade = mix(0.25, 1.0, along * along);
	float dust = 0.75 + 0.5 * texture(noise_tex, vec2(UV.x * 3.0, UV.y * 2.0 - TIME * 0.03)).r;
	ALBEDO = beam_color.rgb * strength * edge * fade * dust;
}
