// (2026-10-14, the forest pass) An old park building's painted cinder block: the trailhead's vault toilet. It was a pale,
// clean, poured box, the first "box" the player meets (the owner's survey). Here, in world space on the walls:
// - the blocks, 40 x 20 cm in running bond, each its own tone, the mortar joints sunk and shadowed under their lip;
// - the concrete's own pores and pits (the concrete_a scan) through it;
// - a coat of park-brown paint, faded and chalked, flaked off in islands to the grey block, worn most at the foot;
// - rain from the eave run down it in dark streaks, black-green algae in them;
// - rust bled down from the steel fittings (up to four sources, world points);
// - splash, grime and moss at the foot, more on the shaded side.
// The vertex colour still tints it.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D tex_a : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D nor_a : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rough_a : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D noise_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float scale = 0.45;
uniform vec2 block = vec2(0.4, 0.2);
uniform vec3 paint : source_color = vec3(0.21, 0.16, 0.11);
uniform float coat = 0.9;
uniform float chipped = 0.12;
uniform vec3 bare_tint : source_color = vec3(0.62, 0.6, 0.56);
uniform float top_y = 2.4;
uniform float floor_y = 0.0;
uniform float streaks = 1.0;
uniform float streak_len = 1.6;
uniform vec3 moss : source_color = vec3(0.14, 0.17, 0.07);
uniform float moss_amount = 0.8;
/// the shaded side (world xz direction it faces): more moss and algae there
uniform vec2 shade_dir = vec2(0.0, -1.0);
uniform vec4 rust_src[4];
uniform int rust_count = 0;

varying vec3 w_pos;
varying vec3 w_nrm;

