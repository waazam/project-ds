// The snow cave (Act 24; the owner's references: blue ice tunnels, scalloped like the inside of a shell, the light coming
// through the ice blue, trodden snow underfoot; then, 2026-10-04: "Increase the texel density on the ice walls ... bake way
// higher-res texture sets for the ice maze, we should make the tunnel more icy ... use that ice texture you created for the
// back door of the ski lodge"):
// - the walls and the roof: glacial ice from a baked 1024 set (tools/textures/ice_maze.py: deep blue, banded, cloudy
//   patches, strings of bubbles, faint fractures; scallops and cracks in its normal map; glassy where it's clear and
//   frosted where it's cloudy), about 400 texels a metre, a second, broad sample over it so the repeat never shows; glassy
//   like the lodge's iced back door (a sharp sheen, a cold rim at its edges) but never glaring: the sheen is clamped, the
//   ice is dark;
// - a faint cold light from inside the ice, more where it's thin (the caves are never pitch black, never bright);
// - the floor: packed snow from its own baked set, bluish in its hollows, banked up the walls' feet;
// - a crystal glinting in the snow here and there as the eye moves (nothing twinkles on its own).
// Everything world-projected three ways (the cave's mesh has no UVs), the normals blended so the three agree.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

uniform sampler2D ice_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D ice_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D ice_rough : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D snow_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D snow_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D noise_tex : filter_linear_mipmap_anisotropic, repeat_enable;  // broad, smooth: breaks the repeat
uniform float ice_scale = 0.4;      // tiles a metre: 2.5 m a tile, ~410 texels a metre
uniform float snow_scale = 0.55;
uniform vec3 ice_tint : source_color = vec3(0.62, 0.7, 0.82);
uniform vec3 snow_tint : source_color = vec3(0.8, 0.84, 0.9);
uniform float normal_strength = 1.0;
uniform float glow = 0.05;
uniform float glisten = 0.35;

varying vec3 w_pos;
varying vec3 w_nrm;
varying float l_y;

void vertex() {
	l_y = VERTEX.y;
	w_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	w_nrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

float hash3(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }

vec3 tri_w(vec3 n) { vec3 w = pow(abs(n), vec3(4.0)); return w / (w.x + w.y + w.z + 1e-4); }

// (two of each: Godot wants every texture handed to one function sampled alike, and colour maps are sRGB, data maps not)
vec4 tri(sampler2D t, vec3 p, vec3 w, float s) {
	return texture(t, p.zy * s) * w.x + texture(t, p.xz * s) * w.y + texture(t, p.xy * s) * w.z;
}
vec4 tri_c(sampler2D t, vec3 p, vec3 w, float s) {
	return texture(t, p.zy * s) * w.x + texture(t, p.xz * s) * w.y + texture(t, p.xy * s) * w.z;
}

// three normal maps, one per projection, blended into one world normal ("whiteout")
vec3 tri_normal(sampler2D t, vec3 p, vec3 n, vec3 w, float s, float k) {
	vec3 nx = texture(t, p.zy * s).xyz * 2.0 - 1.0;
	vec3 ny = texture(t, p.xz * s).xyz * 2.0 - 1.0;
	vec3 nz = texture(t, p.xy * s).xyz * 2.0 - 1.0;
	nx.xy *= k; ny.xy *= k; nz.xy *= k;
	vec3 tx = vec3(nx.xy + n.zy, abs(nx.z) * n.x);
	vec3 ty = vec3(ny.xy + n.xz, abs(ny.z) * n.y);
	vec3 tz = vec3(nz.xy + n.xy, abs(nz.z) * n.z);
	return normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
}

void fragment() {
	vec3 w = tri_w(w_nrm);
	float floorness = smoothstep(0.55, 0.85, w_nrm.y);
	float broad = tri(noise_tex, w_pos, w, 0.07).r;
	// the ice: the baked set, and the same set again broad and turned (its repeat lost under the second)
	vec3 ice_a = tri_c(ice_albedo, w_pos, w, ice_scale).rgb;
	vec3 ice_b = tri_c(ice_albedo, w_pos.zxy + 17.0, w, ice_scale * 0.31).rgb;
	vec3 ice = mix(ice_a, ice_b, 0.35) * ice_tint * (0.8 + 0.35 * broad);
	float rough_i = tri(ice_rough, w_pos, w, ice_scale).r;
	// the snow
	vec3 snow = tri_c(snow_albedo, w_pos, w, snow_scale).rgb * snow_tint * (0.88 + 0.2 * broad);
	// snow banked up the walls' feet
	float bank = clamp(max(floorness, smoothstep(0.8, 0.15, l_y) * 0.85), 0.0, 1.0);
	ALBEDO = mix(ice, snow, bank);
	vec3 n_ice = tri_normal(ice_normal, w_pos, w_nrm, w, ice_scale, normal_strength);
	vec3 n_snow = tri_normal(snow_normal, w_pos, w_nrm, w, snow_scale, normal_strength * 0.8);
	vec3 wn = normalize(mix(n_ice, n_snow, bank));
	NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
	// glassy where the ice is clear, frosted where it's cloudy (never a mirror: no glare off every dimple)
	ROUGHNESS = mix(clamp(rough_i * 0.9 + 0.12, 0.2, 0.75), 0.88, bank);
	SPECULAR = mix(0.55, 0.25, bank);
	// light from inside the ice: faint, cold, more where it's thin and clear
	EMISSION = ice * glow * (1.2 - rough_i) * (1.0 - bank);
	// the cold rim at its edges, as on the lodge's iced door
	RIM = 0.22 * (1.0 - bank);
	RIM_TINT = 0.65;
	// the snow's crystals catching the eye
	if (bank > 0.4) {
		vec3 c = floor(w_pos * 60.0);
		if (hash3(c) > 0.995) {
			vec3 facet = normalize(vec3(hash3(c + 1.3), hash3(c + 4.7), hash3(c + 9.1)) * 2.0 - 1.0 + vec3(0.0, 1.2, 0.0));
			vec3 v = normalize((INV_VIEW_MATRIX * vec4(VIEW, 0.0)).xyz);
			EMISSION += vec3(0.9, 0.95, 1.0) * pow(clamp(dot(facet, v), 0.0, 1.0), 120.0) * glisten;
		}
	}
}
