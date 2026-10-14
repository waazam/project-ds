// (2026-10-14a, the early acts' look) Old painted boards: a door, trim, a shed's door. A scanned plank set
// (albedo, normal, roughness) projected three ways in the object's own space, the boards turned to run up and down on
// the upright faces (or along, with vertical = false), under a coat of paint that has faded, chalked and worn: chipped
// off in flakes to the grey wood, rubbed through at the edges a hand touches, splashed and grimed toward the foot, and
// sunk into the boards' seams. The vertex colour tints the whole (as the kits' other materials).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D wood_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D wood_nor : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D wood_rough : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D noise_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
/// tiles of the plank set per metre (the set has thirteen boards to the tile)
uniform float scale = 0.55;
uniform bool vertical = true;
/// the top faces: boards along z (else along x)
uniform bool top_along_z = false;
/// across the boards: 1 = the scan's boards and seams; small = one board stretched over the face (a post, a beam: grain only)
uniform float across = 1.0;
uniform vec3 wood_tint : source_color = vec3(0.78, 0.74, 0.68);
uniform vec3 paint : source_color = vec3(0.17, 0.22, 0.18);
/// 0 = bare wood, 1 = a full coat
uniform float coat = 0.85;
/// how much of the coat has flaked off (0..1)
uniform float chipped = 0.45;
/// grime and splash rising from the foot (object y), metres
uniform float foot_y = 0.0;
uniform float foot_height = 0.45;
uniform float grime = 0.55;
uniform float normal_strength = 1.0;
uniform float paint_rough = 0.78;
/// the scan is dark (about 0.1 linear): the tint is relative to it times this
uniform float gain = 3.0;

varying vec3 v_pos;
varying vec3 v_nrm;

void vertex() {
	v_pos = VERTEX;
	v_nrm = NORMAL;
}

vec2 plank_uv(vec2 along_across) {
	if (across >= 0.999) return along_across * scale;
	// grain only: stay inside one board of the scan (its middle, about 0.077 down the tile), mirrored back and forth
	// across it, so a post or a beam never shows a seam
	float t = along_across.y * across * scale / 0.05;
	float tri = abs(fract(t) * 2.0 - 1.0) * 2.0 - 1.0;
	return vec2(along_across.x * scale, 0.077 + 0.026 * tri);
}

