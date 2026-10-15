// (2026-10-14h, the old-and-abandoned pass) Act 15's hall as an old institutional corridor: painted concrete block in two
// tones split at a dado line (a darker green-grey below, an old cream above, a dark line between), the paint chipped back
// to the block, scuffed black along the foot where trolleys and boots hit it, a tide line of rising damp, brown water
// stains with rings at their edges, and runs from the dark overhead. Higher up the paint gives way to grimy grey.
// Projected flat on each wall's own plane (along it and up it), so a tall wall never streaks: the old concrete shader's
// second set was swizzled onto the walls end-on and smeared along them in long lines.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

uniform sampler2D block_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D block_nor : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bare_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform float block_scale = 1.2;     // tiles a metre (the scan's four courses: about 21 cm each)
uniform float bare_scale = 0.4;
uniform float floor_y = 0.0;         // world y of the floor
uniform float top_y = 22.0;          // world y the water runs down from
uniform float dado_y = 1.2;          // the paint line, above the floor
uniform float kick_h = 0.2;
uniform float paint_top = 3.2;       // where the cream starts giving way to grime
uniform vec3 paint_low : source_color = vec3(0.34, 0.4, 0.33);
uniform vec3 paint_high : source_color = vec3(0.72, 0.69, 0.58);
uniform vec3 line_col : source_color = vec3(0.16, 0.15, 0.12);
uniform float chip = 1.0;
uniform float damage = 1.0;
uniform float seed = 0.0;
uniform vec3 tint : source_color = vec3(1.0);

varying vec3 w_pos;
varying vec3 w_nrm;

