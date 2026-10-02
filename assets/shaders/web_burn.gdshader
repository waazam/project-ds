// Old web that burns away from where it's lit (Act 20's round room): a photographed web card, and a burn front that
// spreads outward from the lighter's touch across the floor, up the dais and against the walls. Behind the front the
// web is gone; at it, a narrow ragged band of strands blacken and glow a dull ember orange as they shrivel. The glow
// is steady and moves slowly (no flicker, no flashing).
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_never;

uniform sampler2D web_tex : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec4 tint : source_color = vec4(0.8, 0.79, 0.75, 0.85);
/// World space: where it caught, and how far the burn has spread (negative: not lit).
uniform vec3 burn_centre = vec3(0.0);
uniform float burn_radius = -100.0;
/// How wide the glowing band at the front is (metres).
uniform float ember_band = 0.7;

varying vec3 wpos;

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec4 t = texture(web_tex, UV) * tint;
	// how far behind the front this point is (positive: burnt), the front ragged
	float d = length((wpos - burn_centre) * vec3(1.0, 0.6, 1.0));
	float behind = burn_radius - d + (vnoise(wpos.xz * 1.7 + wpos.y) - 0.5) * 0.9;
	float gone = smoothstep(0.0, 0.35, behind);
	float ember = smoothstep(-ember_band, 0.0, behind) * (1.0 - gone);
	ALBEDO = mix(t.rgb, vec3(0.06, 0.04, 0.03), ember);
	EMISSION = vec3(1.0, 0.42, 0.1) * ember * ember * 2.2;
	ALPHA = t.a * (1.0 - gone);
	ROUGHNESS = 0.55;
	SPECULAR = 0.3;
}