void vertex() {
	w_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	w_nrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

float hash2(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

void fragment() {
	vec3 n = w_nrm;
	vec3 w = pow(abs(n), vec3(4.0));
	w /= (w.x + w.y + w.z + 1e-4);
	vec3 conc = (texture(tex_a, w_pos.zy * scale) * w.x + texture(tex_a, w_pos.xz * scale) * w.y + texture(tex_a, w_pos.xy * scale) * w.z).rgb;
	float rough = texture(rough_a, w_pos.zy * scale).r * w.x + texture(rough_a, w_pos.xz * scale).r * w.y + texture(rough_a, w_pos.xy * scale).r * w.z;
	float lumc = dot(conc, vec3(0.333));
	float upright = 1.0 - abs(n.y);
	float along = abs(n.x) > abs(n.z) ? w_pos.z : w_pos.x;

	// the blocks: running bond, a tone each; the joints sunk, dark under each block's lower lip, a little lit above
	float row = floor((w_pos.y - floor_y) / block.y);
	float u = (along + mod(row, 2.0) * block.x * 0.5) / block.x;
	float col = floor(u);
	float fx = fract(u), fy = fract((w_pos.y - floor_y) / block.y);
	float jd = min(min(fx, 1.0 - fx) * block.x, min(fy, 1.0 - fy) * block.y);
	float mortar = (1.0 - smoothstep(0.004, 0.009, jd)) * upright;
	float under_lip = (1.0 - smoothstep(0.0, 0.016, (1.0 - fy) * block.y)) * upright;   // just below the joint above: its shadow
	float tone = 0.88 + 0.22 * hash2(vec2(col, row));

	// the paint: flaked in islands, worn most at the foot; it fills the joints
	vec3 p = w_pos * 2.6;
	float big = texture(noise_tex, p.xy * 0.33 + p.zy * 0.21 + vec2(0.13, 0.71)).r;
	float fine = texture(noise_tex, p.xy * 2.1 + p.zx * 1.7 + vec2(0.4, 0.2)).r;
	float flake = big * 0.85 + fine * 0.15;
	float footw = 1.0 - smoothstep(floor_y, floor_y + 0.7, w_pos.y);
	float thresh = mix(0.28, 0.6, chipped) + footw * 0.12;
	float on_paint = smoothstep(thresh - 0.03, thresh + 0.03, flake) * coat * upright;
	vec3 pcol = paint * mix(0.8, 1.12, big) * mix(0.92, 1.06, tone);
	pcol = mix(pcol, vec3(dot(pcol, vec3(0.333))) * vec3(1.0, 0.97, 0.92), fine * 0.15 + 0.05);   // chalked
	vec3 bare = conc * bare_tint * 0.6 * tone;
	vec3 c = mix(bare, pcol * mix(0.85, 1.1, lumc * 2.0), on_paint);
	float edge = smoothstep(thresh - 0.045, thresh - 0.015, flake) * (1.0 - smoothstep(thresh - 0.015, thresh + 0.01, flake));
	c *= 1.0 - edge * 0.3 * coat;
	c *= 1.0 - mortar * 0.38 - under_lip * 0.3;

	// rain from the eave: dark streaks, uneven, longer in places, green-black algae in them
	float sx = texture(noise_tex, vec2(along * 0.9, along * 0.9 * 0.37 + w_pos.y * 0.02)).r;
	float len = (0.2 + 0.8 * hash2(vec2(floor(along * 4.0), 7.0))) * streak_len;
	float fromTop = clamp((top_y - w_pos.y) / len, 0.0, 1.0);
	float streak = smoothstep(0.5, 0.82, sx) * (1.0 - fromTop) * upright * streaks;
	float shade = clamp(dot(normalize(n.xz + vec2(1e-4)), normalize(shade_dir)) * 0.5 + 0.5, 0.0, 1.0);
	c = mix(c, c * vec3(0.55, 0.6, 0.5), streak * (0.55 + 0.25 * shade));

	// rust bled down from the steel
	float rust = 0.0;
	for (int i = 0; i < 4; i++) {
		if (i >= rust_count) break;
		vec4 s = rust_src[i];
		float dx = length(w_pos.xz - s.xz);
		float below = s.y - w_pos.y;
		if (below > 0.0 && dx < s.w * 1.6) {
			float wob = texture(noise_tex, vec2(w_pos.x * 3.1 + w_pos.z * 3.1, w_pos.y * 0.4)).r;
			float wide = s.w * (0.7 + 0.6 * wob) * (1.0 + below * 0.25);
			rust = max(rust, (1.0 - smoothstep(wide * 0.4, wide, dx)) * exp(-below * 1.4));
		}
	}
	c = mix(c, vec3(0.2, 0.09, 0.04) * (0.8 + 0.4 * fine), clamp(rust * upright, 0.0, 1.0) * 0.8);

	// the foot: splash and grime, then moss and algae creeping up (more on the shaded side, in the joints first)
	float foot = 1.0 - smoothstep(0.0, 0.55, w_pos.y - floor_y);
	c *= 1.0 - foot * 0.45 * upright;
	float mn = texture(noise_tex, w_pos.xz * 0.9 + w_pos.yy * 0.7 + vec2(0.3, 0.9)).r;
	float reach = 0.25 + 0.55 * shade + 0.3 * mn;
	float mossy = (1.0 - smoothstep(reach * 0.5, reach, w_pos.y - floor_y)) * smoothstep(0.38, 0.62, mn + mortar * 0.25) * moss_amount * upright;
	c = mix(c, moss * (0.7 + 0.6 * fine), clamp(mossy, 0.0, 1.0));

	ALBEDO = c * COLOR.rgb;
	ROUGHNESS = clamp(mix(rough, 0.8 + (fine - 0.5) * 0.1, on_paint) - streak * 0.15, 0.35, 1.0);
	SPECULAR = 0.25;
	// relief: the concrete's pits (flattened under the paint), the joints' groove
	vec3 nx = texture(nor_a, w_pos.zy * scale).xyz * 2.0 - 1.0;
	vec3 ny = texture(nor_a, w_pos.xz * scale).xyz * 2.0 - 1.0;
	vec3 nz = texture(nor_a, w_pos.xy * scale).xyz * 2.0 - 1.0;
	float ns = mix(1.0, 0.5, on_paint);
	vec3 pert = vec3(0.0, nx.y, nx.x) * w.x + vec3(ny.x, 0.0, ny.y) * w.y + vec3(nz.x, nz.y, 0.0) * w.z;
	vec3 wn = normalize(n + pert * ns * 0.8 + vec3(0.0, -under_lip * 0.5 + mortar * 0.2, 0.0));
	NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
}
