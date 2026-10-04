// Fresh snow with body (2026-10-04, the owner: "the snow looks flat and two dimensional, we probably need better shaders
// for the snow all around to give it more body, texture, and that subtle glistening sparkle effect fresh snow has"):
// - body: bright on top, going a cold shadowed blue down its sides and in under its overhangs (the light that gets into
//   snow and comes out blue), with soft drifts of lighter and darker over it at a hand's scale;
// - texture: the snow photo's normal map, world-projected three ways, crisp at the grain, and a coarser crust over it;
// - glisten: here and there a single crystal catches the eye, tiny and sharp, a different one as you move (from where the
//   eye is, not from the clock: nothing flashes or twinkles on its own).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 top_color : source_color = vec3(0.84, 0.86, 0.9);
uniform vec3 side_color : source_color = vec3(0.56, 0.63, 0.76);
uniform float grain_scale = 0.9;
uniform float glisten = 0.55;
uniform float glisten_density = 0.996;

varying vec3 w_pos;
varying vec3 w_nrm;

void vertex() {
	w_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	w_nrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

float hash3(vec3 p) {
	return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453);
}

vec3 tri_normal(vec3 p, vec3 n, float s) {
	vec3 w = pow(abs(n), vec3(4.0));
	w /= (w.x + w.y + w.z + 1e-4);
	vec3 nx = texture(normal_tex, p.zy * s).rgb * w.x;
	vec3 ny = texture(normal_tex, p.xz * s).rgb * w.y;
	vec3 nz = texture(normal_tex, p.xy * s).rgb * w.z;
	return nx + ny + nz;
}

void fragment() {
	float up = clamp(w_nrm.y * 0.5 + 0.5, 0.0, 1.0);
	float drift = texture(noise_tex, w_pos.xz * 0.35).r * 0.6 + texture(noise_tex, w_pos.xz * 1.7 + vec2(0.3)).r * 0.4;
	vec3 col = mix(side_color, top_color, smoothstep(0.25, 0.95, up));
	col *= 0.9 + 0.16 * drift;
	ALBEDO = col;
	NORMAL_MAP = normalize(mix(tri_normal(w_pos, w_nrm, grain_scale), tri_normal(w_pos, w_nrm, grain_scale * 0.23), 0.35));
	NORMAL_MAP_DEPTH = 1.1;
	ROUGHNESS = mix(0.78, 0.92, drift);
	SPECULAR = 0.35;
	// the cold light got into it and back out: a faint blue lift on the sides, where the lamp doesn't reach
	BACKLIGHT = side_color * 0.22;
	// a crystal here and there catching the eye: per tiny cell, a facet in a random direction; it glints when it faces
	// the eye squarely
	vec3 cell = floor(w_pos * 70.0);
	float pick = hash3(cell);
	if (pick > glisten_density) {
		vec3 facet = normalize(vec3(hash3(cell + 1.3), hash3(cell + 4.7), hash3(cell + 9.1)) * 2.0 - 1.0 + w_nrm * 1.2);
		vec3 v = normalize((INV_VIEW_MATRIX * vec4(VIEW, 0.0)).xyz);
		float g = pow(clamp(dot(facet, v), 0.0, 1.0), 120.0);
		EMISSION = vec3(0.9, 0.95, 1.0) * g * glisten;
	}
}
