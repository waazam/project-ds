// Leaf litter and mud over the forest floor (the fidelity pass, 2026-10-02): soft-edged patches laid on the ground
// under the trees, the photo projected from above in world space (so neighbouring patches run on into each other and
// nothing stretches on a slope), their edges broken up by the photo's own pattern. Never casts shadows.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, diffuse_lambert, specular_schlick_ggx;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform float tile = 0.55;
uniform float opacity = 0.7;
// the photo is a lit day's forest floor: brought down to the woods' own dark ground
uniform float darken = 0.42;

varying vec3 wp;

void vertex() {
	wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 uv = wp.xz * tile;
	vec3 c = texture(albedo_tex, uv).rgb;
	float n = texture(albedo_tex, uv * 0.31 + vec2(0.37, 0.71)).g;
	float r = length(UV * 2.0 - 1.0) + (n - 0.4) * 0.7;
	ALBEDO = c * COLOR.rgb * darken;
	float a = 1.0 - smoothstep(0.1, 0.95, r);
	ALPHA = a * a * opacity;
	ROUGHNESS = 0.95;
	SPECULAR = 0.15;
}
