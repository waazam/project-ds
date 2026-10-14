// Ink only the blacklight shows (the lantern's second mode): R.H.'s numbers on the trees, handprints,
// smears, footprints to the bunker. Invisible under any other light; where the lantern's violet cone
// falls on it, it fluoresces: a cold cyan-white (or a sick green) with the speckled, fibrous grain of
// real fluorescent residue, brightest at the heart of the beam and fading at its edge and with range.
// The lantern writes the globals every frame (Lantern.cs).
//
// (2026-10-13, the owner: "floating on nothing ... on an invisible piece of paper") A projected decal, not a card:
// the mesh is a box (a unit cube, scaled; local Y is the surface's normal, X and Z the mark's across and down), and
// each pixel finds the real surface behind it from the depth buffer and draws the ink only where that surface lies
// inside the box and faces the way the mark does. So the ink wraps bark, books and boards, shows nothing where there
// is no surface, never on the back of a thin wall, and there is no quad to see: the box draws nothing but ink (and
// no fog: the fog added over an additive card was the "paper").
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, depth_test_disabled, cull_front, shadows_disabled, fog_disabled;

global uniform vec3 uv_light_pos;
global uniform vec3 uv_light_dir;
global uniform float uv_light_on;
global uniform float uv_light_range;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform sampler2D mask_tex : filter_linear_mipmap, repeat_disable;
uniform sampler2D grain_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 ink : source_color = vec3(0.55, 0.85, 1.0);
uniform float strength = 1.6;
/// The mark's other hand (a left print from a right one, a left foot).
uniform bool mirror = false;
/// Cosines of the beam's half-angles: full inside `inner`, gone outside `outer`.
uniform float cos_inner = 0.974;
uniform float cos_outer = 0.9135;   // the lantern's 24 degree cone (Lantern.UvAngle)

varying mat4 v_to_local;
varying vec3 v_axis;
varying vec2 v_scale;

void vertex() {
	v_to_local = inverse(MODEL_MATRIX);
	v_axis = normalize(MODEL_MATRIX[1].xyz);
	v_scale = vec2(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[2].xyz));
}

void fragment() {
	// nothing to show with the blacklight off (a uniform: the same for every pixel, so safe before the derivatives)
	if (uv_light_on < 0.001) discard;
	// the surface behind this pixel
	float depth = textureLod(depth_tex, SCREEN_UV, 0.0).r;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	view.xyz /= view.w;
	vec3 world = (INV_VIEW_MATRIX * vec4(view.xyz, 1.0)).xyz;
	vec3 local = (v_to_local * vec4(world, 1.0)).xyz;
	// (everything that takes a derivative first, before any pixel is thrown away)
	// facing the way the mark does (not the back of a board, not a face side-on to it)
	vec3 n = normalize(cross(dFdx(world), dFdy(world)));
	if (dot(n, CAMERA_POSITION_WORLD - world) < 0.0) n = -n;
	float facing = smoothstep(0.12, 0.4, dot(n, v_axis));
	float depth_fade = 1.0 - smoothstep(0.3, 0.5, abs(local.y));
	vec2 uv = vec2(local.x + 0.5, local.z + 0.5);
	if (mirror) uv.x = 1.0 - uv.x;
	float m = texture(mask_tex, uv).a;
	float g = texture(grain_tex, local.xz * v_scale * 3.1).r;
	if (abs(local.x) > 0.5 || abs(local.y) > 0.5 || abs(local.z) > 0.5) discard;
	if (m * facing * depth_fade < 0.002) discard;

	vec3 to = world - uv_light_pos;
	float d = length(to);
	float cone = smoothstep(cos_outer, cos_inner, dot(to / max(d, 0.001), normalize(uv_light_dir)));
	float fall = 1.0 - smoothstep(uv_light_range * 0.45, uv_light_range, d);
	float lit = uv_light_on * cone * fall;
	float speck = 0.65 + 0.7 * g;
	ALBEDO = ink * m * facing * depth_fade * lit * strength * speck;
}
