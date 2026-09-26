// Act 15's shadow man, after the owner's references (ink-splatter figures, a shape behind frosted glass):
// a flat, light-swallowing black, its outline never clean. Toward the silhouette the surface frays
// into a spattered, ragged edge (noise in object and screen space eats it away where the view grazes),
// and the whole figure "boils" very slightly, in slow steps, like wet ink that won't settle. Slow on
// purpose: nothing here flickers.
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_opaque, shadows_disabled;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform float fray : hint_range(0.0, 1.0) = 0.5;
uniform float boil = 0.03;
uniform float steps_per_second = 2.5;
uniform vec3 ink : source_color = vec3(0.004, 0.004, 0.005);

varying vec3 v_obj;

void vertex() {
	v_obj = VERTEX;
	float t = floor(TIME * steps_per_second) / steps_per_second;
	float n = texture(noise_tex, VERTEX.xy * 1.3 + vec2(t * 0.37, t * 0.21)).r - 0.5;
	VERTEX += NORMAL * n * boil;
}

void fragment() {
	float grazing = 1.0 - abs(dot(normalize(NORMAL), normalize(VIEW)));
	float t = floor(TIME * steps_per_second) / steps_per_second;
	float n = texture(noise_tex, v_obj.xy * 3.1 + v_obj.z * 1.7 + vec2(t * 0.13)).r * 0.6
		+ texture(noise_tex, SCREEN_UV * vec2(11.0, 6.0) + vec2(t * 0.07, 0.0)).r * 0.4;
	if (grazing > 1.0 - fray * 0.85 + (n - 0.5) * 0.7) discard;
	ALBEDO = ink;
}
