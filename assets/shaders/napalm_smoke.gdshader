// The flamethrower's smoke and steam (2026-10-14d): soft puffs turned to the eye (the stream turns each quad), their
// shape from the weather's smoke flipbook (a dense knot billowing out to a thin wisp over its life) torn by noise.
// Napalm's smoke is thick, oily and near black, lit from below by the fire it came off (a dull orange underglow that
// fades as it climbs away); the steam where it hits the snow is a pale grey-white that's gone in a second or two.
// Premultiplied alpha: the glow adds, the smoke occludes.
// INSTANCE_CUSTOM = (age 0..1, kind: 0 smoke, 1 steam, seed, glow 0..1).
shader_type spatial;
render_mode unshaded, blend_premul_alpha, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

uniform sampler2D sheet : filter_linear_mipmap, hint_default_white;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform float soft = 0.8;
uniform vec3 smoke_col = vec3(0.014, 0.012, 0.011);
uniform vec3 steam_col = vec3(0.2, 0.21, 0.23);
uniform vec3 glow_col = vec3(1.1, 0.36, 0.06);
// the cave's light on the steam and the smoke's top (a faint cold fill, so neither is a flat cut-out)
uniform vec3 fill_col = vec3(0.008, 0.01, 0.014);

varying vec4 v_custom;

void vertex() { v_custom = INSTANCE_CUSTOM; }

void fragment() {
	float age = v_custom.x, kind = v_custom.y, seed = v_custom.z, glow = v_custom.w;
	float ang = seed * 6.2831 + age * (seed - 0.5) * 2.0;
	vec2 uv = UV - 0.5;
	uv = mat2(vec2(cos(ang), sin(ang)), vec2(-sin(ang), cos(ang))) * uv + 0.5;
	float frame = floor(min(age, 0.999) * 16.0);
	vec2 cell = vec2(mod(frame, 4.0), floor(frame / 4.0)) * 0.25;
	float a = texture(sheet, cell + clamp(uv, 0.01, 0.99) * 0.25).a;
	// (round: the turned cell's corners never show as a square's edge)
	a *= 1.0 - smoothstep(0.3, 0.5, length(UV - 0.5));
	float n = texture(noise_tex, uv * 0.9 + vec2(seed * 7.0, -TIME * 0.05 + seed)).r;
	a *= smoothstep(0.15, 0.65, n + a * 0.4);
	float life = smoothstep(0.0, 0.06, age) * (1.0 - smoothstep(0.45, 1.0, age));
	vec3 col;
	float alpha;
	if (kind < 0.5) {
		// the underglow: strongest low on the puff (where the fire is), fading as it climbs and ages
		float under = glow * (1.0 - smoothstep(0.0, 0.5, age)) * smoothstep(0.2, 1.0, UV.y);
		alpha = a * 0.94 * life;
		col = (smoothstep(0.0, 1.0, 1.0 - UV.y) * fill_col + smoke_col * (0.7 + 0.6 * n) + glow_col * under * (0.2 + 0.35 * n)) * alpha;
	} else {
		alpha = a * 0.3 * life;
		col = (steam_col * (0.75 + 0.4 * n) + glow_col * glow * 0.35) * alpha;
	}
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 up = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	float scene_z = -up.z / up.w;
	float k = clamp((scene_z + VERTEX.z) / soft, 0.0, 1.0) * smoothstep(0.3, 1.2, -VERTEX.z);
	ALBEDO = col * k;
	ALPHA = clamp(alpha * k, 0.0, 1.0);
}
