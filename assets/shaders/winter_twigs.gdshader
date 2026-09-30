// Act 22's trees: the sprays of fine bare twigs at the ends of the branches (crossed cards; the twigs drawn by
// tools/Textures/make_winter_forest.py, snow along their tops), alpha-cut. Lit as if from above whichever side is
// seen (they're too fine to shade by face). The ice near the lodge as the bark's (MultiMesh custom data r).
shader_type spatial;
render_mode cull_disabled, diffuse_lambert, specular_disabled;

uniform sampler2D twig_tex : source_color, filter_linear_mipmap, repeat_disable;

varying float ice;

void vertex() {
	ice = clamp(INSTANCE_CUSTOM.r, 0.0, 1.0);
}

void fragment() {
	vec4 t = texture(twig_tex, UV);
	vec3 c = t.rgb * COLOR.rgb;
	c = mix(c, c * vec3(0.75, 0.85, 1.0) + vec3(0.06, 0.08, 0.11), ice * 0.7);
	ALBEDO = c;
	ALPHA = t.a;
	ALPHA_SCISSOR_THRESHOLD = 0.4;
	ROUGHNESS = 1.0;
}
