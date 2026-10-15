// (2026-10-14, the forest pass) Years of weather on a park sign's face: the routed letters and the paint were crisp and
// clean, fresh from the shop (the owner's survey). A thin film laid just over the board (its quad's UV in metres, offset
// per board so no two match): grime in blotches, rain run down from the top edge in streaks, moss along the bottom
// edge and into the corners, pale lichen spots, and a faint dull film that takes the shine off the white fill. Lit as
// the board is (the lantern shows it), never emissive.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_back, diffuse_burley, specular_disabled, shadows_disabled;

uniform sampler2D noise_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
/// the board's size (m): UV runs 0..size
uniform vec2 size = vec2(1.5, 0.3);
uniform float amount = 1.0;
uniform vec3 grime_col : source_color = vec3(0.07, 0.055, 0.04);
uniform vec3 moss_col : source_color = vec3(0.15, 0.19, 0.07);
uniform vec3 lichen_col : source_color = vec3(0.45, 0.47, 0.38);

void fragment() {
	vec2 m = UV;                     // metres across, up
	vec2 q = m + COLOR.rg * 23.0;    // (the board's own offset, in the vertex colour)
	float big = texture(noise_tex, q * 0.55).r;
	float mid = texture(noise_tex, q * 1.9 + vec2(0.37, 0.11)).r;
	float fine = texture(noise_tex, q * 7.0 + vec2(0.71, 0.29)).r;
	vec2 local = m;                  // 0..size
	float fromTop = size.y - local.y;
	float fromBottom = local.y;
	float edgeX = min(local.x, size.x - local.x);

	// a dull film over all of it (the white fill's chalky, the brown's lost its gloss)
	vec3 col = vec3(0.2, 0.18, 0.15);
	float a = 0.14;
	// grime in blotches
	float g = smoothstep(0.48, 0.78, big * 0.6 + mid * 0.4);
	col = mix(col, grime_col, g);
	a = max(a, g * 0.62);
	// rain run down from the top edge
	float s = texture(noise_tex, vec2(q.x * 5.0, q.y * 0.35 + 0.5)).r;
	float run = smoothstep(0.58, 0.82, s) * (1.0 - smoothstep(0.0, size.y * (0.5 + mid), fromTop));
	col = mix(col, grime_col * 0.8, run);
	a = max(a, run * 0.5);
	// moss along the bottom edge and up the ends, ragged
	float reach = 0.025 + 0.08 * mid;
	float moss = max(1.0 - smoothstep(reach * 0.4, reach, fromBottom), (1.0 - smoothstep(reach * 0.3, reach * 0.8, edgeX)) * 0.7);
	moss *= smoothstep(0.32, 0.55, big + fine * 0.3);
	col = mix(col, moss_col * (0.75 + 0.5 * fine), moss);
	a = max(a, moss * 0.92);
	// lichen: pale grey-green spots, a few
	float li = smoothstep(0.74, 0.8, fine) * smoothstep(0.45, 0.7, mid);
	col = mix(col, lichen_col, li);
	a = max(a, li * 0.7);
	ALBEDO = col;
	ALPHA = clamp(a * amount, 0.0, 1.0);
	ROUGHNESS = 1.0;
}
