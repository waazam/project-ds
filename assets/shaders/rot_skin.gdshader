// Act 18's leviathan: its skin, rotting as the fight goes on (rot 0 -> 1 over the sixteen valves).
// Healthy it is the lake creature's hide: glossy, purple-grey, a wet rim. Rotting it goes mottled and
// grey-green, dull, blotched dark, opening into wet red sores that spread and deepen, and at the end
// pale patches where the hide has sloughed off down to something white underneath: a zombified thing.
// The pattern is in object space, so it stays put on each moving limb.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform float rot : hint_range(0.0, 1.0) = 0.0;
uniform float scale = 0.6;
uniform vec4 healthy : source_color = vec4(0.34, 0.25, 0.29, 1.0);
uniform vec4 rotten : source_color = vec4(0.33, 0.36, 0.24, 1.0);
uniform vec4 dead : source_color = vec4(0.13, 0.11, 0.09, 1.0);
uniform vec4 sore : source_color = vec4(0.42, 0.04, 0.04, 1.0);
uniform vec4 bone : source_color = vec4(0.78, 0.74, 0.62, 1.0);

varying vec3 v_obj;

void vertex() {
	v_obj = VERTEX;
}

void fragment() {
	vec3 p = v_obj;
	float n1 = texture(noise_tex, p.xy * scale + p.z * 0.37).r;
	float n2 = texture(noise_tex, p.zy * scale * 2.3 + vec2(0.31, 0.77)).r;
	float n3 = texture(noise_tex, p.xz * scale * 4.1 + vec2(0.13)).r;
	float mottle = n1 * 0.6 + n2 * 0.4;

	// the hide: healthy -> grey-green rot -> dark dead patches
	vec3 col = healthy.rgb * (0.85 + 0.3 * n3);
	float r1 = smoothstep(0.0, 0.55, rot);
	vec3 rotted = mix(rotten.rgb, dead.rgb, smoothstep(0.45, 0.75, mottle)) * (0.8 + 0.4 * n3);
	col = mix(col, rotted, r1 * smoothstep(0.25 - rot * 0.4, 0.55, mottle + rot * 0.3));
	// sores: wet red openings that spread with the rot
	float sorePat = smoothstep(0.62 - rot * 0.22, 0.7 - rot * 0.2, n2) * smoothstep(0.1, 0.4, rot);
	col = mix(col, sore.rgb * (0.7 + 0.5 * n1), sorePat);
	// late on, the hide sloughs off to something pale beneath
	float slough = smoothstep(0.72, 0.78, n1 * 0.5 + n3 * 0.5 + (1.0 - rot) * 0.6) * smoothstep(0.6, 1.0, rot);
	col = mix(col, bone.rgb * (0.8 + 0.3 * n2), slough);

	ALBEDO = col;
	// glossy and wet when healthy; the rot is dull, the sores wet
	ROUGHNESS = mix(0.14, 0.75, r1) * (1.0 - sorePat * 0.7);
	SPECULAR = mix(0.7, 0.3, r1) + sorePat * 0.4;
	RIM = mix(0.85, 0.2, r1);
	RIM_TINT = 0.7;
}
