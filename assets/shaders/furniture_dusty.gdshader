// The modelled furniture's material with its years of dust in it (the optimization pass, 2026-10-02: the dust had been a
// second pass over every piece, ~7% of the lodge's frame; folded into one). What a dressed StandardMaterial3D did (the
// room's photo at a metre a repeat, its relief and roughness, the piece's baked cavity map multiplied in on the second
// UV set and used as its occlusion) and the dust over it: lying only where it can, on what faces up, in patches, pale
// and matte. World-space, so it's continuous across pieces and never stretched.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec4 albedo : source_color = vec4(1.0);
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_albedo_tex = false;
uniform bool use_vertex_color = false;
uniform vec3 uv1_scale = vec3(1.0);
uniform vec3 uv1_offset = vec3(0.0);
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_normal = false;
uniform float normal_scale = 1.0;
uniform float roughness : hint_range(0.0, 1.0) = 0.8;
uniform sampler2D roughness_tex : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_roughness_tex = false;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float specular : hint_range(0.0, 1.0) = 0.5;
uniform sampler2D cavity_tex : hint_default_white, filter_linear_mipmap;
uniform float ao_light_affect = 0.35;

uniform float dust_amount : hint_range(0.0, 1.0) = 0.55;
uniform vec3 dust_color : source_color = vec3(0.5, 0.48, 0.43);

varying vec3 wpos;
varying vec3 wnorm;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}
float fbm(vec2 p) { return vnoise(p) * 0.55 + vnoise(p * 2.3 + 7.1) * 0.3 + vnoise(p * 5.7 + 3.3) * 0.15; }

void vertex() {
	UV = UV * uv1_scale.xy + uv1_offset.xy;
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnorm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 c = albedo.rgb;
	if (use_albedo_tex) c *= texture(albedo_tex, UV).rgb;
	if (use_vertex_color) c *= COLOR.rgb;
	float cav = texture(cavity_tex, UV2).r;
	c *= cav;
	float r = roughness;
	if (use_roughness_tex) r *= texture(roughness_tex, UV).r;
	// the dust: on what faces up, in patches
	float up = smoothstep(0.45, 0.95, wnorm.y);
	float patches = fbm(wpos.xz * 2.2 + wpos.y * 0.7);
	float fine = vnoise(wpos.xz * 40.0);
	float d = clamp(up * dust_amount * smoothstep(0.25, 0.75, patches) * (0.75 + 0.25 * fine), 0.0, 0.8);
	ALBEDO = mix(c, dust_color * cav, d);
	ROUGHNESS = mix(r, 1.0, d);
	METALLIC = metallic * (1.0 - d);
	SPECULAR = specular * (1.0 - d * 0.8);
	if (use_normal) {
		NORMAL_MAP = texture(normal_tex, UV).rgb;
		NORMAL_MAP_DEPTH = normal_scale * (1.0 - d * 0.6);
	}
	AO = cav;
	AO_LIGHT_AFFECT = ao_light_affect;
}
