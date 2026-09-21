shader_type spatial;
render_mode blend_mix, depth_draw_never, depth_test_default, diffuse_lambert, specular_disabled, cull_back;

// Laid over a building's meshes (GeometryInstance3D.material_overlay) to show it burning:
// "amount" spreads blackened char across the surface in world-space patches (0 = none,
// 1 = all of it), and "ember" lights sparse orange cracks inside a few still-hot patches
// of the char, breathing slowly. The pattern is world-projected, so neighbouring pieces
// char continuously across their seams.
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform float amount : hint_range(0.0, 1.0) = 0.0;
uniform float ember : hint_range(0.0, 2.0) = 0.0;
uniform vec3 char_color : source_color = vec3(0.03, 0.025, 0.022);
uniform vec3 ember_color : source_color = vec3(1.0, 0.33, 0.05);

varying vec3 wpos;
varying vec3 wnrm;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

float tri(vec3 p, vec3 n, float s) {
	vec3 w = pow(abs(n), vec3(4.0));
	w /= (w.x + w.y + w.z);
	return texture(noise_tex, p.yz * s).r * w.x + texture(noise_tex, p.xz * s).r * w.y + texture(noise_tex, p.xy * s).r * w.z;
}

void fragment() {
	float big = tri(wpos, wnrm, 0.11);
	float fine = tri(wpos + vec3(3.1, 7.7, 1.3), wnrm, 0.45);
	float spread = big * 0.7 + fine * 0.3;
	float cover = smoothstep(spread - 0.1, spread + 0.03, amount * 1.1);
	// a few patches that are still glowing, and thin cracks inside them
	float hot = smoothstep(0.56, 0.72, tri(wpos + vec3(2.0, 5.0, 1.0), wnrm, 0.17));
	float c1 = tri(wpos + vec3(11.0), wnrm, 0.5);
	float crack = 1.0 - smoothstep(0.0, 0.02, abs(c1 - 0.5));
	float breathe = 0.65 + 0.35 * sin(TIME * 1.7 + big * 17.0) * sin(TIME * 0.6 + fine * 9.0);
	ALBEDO = char_color * (0.75 + fine * 0.5);
	ROUGHNESS = 1.0;
	EMISSION = ember_color * cover * ember * breathe * hot * (crack * 2.2 + 0.12);
	ALPHA = cover;
}
