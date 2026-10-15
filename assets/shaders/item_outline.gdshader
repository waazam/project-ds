// The thin white line round a thing the player can pick up or use (ItemOutline; the owner, 2026-10-14: "a very thin but
// noticeable white line highlight for items you can pick up"). The item's mesh drawn again, grown outward by a fixed
// number of the game's own pixels (its 360 lines, whatever the 3D's supersampling), and drawn only where the item
// itself isn't (the stencil its overlay, item_outline_mask.gdshader, wrote): a crisp line on the silhouette, the same
// width near or far, on a thin card or a note as on the axe.
//
// The grow: each vertex is pushed out on the screen along its normal as seen on the screen, blended toward "away from
// the item's centre" where the normal faces the camera (a flat card seen from above has no sideways normal) and a
// little everywhere (so a box's hard corners don't leave a notch).
// Walls: depth-tested, so a wall in front hides it; the grown hull is drawn a few centimetres nearer the camera than
// the thing really is, so the line where it meets the table or the ground in front of it isn't lost in that surface.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;
// drawn only off the item (its overlay marked it)
stencil_mode read, compare_not_equal, 64;

uniform vec3 line_color : source_color = vec3(1.0);
uniform float alpha : hint_range(0.0, 1.0) = 0.0;
/// Width in the game's pixels (of its 360 lines).
uniform float width_px = 1.0;
/// The item's centre (world), for the outward push where a normal can't give one.
uniform vec3 center_world = vec3(0.0);
/// How much nearer the camera the line is drawn: a few centimetres (and a little more further off), so the edge where
/// a thing meets the table in front of it keeps its line. (It was 6 % of the distance: a sheet's table, hidden a
/// couple of centimetres under the cloth, came through it as a white slab.)
uniform float pull_base = 0.025;
uniform float pull_rate = 0.006;
uniform float game_lines = 360.0;
/// 1 for a flat thing (ItemOutline chooses): pushed straight out from its centre on the screen. The push along the
/// normals is right for a handle's sides but not for a flat card seen at a slant (its face's normal points up the
/// screen at its near edge too).
uniform float radial_only = 0.0;

void vertex() {
	vec4 vp = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	vec3 nv = normalize((MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
	float aspect = VIEWPORT_SIZE.x / max(VIEWPORT_SIZE.y, 1.0);
	float dist = max(length(vp.xyz), 0.05);
	float pull = min((pull_base + pull_rate * dist) / dist, 0.06);
	vec4 cp = PROJECTION_MATRIX * vec4(vp.xyz * (1.0 - pull), 1.0);
	vec4 cn = PROJECTION_MATRIX * vec4(vp.xyz + nv * 0.02 * max(-vp.z, 0.05), 1.0);
	vec4 cc = PROJECTION_MATRIX * (VIEW_MATRIX * vec4(center_world, 1.0));
	vec2 sp = cp.xy / cp.w;
	vec2 sn = cn.xy / max(cn.w, 1e-4) - sp;
	vec2 sr = sp - cc.xy / max(cc.w, 1e-4);
	sn.x *= aspect;
	sr.x *= aspect;
	vec2 dn = length(sn) > 1e-6 ? normalize(sn) : vec2(0.0);
	vec2 dr = length(sr) > 1e-6 ? normalize(sr) : vec2(0.0);
	// how much of the normal lies across the screen (0: it faces the camera)
	float wn = clamp(length(nv.xy) * 1.6, 0.0, 1.0);
	vec2 d = mix(dn * wn + dr * (1.0 - wn * 0.75), dr, radial_only);
	d = length(d) > 1e-6 ? normalize(d) : vec2(0.0);
	vec2 off = d * width_px * 2.0 / game_lines;
	off.x /= aspect;
	cp.xy += off * cp.w;
	POSITION = cp;
}

void fragment() {
	ALBEDO = line_color;
	ALPHA = alpha;
}
