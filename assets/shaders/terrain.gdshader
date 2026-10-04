// Forest ground: world-space tiled low-res textures blended by a splat mask
// (R dirt trail, G gravel, B grass, A stream mud), by slope (rock) and by
// large-scale noise (leaf litter / needle duff / moss patches).
// Vertex colour carries baked canopy shade.
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;

uniform sampler2D tex_litter : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_floor : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_moss : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_grass : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_dirt : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_gravel : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_rock : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_noise : filter_linear_mipmap, repeat_enable;
uniform sampler2D mask : filter_linear_mipmap, repeat_disable;
/// The forest floor's micro-surface (DetailKit's ground: leaf litter grain, from Poly Haven), close up only.
uniform sampler2D detail_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float detail_tile = 1.3;
uniform vec2 mask_origin = vec2(-120.0, -360.0);
uniform vec2 mask_size = vec2(240.0, 420.0);
uniform float tile = 0.42;
uniform float litter_tile = 0.6;
uniform vec3 litter_tint = vec3(1.0);
uniform vec3 mud_color : source_color = vec3(0.10, 0.08, 0.065);
uniform float moss_amount = 0.5;
uniform float trail_leaf_cover = 0.55;
uniform float trail_bright = 1.3;
/// North of this (world z, more negative) the trail is bare dirt; south of it, gravel.
uniform float gravel_trail_z = -125.0;
uniform float puddles = 1.0;

varying vec3 wpos;
varying vec3 wnrm;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec2 uv = wpos.xz * tile;
	vec4 m = texture(mask, (wpos.xz - mask_origin) / mask_size);
	float n_big = texture(tex_noise, wpos.xz * 0.017).r;
	float n_mid = texture(tex_noise, wpos.xz * 0.061 + vec2(0.31, 0.17)).r;
	float n_fine = texture(tex_noise, wpos.xz * 0.29 + vec2(0.7, 0.4)).r;
	float canopy = 1.0 - COLOR.r;   // 0 open .. ~0.45 deep under the trees

	// base: autumn leaf litter, darker needle duff in patches, moss where it's shaded
	vec3 litter = texture(tex_litter, wpos.xz * litter_tile).rgb * litter_tint;
	vec3 duff = texture(tex_floor, uv).rgb;
	vec3 c = mix(litter, duff, smoothstep(0.42, 0.68, n_big) * 0.75);
	float moss = smoothstep(0.62, 0.78, n_mid * 0.65 + n_big * 0.35 + canopy * 0.35 + (moss_amount - 0.5) * 0.3);
	c = mix(c, texture(tex_moss, uv * 0.9).rgb, moss * 0.9);

	c = mix(c, mix(texture(tex_grass, uv * 1.4).rgb, litter, 0.25 * n_fine), m.b);
	float slope = 1.0 - clamp(wnrm.y, 0.0, 1.0);
	vec3 rock = mix(texture(tex_rock, uv * 0.7).rgb, texture(tex_moss, uv).rgb, smoothstep(0.45, 0.65, n_mid) * 0.6);
	c = mix(c, rock, smoothstep(0.30, 0.48, slope) * 0.85);
	c = mix(c, mud_color * (0.8 + 0.4 * texture(tex_dirt, uv * 2.0).r), m.a * 0.85);
	c = mix(c, texture(tex_gravel, uv * 1.7).rgb, m.g);

	// trail: packed dirt, mostly buried under fallen leaves; the worn centre line shows through
	float cover = smoothstep(0.3, 0.7, n_fine * 0.8 + (1.0 - m.r) * 0.6 + (trail_leaf_cover - 0.5));
	vec3 trail = mix(texture(tex_dirt, uv * 1.25).rgb, litter * 1.1, cover) * trail_bright;
	// the open first stretch (the owner's photo of a trail, 2026-10-03): pea gravel laid on it, leaves over it, giving
	// way to the bare dirt as the woods get deep
	float gravelly = smoothstep(gravel_trail_z - 45.0, gravel_trail_z, wpos.z);
	vec3 pea = texture(tex_gravel, uv * 2.6).rgb * vec3(1.02, 0.98, 0.9) * trail_bright;
	trail = mix(trail, mix(pea, litter * 1.05, cover * 0.6), gravelly);
	c = mix(c, trail, m.r);
	// puddles standing in the trail's dips: dark, still, glossy
	float pud = smoothstep(0.69, 0.73, n_mid * 0.75 + n_fine * 0.25) * smoothstep(0.55, 0.9, m.r) * puddles;
	c = mix(c, c * 0.3 + vec3(0.025, 0.03, 0.035), pud);

	// canopy shade is baked into the vertex colour, but open ground (trail, gravel, grass) stays lighter
	float open_ground = max(max(m.r, m.g), m.b);
	float shade = mix(COLOR.r, 1.0, open_ground * 0.8);
	// the texture pass: fine grit and wet dark pockets at close range, so the ground holds detail underfoot
	float grit = texture(tex_noise, wpos.xz * 1.9 + vec2(0.13, 0.71)).r;
	float pocket = smoothstep(0.62, 0.8, texture(tex_noise, wpos.xz * 0.9 + vec2(0.5, 0.2)).r);
	float near = 1.0 - smoothstep(6.0, 22.0, length(VERTEX));
	c *= mix(1.0, (0.88 + 0.2 * grit) * (1.0 - 0.18 * pocket), near);
	c *= mix(1.0, texture(detail_tex, wpos.xz * detail_tile).r, near);
	ALBEDO = c * shade;
	ROUGHNESS = mix(1.0, 0.06, pud);
	SPECULAR = mix(0.5, 0.75, pud);
}
