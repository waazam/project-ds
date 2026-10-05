// The snow cave (Act 24; the owner's references: blue ice tunnels, scalloped like the inside of a shell, the light coming
// through the ice blue, trodden snow underfoot; "it needs its own shaders to make it really pop"):
// - the walls and the roof: ice, deep blue where it's thick and in its hollows, paler and greener-blue where it bulges
//   toward the eye, scalloped all over (a cellular dimpling, carried into the normal), glossy and a little see-into
//   (a cold light from inside it, faint: the caves are never pitch black, never bright);
// - the floor: snow, trodden and packed, white going blue into the walls' feet, a crystal glinting here and there as the
//   eye moves (nothing twinkles on its own);
// - between them, a soft band where the snow banks up the walls.
// Everything world-projected three ways (the cave's mesh has no UVs).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

uniform sampler2D cell_tex : filter_linear_mipmap, repeat_enable;   // cellular noise: the scallops
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;  // smooth noise: the blotches
uniform sampler2D snow_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform vec3 ice_deep : source_color = vec3(0.05, 0.13, 0.25);
uniform vec3 ice_pale : source_color = vec3(0.3, 0.45, 0.6);
uniform vec3 snow_col : source_color = vec3(0.6, 0.66, 0.75);
uniform vec3 snow_shadow : source_color = vec3(0.43, 0.51, 0.64);
uniform float scallop_scale = 0.3;
uniform float glow = 0.055;
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

float tri(sampler2D t, vec3 p, vec3 w, float s) {
	return texture(t, p.zy * s).r * w.x + texture(t, p.xz * s).r * w.y + texture(t, p.xy * s).r * w.z;
}

void fragment() {
	vec3 w = tri_w(w_nrm);
	float floorness = smoothstep(0.55, 0.85, w_nrm.y);
	// the scallops: the cellular noise's distance, a dimple in each cell
	float cell = tri(cell_tex, w_pos, w, scallop_scale);
	float cell2 = tri(cell_tex, w_pos, w, scallop_scale * 2.3);
	float blot = tri(noise_tex, w_pos, w, 0.09);
	// the ice: deep blue in the dimples and the thick of it, paler on the ridges between them
	float ridge = smoothstep(0.15, 0.75, cell * 0.7 + cell2 * 0.3);
	// (gently: at full strength the scallops and blotches read as camouflage, busy and glaring)
	vec3 ice = mix(ice_deep, ice_pale, 0.3 + ridge * 0.35 + (blot - 0.5) * 0.25);
	ice *= 0.92 + 0.12 * blot;
	// the snow: packed white, blue in its hollows and toward the walls
	float trod = tri(noise_tex, w_pos, w, 0.25);
	vec3 snow = mix(snow_shadow, snow_col, (0.45 + (trod - 0.5) * 0.35) * floorness + 0.1 * floorness);
	// snow banked up the walls' feet
	float bank = max(floorness, smoothstep(0.8, 0.15, l_y) * 0.85);
	vec3 col = mix(ice, snow, clamp(bank, 0.0, 1.0));
	ALBEDO = col;
	// the scallops carried into the normal (from the cellular field's slope), the snow's own grain on the floor
	vec3 e = vec3(0.04, 0.0, 0.0);
	float cx = tri(cell_tex, w_pos + e.xyy, w, scallop_scale) - cell;
	float cy = tri(cell_tex, w_pos + e.yxy, w, scallop_scale) - cell;
	float cz = tri(cell_tex, w_pos + e.yyx, w, scallop_scale) - cell;
	vec3 g = vec3(cx, cy, cz) / 0.04;
	vec3 vn = normalize((VIEW_MATRIX * vec4(normalize(w_nrm - g * 0.09 * (1.0 - floorness)), 0.0)).xyz);
	NORMAL = vn;
	// (satin, not glass: glossy, every dimple threw back the lantern as a glint, all over)
	ROUGHNESS = mix(mix(0.42, 0.55, ridge), 0.9, floorness);
	SPECULAR = mix(0.4, 0.25, floorness);
	// light from inside the ice: faint, cold, more in the thin bulges
	EMISSION = ice * glow * (0.5 + ridge) * (1.0 - floorness);
	// a cold light at the ice's edge (the rim, toward the eye)
	RIM = 0.12 * (1.0 - floorness);
	RIM_TINT = 0.7;
	// the snow's crystals catching the eye
	if (floorness > 0.4) {
		vec3 c = floor(w_pos * 60.0);
		if (hash3(c) > 0.995) {
			vec3 facet = normalize(vec3(hash3(c + 1.3), hash3(c + 4.7), hash3(c + 9.1)) * 2.0 - 1.0 + vec3(0.0, 1.2, 0.0));
			vec3 v = normalize((INV_VIEW_MATRIX * vec4(VIEW, 0.0)).xyz);
			EMISSION += vec3(0.9, 0.95, 1.0) * pow(clamp(dot(facet, v), 0.0, 1.0), 120.0) * glisten;
		}
	}
}
