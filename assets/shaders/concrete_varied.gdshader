// Concrete that doesn't repeat (2026-10-09, the owner: "The repeating patterns in the red light green light hallway on
// the walls needs to be varied and the texture for all the concrete needs to be drastically upscaled to look more real"):
// two baked 1024 px sets (tools/textures/surfaces.py: concrete_a, older concrete_b), world-projected three ways, and
// over them, at the scale of a wall:
// - the two sets blended by a broad noise, so one stretch is newer, the next stained and old;
// - the wall in pours: panels a couple of metres wide and a lift high, each a slightly different tone, with a faint
//   cold joint between;
// - water down it: streaks from the top, darker, uneven, longer in places;
// - grime at its foot, where the floor meets it.
// The vertex colour still tints it (the hall's own darkening along its length).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

uniform sampler2D tex_a : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D tex_b : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D nor_a : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rough_a : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D noise_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform float scale = 0.35;          // tiles a metre
uniform float panel_w = 2.4;         // the pours' width (m)
uniform float panel_h = 1.5;         // a lift's height (m)
uniform float streaks = 0.7;
uniform float streak_len = 3.0;     // how far down the longest run (m)
uniform float top_y = 4.0;           // where the water runs down from (world y)
uniform float floor_y = 0.0;
uniform vec3 tint : source_color = vec3(1.0);

varying vec3 w_pos;
varying vec3 w_nrm;

void vertex() {
	w_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	w_nrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

vec3 tri_w(vec3 n) { vec3 w = pow(abs(n), vec3(4.0)); return w / (w.x + w.y + w.z + 1e-4); }
vec4 tri_c(sampler2D t, vec3 p, vec3 w, float s) { return texture(t, p.zy * s) * w.x + texture(t, p.xz * s) * w.y + texture(t, p.xy * s) * w.z; }
vec4 tri_c2(sampler2D t, vec3 p, vec3 w, float s) { return texture(t, p.zy * s) * w.x + texture(t, p.xz * s) * w.y + texture(t, p.xy * s) * w.z; }
vec4 tri_d(sampler2D t, vec3 p, vec3 w, float s) { return texture(t, p.zy * s) * w.x + texture(t, p.xz * s) * w.y + texture(t, p.xy * s) * w.z; }
vec4 tri_d2(sampler2D t, vec3 p, vec3 w, float s) { return texture(t, p.zy * s) * w.x + texture(t, p.xz * s) * w.y + texture(t, p.xy * s) * w.z; }
float hash2(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

vec3 tri_normal(sampler2D t, vec3 p, vec3 n, vec3 w, float s) {
	vec3 nx = texture(t, p.zy * s).xyz * 2.0 - 1.0;
	vec3 ny = texture(t, p.xz * s).xyz * 2.0 - 1.0;
	vec3 nz = texture(t, p.xy * s).xyz * 2.0 - 1.0;
	vec3 tx = vec3(nx.xy + n.zy, abs(nx.z) * n.x);
	vec3 ty = vec3(ny.xy + n.xz, abs(ny.z) * n.y);
	vec3 tz = vec3(nz.xy + n.xy, abs(nz.z) * n.z);
	return normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
}

void fragment() {
	vec3 w = tri_w(w_nrm);
	// (the second set offset and turned, so even the same set never lines up with itself)
	vec3 a = tri_c(tex_a, w_pos, w, scale).rgb;
	vec3 b = tri_c2(tex_b, w_pos.zxy * vec3(1.0, 1.0, 1.0) + vec3(13.7, 0.0, 5.3), w, scale * 0.83).rgb;
	float broad = tri_d(noise_tex, w_pos, w, 0.045).r;
	float mixab = smoothstep(0.35, 0.68, broad);
	vec3 c = mix(a, b, mixab);
	// the pours: a tone per panel (along the wall, whichever way it runs), a faint joint between
	float along = abs(w_nrm.x) > abs(w_nrm.z) ? w_pos.z : w_pos.x;
	vec2 cell = vec2(floor(along / panel_w), floor(w_pos.y / panel_h));
	float pt = hash2(cell);
	c *= 0.9 + 0.18 * pt;
	float jx = abs(fract(along / panel_w) - 0.5) * 2.0, jy = abs(fract(w_pos.y / panel_h) - 0.5) * 2.0;
	float joint = smoothstep(0.985, 1.0, max(jx, jy)) * (1.0 - abs(w_nrm.y));
	c *= 1.0 - joint * 0.35;
	// water down it from the top: streaks, uneven, longer in places
	float sx = tri_d2(noise_tex, vec3(along * 0.9, 0.0, along * 0.9 + w_pos.y * 0.02), w, 0.8).r;
	float len = (0.15 + 0.85 * hash2(vec2(floor(along * 3.0), 7.0))) * streak_len;
	float fromTop = clamp((top_y - w_pos.y) / len, 0.0, 1.0);
	float streak = smoothstep(0.55, 0.85, sx) * (1.0 - fromTop) * (1.0 - abs(w_nrm.y)) * streaks;
	c *= 1.0 - streak * 0.45;
	// grime at the foot
	float foot = (1.0 - smoothstep(0.0, 0.5, w_pos.y - floor_y)) * (1.0 - abs(w_nrm.y));
	c *= 1.0 - foot * 0.35;
	ALBEDO = c * COLOR.rgb * tint;
	vec3 wn = tri_normal(nor_a, w_pos, w_nrm, w, scale);
	NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
	ROUGHNESS = clamp(tri_d(rough_a, w_pos, w, scale).r - streak * 0.25, 0.3, 1.0);
	SPECULAR = 0.3;
}
