// A shaft of daylight through a window into a dim room, with the dust turning in it (2026-10-04, the owner: "make
// everything stand out more"). Drawn on a few crossed cards running from the window along the light (UV.x across the
// shaft, UV.y from the window, 0, to its end, 1): soft at its sides, brightest at the window and dying away along its
// length, faded where it meets a floor or a wall (no hard line where it cuts through them) and where a card is seen edge
// on. Dim on purpose: a haze of light, never a glare.
shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, unshaded, shadows_disabled, fog_disabled;

uniform vec3 shaft_color : source_color = vec3(0.78, 0.82, 0.9);
uniform float strength = 0.05;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;

void fragment() {
	float side = smoothstep(0.0, 0.3, UV.x) * smoothstep(1.0, 0.7, UV.x);
	float along = smoothstep(0.0, 0.06, UV.y) * pow(1.0 - UV.y, 1.6);
	float face = smoothstep(0.08, 0.55, abs(dot(normalize(NORMAL), normalize(VIEW))));
	float dust = 0.65 + 0.7 * texture(noise_tex, vec2(UV.x * 1.7 + TIME * 0.004, UV.y * 2.4 - TIME * 0.012)).r;
	// soft where it passes into a surface
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 up = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	float scene_z = -up.z / up.w;
	float prox = clamp((scene_z + VERTEX.z) / 0.8, 0.0, 1.0);
	ALBEDO = shaft_color * strength * side * along * face * dust * prox;
}
