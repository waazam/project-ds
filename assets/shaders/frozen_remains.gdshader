// Remains left in the cold (2026-10-04, the owner: the dining hall's skeleton should look "grimy and frozen over with
// some meat still decomposing off of his mainly skeletal remains"): its own colour under grime (dark, in blotches, worked
// into it), the meat's wet sheen gone dull in the cold; and frost over the top of it all, a pale crystalline crust on what
// faces up, with now and then a crystal catching the eye as it moves (from where the eye is: nothing flashes).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

uniform vec3 base_color : source_color = vec3(0.74, 0.68, 0.55);
uniform vec3 grime_color : source_color = vec3(0.2, 0.14, 0.09);
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform float grime = 0.55;
uniform float frost = 0.6;
uniform float base_rough = 0.7;
uniform float wet = 0.0;

varying vec3 w_pos;
varying vec3 w_nrm;

void vertex() {
	w_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	w_nrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

float hash3(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }

void fragment() {
	float n1 = texture(noise_tex, w_pos.xz * 3.1 + w_pos.y * 1.7).r;
	float n2 = texture(noise_tex, w_pos.xy * 9.0 + vec2(0.37, 0.11)).r;
	float g = smoothstep(0.35, 0.8, n1 * 0.65 + n2 * 0.35) * grime;
	vec3 col = mix(base_color * (0.85 + 0.25 * n2), grime_color, g);
	// grime settles underneath: what faces down is darker
	col *= mix(0.55, 1.0, smoothstep(-0.7, 0.3, w_nrm.y));
	float up = smoothstep(0.15, 0.7, w_nrm.y + (n1 - 0.5) * 0.6) * frost;
	col = mix(col, vec3(0.78, 0.82, 0.88) * (0.9 + 0.15 * n2), up * 0.8);
	ALBEDO = col;
	ROUGHNESS = mix(mix(base_rough, 0.3, wet), 0.85, up);
	SPECULAR = mix(0.4, 0.55, wet);
	vec3 cell = floor(w_pos * 90.0);
	if (up > 0.3 && hash3(cell) > 0.994) {
		vec3 facet = normalize(vec3(hash3(cell + 1.3), hash3(cell + 4.7), hash3(cell + 9.1)) * 2.0 - 1.0 + w_nrm);
		vec3 v = normalize((INV_VIEW_MATRIX * vec4(VIEW, 0.0)).xyz);
		EMISSION = vec3(0.9, 0.95, 1.0) * pow(clamp(dot(facet, v), 0.0, 1.0), 120.0) * 0.5 * up;
	}
}
