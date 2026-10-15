// Act 14's stairwell walls: poured concrete that goes from clean, to stained and cracked, to dark,
// oily and grimy the deeper it is, and loses its flatness on the way: the deep walls are smoothed
// into a slight, uneven wave, as if something had worn them.
//
// How far down a wall is comes from its world height between top_y and bottom_y, so the change is a
// continuous gradient down the shaft. grime_bias shifts it for everything: when the stairwell moves
// the player back up the shaft it sets the bias so the walls around them look exactly as they did,
// then lets it relax back to zero slowly enough that nobody sees them get cleaner.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D clean_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D stain_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D grime_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform float top_y = 0.0;
uniform float bottom_y = -300.0;
uniform float grime_bias = 0.0;
uniform float wave_amp = 0.08;
// the shaft's middle (world xz) and half-width: the wave dies away into the corners, where two walls
// meet, so they never pull apart
uniform vec2 shaft_centre = vec2(0.0);
uniform float half_width = 2.0;
uniform vec4 tint : source_color = vec4(1.0);
// (2026-10-14b, the graphics pass: the walls had their scans' colour and nothing else, a flat print of concrete under the
// lantern; MeshKit's grids carry no tangents, so the frame is made here from the screen-space derivatives of the
// position and the UV) each stage's relief and roughness, blended the way the colours are
uniform sampler2D clean_nor : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D stain_nor : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D grime_nor : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D clean_rough : filter_linear_mipmap, repeat_enable;
uniform sampler2D grime_rough : filter_linear_mipmap, repeat_enable;
uniform float relief = 0.0;
// a second, larger, rotated sample of the grime breaks the 2.5 m repeat of the scans
uniform float macro_vary = 0.0;
// (2026-10-14h) how stained the walls are at their cleanest (the top): never the bare pale pour
uniform float top_stain = 0.0;

varying float v_g;

float wave(vec3 p) {
	return sin(p.x * 1.31 + p.y * 0.73) * 0.5
		+ sin(p.z * 1.13 - p.y * 0.91 + 1.7) * 0.5
		+ sin(p.y * 2.3 + (p.x + p.z) * 1.7) * 0.25;
}

void vertex() {
	vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float g = clamp((top_y - wp.y) / max(top_y - bottom_y, 0.001) + grime_bias, 0.0, 1.0);
	v_g = g;
	vec2 d = abs(wp.xz - shaft_centre);
	float corner = smoothstep(half_width - 0.02, half_width - 0.45, min(d.x, d.y));
	float a = wave_amp * pow(smoothstep(0.35, 1.0, g), 1.3) * corner;
	if (a > 0.0001) {
		float e = 0.08;
		vec3 grad = vec3(
			wave(wp + vec3(e, 0.0, 0.0)) - wave(wp - vec3(e, 0.0, 0.0)),
			wave(wp + vec3(0.0, e, 0.0)) - wave(wp - vec3(0.0, e, 0.0)),
			wave(wp + vec3(0.0, 0.0, e)) - wave(wp - vec3(0.0, 0.0, e))) / (2.0 * e) * a;
		mat3 m = mat3(MODEL_MATRIX);
		vec3 nw = normalize(m * NORMAL);
		VERTEX += NORMAL * wave(wp) * a;
		vec3 along = grad - nw * dot(grad, nw);
		NORMAL = normalize(transpose(m) * normalize(nw - along));
	}
}

void fragment() {
	vec3 c = texture(clean_tex, UV).rgb;
	vec3 s = texture(stain_tex, UV * 0.9 + vec2(0.37, 0.11)).rgb;
	vec3 k = texture(grime_tex, UV * 0.8 + vec2(0.13, 0.52)).rgb;
	// grime creeps in patchily, not as a level line
	float n = texture(grime_tex, UV * 0.23 + vec2(0.5)).r;
	float g = max(v_g, top_stain) + (n - 0.3) * 0.3;
	float g1 = smoothstep(0.0, 0.5, g);
	float g2 = smoothstep(0.45, 1.0, g);
	vec3 col = mix(mix(c, s, g1), k, g2);
	if (macro_vary > 0.0) {
		vec2 rr = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8)) * UV * 0.17;
		float m = texture(grime_tex, rr + vec2(0.31, 0.77)).r;
		col *= mix(1.0, 0.72 + m * 0.6, macro_vary);
	}
	ALBEDO = col * tint.rgb;
	float oily = g2 * (1.0 - smoothstep(0.1, 0.35, k.r));
	float rough = 0.93;
	if (relief > 0.0) {
		vec3 nc = texture(clean_nor, UV).xyz;
		vec3 ns = texture(stain_nor, UV * 0.9 + vec2(0.37, 0.11)).xyz;
		vec3 nk = texture(grime_nor, UV * 0.8 + vec2(0.13, 0.52)).xyz;
		// (the maps are imported as two-channel normal maps: z rebuilt from x and y)
		vec2 nxy = mix(mix(nc.xy, ns.xy, g1), nk.xy, g2) * 2.0 - 1.0;
		vec3 nm = vec3(nxy * relief, sqrt(max(1.0 - dot(nxy, nxy), 0.0)));
		// the cotangent frame (view space)
		vec3 dp1 = dFdx(VERTEX), dp2 = dFdy(VERTEX);
		vec2 du1 = dFdx(UV), du2 = dFdy(UV);
		vec3 nv = normalize(NORMAL);
		vec3 dp2p = cross(dp2, nv), dp1p = cross(nv, dp1);
		vec3 t = dp2p * du1.x + dp1p * du2.x;
		vec3 b = dp2p * du1.y + dp1p * du2.y;
		float inv = inversesqrt(max(max(dot(t, t), dot(b, b)), 1e-12));
		// (the maps are OpenGL-style, green up; V runs down the wall)
		NORMAL = normalize(t * inv * nm.x - b * inv * nm.y + nv * nm.z);
		rough = mix(texture(clean_rough, UV).r, texture(grime_rough, UV * 0.8 + vec2(0.13, 0.52)).r, g2) * 0.25 + 0.72;
	}
	ROUGHNESS = mix(rough, 0.35, oily);
	SPECULAR = mix(0.25, 0.65, oily);
}
