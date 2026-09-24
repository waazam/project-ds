// Act 12's breach: for a moment the whole world goes red. Not an overlay that hides the scene: the
// image is regraded toward a blood-red monochrome (dark stays dark, highlights burn red), so the
// tentacles are still plainly visible through it. Sits one layer under the PS2 post, which dithers
// and grains it like everything else. amount 0..1 (the crossing eases it in once and lets it fade).
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform float amount : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec3 c = texture(screen_tex, SCREEN_UV).rgb;
	float l = dot(c, vec3(0.3, 0.59, 0.11));
	vec3 red = vec3(0.05 + l * 1.55, l * 0.16, l * 0.12);
	// the edges darken toward a bruise, the centre stays readable
	vec2 d = SCREEN_UV - 0.5;
	red *= 1.0 - dot(d, d) * 1.2;
	COLOR = vec4(mix(c, red, amount), 1.0);
}