void vertex() {
	w_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	w_nrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

float nz(vec2 p) { return texture(noise_tex, p).r; }
float hash1(float x) { return fract(sin(x * 127.1 + seed * 3.7) * 43758.5453); }

void fragment() {
	vec3 an = abs(w_nrm);
	bool flat_face = an.y > 0.7;
	float along = an.x > an.z ? w_pos.z : w_pos.x;
	vec2 uv = flat_face ? w_pos.xz : vec2(along, w_pos.y);
	float h = w_pos.y - floor_y;
	vec3 blk = texture(block_tex, uv * block_scale).rgb;
	float lum = clamp(dot(blk, vec3(0.333)), 0.0, 1.0);
	vec3 bare = texture(bare_tex, uv * bare_scale + vec2(0.37, 0.11)).rgb;
	float wob = (nz(vec2(along * 0.031 + seed, 0.37)) - 0.5) * 0.012;
	float line = dado_y + wob;
	// the paint: low and high, the block's courses and pits showing through it a little
	vec3 paint = h < line ? paint_low : paint_high;
	vec3 c = paint * clamp(0.3 + 1.4 * lum, 0.45, 1.15);
	// the line between the two
	float stripe = 1.0 - smoothstep(0.012, 0.02, abs(h - line - 0.02));
	c = mix(c, line_col * (0.8 + 0.4 * lum), stripe);
	// high up: the cream greys and darkens under dust and soot
	float up = smoothstep(paint_top - 0.3, paint_top + 6.0, h);
	c = mix(c, bare * vec3(0.62, 0.6, 0.56), up * 0.85);
	// chipped paint, back to the bare block (more of it low down and at the rail's height, where things hit it)
	float cn = nz(uv * 0.33 + vec2(seed * 0.17, 0.0)) * 0.5 + nz(uv * 1.4 + 0.5) * 0.5;
	float hit = 0.05 * (1.0 - smoothstep(0.0, 0.6, h)) + 0.04 * (1.0 - smoothstep(0.0, 0.25, abs(h - 0.95)));
	float th = 0.74 - 0.04 * chip - hit + (h > line ? 0.025 : 0.0);
	float chipped = smoothstep(th, th + 0.012, cn) * (1.0 - up * 0.6) * (flat_face ? 0.0 : 1.0);
	float chipEdge = (smoothstep(th - 0.025, th, cn) - smoothstep(th, th + 0.012, cn)) * (1.0 - up);
	c *= 1.0 - chipEdge * 0.45;
	// (the bare block under it: a paler grey than the old paint, the primer's grey-white in patches)
	vec3 under = mix(bare * 1.3 + 0.04, vec3(0.62, 0.6, 0.55) * (0.8 + 0.4 * lum), 0.35 * step(0.5, nz(uv * 0.6 + 3.3)));
	c = mix(c, under, chipped);
	// the foot: a scuffed, blackened kick band
	float kick = (1.0 - smoothstep(kick_h - 0.03, kick_h + 0.02, h)) * (flat_face ? 0.0 : 1.0);
	float sc = nz(vec2(along * 0.9, h * 2.5) + 0.3);
	c *= 1.0 - kick * (0.2 + 0.4 * smoothstep(0.45, 0.75, sc));
	// trolley scuffs: dark horizontal marks round the rail's height
	float tr = smoothstep(0.62, 0.7, nz(vec2(along * 0.12, h * 3.0) + 7.0)) * (1.0 - smoothstep(0.0, 0.18, abs(h - 0.82)));
	c *= 1.0 - tr * 0.35 * (flat_face ? 0.0 : 1.0);
	// rising damp: a tide line at its own height along the wall, the paint stained and blistered under it
	float tideH = 0.35 + 0.35 * nz(vec2(along * 0.017 + seed, 1.7));
	float damp = 1.0 - smoothstep(tideH - 0.12, tideH, h);
	c = mix(c, c * vec3(0.74, 0.68, 0.55), damp * 0.65 * damage);
	float tideLine = smoothstep(tideH - 0.03, tideH, h) - smoothstep(tideH, tideH + 0.025, h);
	c *= 1.0 - tideLine * 0.3 * damage;
	// water stains: brown blots with a darker ring at their edges, here and there up the wall
	float st = nz(vec2(along * 0.045, h * 0.06) + vec2(seed * 0.31, 3.0)) * 0.7 + nz(uv * 0.35) * 0.3;
	float stain = smoothstep(0.6, 0.66, st);
	float ring = smoothstep(0.645, 0.66, st) - smoothstep(0.665, 0.69, st);
	c = mix(c, c * vec3(0.66, 0.56, 0.4), stain * damage);
	c *= 1.0 - ring * 0.4 * damage;
	// runs from the dark overhead, longer in places
	float col = floor(along * 1.7);
	float runLen = (0.25 + 0.75 * hash1(col)) * (top_y - floor_y) * 0.85;
	float run = smoothstep(0.55, 0.8, nz(vec2(along * 0.47, h * 0.004) + 11.0)) * clamp(1.0 - (top_y - w_pos.y) / runLen, 0.0, 1.0);
	c *= 1.0 - run * 0.45 * damage * (flat_face ? 0.0 : 1.0);
	// broad mottling: no stretch of it the same as the next
	c *= 0.86 + 0.24 * nz(uv * 0.018 + seed * 0.07);
	ALBEDO = c * COLOR.rgb * tint;
	// relief: the block's under the paint, sharper where it's bare
	vec3 nm = texture(block_nor, uv * block_scale).xyz * 2.0 - 1.0;
	nm.xy *= mix(0.45, 1.0, max(chipped, up));
	vec3 T = flat_face ? vec3(1.0, 0.0, 0.0) : (an.x > an.z ? vec3(0.0, 0.0, sign(w_nrm.x)) : vec3(-sign(w_nrm.z), 0.0, 0.0));
	vec3 B = flat_face ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
	vec3 wn = normalize(T * nm.x + B * nm.y + w_nrm * max(nm.z, 0.2));
	NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
	ROUGHNESS = clamp(mix(0.72, 0.95, max(chipped, up)) - damp * 0.15 - stain * 0.1, 0.4, 1.0);
	SPECULAR = 0.35;
}