void fragment() {
	vec3 n = normalize(v_nrm);
	vec3 w = pow(abs(n), vec3(6.0));
	w /= (w.x + w.y + w.z);
	// per axis: (u along the boards, v across them). Upright faces: boards up and down (or along x/z).
	vec2 uvx = vertical ? vec2(v_pos.y, v_pos.z) : vec2(v_pos.z, v_pos.y);
	vec2 uvz = vertical ? vec2(v_pos.y, v_pos.x) : vec2(v_pos.x, v_pos.y);
	vec2 uvy = top_along_z ? vec2(v_pos.z, v_pos.x) : vec2(v_pos.x, v_pos.z);
	uvx = plank_uv(uvx); uvz = plank_uv(uvz); uvy = plank_uv(uvy);
	vec3 ax = texture(wood_tex, uvx).rgb, ay = texture(wood_tex, uvy).rgb, az = texture(wood_tex, uvz).rgb;
	vec3 raw = ax * w.x + ay * w.y + az * w.z;
	vec3 wood = raw * wood_tint * gain;
	float rx = texture(wood_rough, uvx).r, ry = texture(wood_rough, uvy).r, rz = texture(wood_rough, uvz).r;
	float wrough = rx * w.x + ry * w.y + rz * w.z;
	// (the normal maps import two-channel: z rebuilt from x and y)
	vec3 nx = vec3(texture(wood_nor, uvx).rg * 2.0 - 1.0, 0.0), ny = vec3(texture(wood_nor, uvy).rg * 2.0 - 1.0, 0.0), nz = vec3(texture(wood_nor, uvz).rg * 2.0 - 1.0, 0.0);
	nx.z = sqrt(max(0.0, 1.0 - dot(nx.xy, nx.xy)));
	ny.z = sqrt(max(0.0, 1.0 - dot(ny.xy, ny.xy)));
	nz.z = sqrt(max(0.0, 1.0 - dot(nz.xy, nz.xy)));

	// the seams: where the wood is darkest across the board (the scan's grooves)
	float lum = dot(raw, vec3(0.333));   // (linear: the boards about 0.1, the seams under 0.03)
	float seam = 1.0 - smoothstep(0.025, 0.05, lum);

	// the paint: flaked in islands (two scales of noise), worn more at the foot and where it's rubbed
	vec3 p = v_pos * 3.1;
	float big = texture(noise_tex, p.xy * 0.35 + p.zy * 0.21 + vec2(0.13, 0.71)).r;
	float fine = texture(noise_tex, p.xy * 2.3 + p.zx * 1.7 + vec2(0.4, 0.2)).r;
	float flake = big * 0.45 + fine * 0.55;
	float footw = 1.0 - smoothstep(foot_y, foot_y + foot_height, v_pos.y);
	float thresh = mix(0.3, 0.62, chipped) + footw * 0.1;
	float on_paint = smoothstep(thresh - 0.03, thresh + 0.03, flake) * coat;
	// the coat thins into the grain: the wood's dark streaks show through as a faint pattern
	float thin = clamp((0.075 - lum) * 8.0, 0.0, 1.0) * 0.3;
	vec3 pcol = paint * mix(0.82, 1.12, big);
	// chalked: greyed in patches by the weather
	float chalk = fine * 0.18 + 0.08;
	pcol = mix(pcol, vec3(dot(pcol, vec3(0.333))), chalk);
	pcol = mix(pcol, wood * 0.6, thin);
	// where the paint has gone the wood under it is grey and dirty, not fresh: it never stands out pale against the coat
	vec3 bare = mix(wood, vec3(dot(wood, vec3(0.333))) * vec3(0.95, 0.9, 0.85), coat * 0.5) * mix(1.0, 0.62, coat);
	vec3 col = mix(bare, pcol, on_paint);
	// the flaked edge: a thin dark line where paint stands proud of the wood
	float edge = smoothstep(thresh - 0.045, thresh - 0.015, flake) * (1.0 - smoothstep(thresh - 0.015, thresh + 0.01, flake));
	col *= 1.0 - edge * 0.35 * coat;
	col *= 1.0 - seam * 0.45 * on_paint;
	// grime and splash from the foot; damp streaks running down
	float streak = texture(noise_tex, vec2(v_pos.x * 7.0 + v_pos.z * 7.0, v_pos.y * 0.6)).r;
	col *= 1.0 - grime * (footw * 0.45 + smoothstep(0.55, 0.85, streak) * 0.18);
	ALBEDO = col * COLOR.rgb;
	ROUGHNESS = mix(wrough, paint_rough + (fine - 0.5) * 0.12, on_paint);
	SPECULAR = mix(0.22, 0.32, on_paint);

	// relief: the boards' normal map, flattened under the paint (it fills the grain but not the seams)
	float ns = normal_strength * mix(1.0, 0.45, on_paint * (1.0 - seam));
	float sx = n.x >= 0.0 ? 1.0 : -1.0, sy = n.y >= 0.0 ? 1.0 : -1.0, sz = n.z >= 0.0 ? 1.0 : -1.0;
	vec3 tx = vertical ? vec3(0.0, 1.0, 0.0) : vec3(0.0, 0.0, 1.0);
	vec3 tz = vertical ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
	vec3 bx = vertical ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
	vec3 bz = vertical ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 1.0, 0.0);
	vec3 pnx = normalize(tx * nx.x * ns + bx * nx.y * ns + vec3(sx, 0.0, 0.0) * nx.z);
	vec3 ty = top_along_z ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0), by = top_along_z ? vec3(1.0, 0.0, 0.0) : vec3(0.0, 0.0, 1.0);
	vec3 pny = normalize(ty * ny.x * ns + by * ny.y * ns + vec3(0.0, sy, 0.0) * ny.z);
	vec3 pnz = normalize(tz * nz.x * ns + bz * nz.y * ns + vec3(0.0, 0.0, sz) * nz.z);
	vec3 nobj = normalize(n + (pnx - vec3(sx, 0.0, 0.0)) * w.x + (pny - vec3(0.0, sy, 0.0)) * w.y + (pnz - vec3(0.0, 0.0, sz)) * w.z);
	NORMAL = normalize(mat3(VIEW_MATRIX) * (mat3(MODEL_MATRIX) * nobj));
}
