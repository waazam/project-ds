// A room's wall, floor or ceiling, weathered (2026-10-14g, the interiors kit: RoomSurface.cs). What the game's
// StandardMaterial3D did for these surfaces (its photo by UV or triplanar, object or world space, its two-channel normal
// map, roughness, the detail grain, the vertex colour), and over it a world-space breakup so a big plane never reads as
// one tiled print: broad mottling; on walls water streaks run down from the ceiling (and from wherever RoomShade marked
// damp), a ragged tide line of rising damp at the foot; on ceilings ring-edged water stains; on floors dirt and the
// rubbed-pale paths where feet went. All of it from one small noise texture, a few samples a pixel.
// RoomShade's bake feeds it: COLOR.rgb its occlusion, COLOR.a (1 - wet) on walls, (1 - wear) on floors, UV2 the height
// above the floor and the depth under the ceiling (zero when not baked: those parts then fall back to plain mottling).
// No tangents needed (MeshKit writes none): the normal map's frame comes from the projection, or from screen derivatives.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec4 albedo : source_color = vec4(1.0);
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_albedo_tex = false;
uniform bool use_vertex_color = true;
uniform vec3 uv1_scale = vec3(1.0);
uniform vec3 uv1_offset = vec3(0.0);
// 0 the mesh's UVs, 1 triplanar in the object's space, 2 triplanar in the world's
uniform int projection = 0;
uniform float tri_sharpness = 4.0;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_normal = false;
uniform float normal_scale = 1.0;
uniform float roughness : hint_range(0.0, 1.0) = 0.9;
uniform sampler2D roughness_tex : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_roughness_tex = false;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float specular : hint_range(0.0, 1.0) = 0.5;
uniform sampler2D detail_tex : source_color, hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool use_detail = false;

// ---- the weathering
uniform sampler2D grime_noise : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float grime : hint_range(0.0, 2.0) = 1.0;
uniform vec3 grime_tint : source_color = vec3(0.42, 0.37, 0.31);
uniform float mottle : hint_range(0.0, 1.0) = 0.35;
uniform float streaks : hint_range(0.0, 1.0) = 0.5;
uniform float streak_len = 1.6;
uniform float tide : hint_range(0.0, 1.0) = 0.45;
uniform float ceiling_stains : hint_range(0.0, 1.0) = 0.5;
uniform float floor_dirt : hint_range(0.0, 1.0) = 0.45;
uniform float wear : hint_range(0.0, 1.0) = 0.6;
uniform float desaturate : hint_range(0.0, 1.0) = 0.15;
uniform float seed = 0.0;

varying vec3 wpos;
varying vec3 wnrm;
varying vec3 tpos;
varying vec3 tnrm;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnrm = normalize(mat3(MODEL_MATRIX) * NORMAL);
	if (projection == 2) { tpos = wpos; tnrm = wnrm; }
	else { tpos = VERTEX; tnrm = NORMAL; }
	tpos = tpos * uv1_scale + uv1_offset;
	UV = UV * uv1_scale.xy + uv1_offset.xy;
}

vec3 tri_weights(vec3 n) {
	vec3 w = pow(abs(n), vec3(tri_sharpness));
	return w / max(dot(w, vec3(1.0)), 1e-4);
}

// Godot's own triplanar layout (so a converted material lands exactly as it did)
vec4 tri_sample(sampler2D s, vec3 p, vec3 w) {
	p *= vec3(1.0, -1.0, 1.0);
	return texture(s, p.xy) * w.z + texture(s, p.xz) * w.y + texture(s, p.zy * vec2(-1.0, 1.0)) * w.x;
}

vec3 two_channel(vec4 t) {
	vec2 xy = t.xy * 2.0 - 1.0;
	return vec3(xy, sqrt(max(0.0, 1.0 - dot(xy, xy))));
}

float nz(vec2 p) { return texture(grime_noise, p).r; }

void fragment() {
	vec3 w = tri_weights(tnrm);
	vec3 c = albedo.rgb;
	if (use_albedo_tex) c *= projection == 0 ? texture(albedo_tex, UV).rgb : tri_sample(albedo_tex, tpos, w).rgb;
	if (use_detail) c *= projection == 0 ? texture(detail_tex, UV).rgb : tri_sample(detail_tex, tpos, w).rgb;
	float r = roughness;
	if (use_roughness_tex) r *= projection == 0 ? texture(roughness_tex, UV).r : tri_sample(roughness_tex, tpos, w).r;
	vec4 vc = use_vertex_color ? COLOR : vec4(1.0);
	c *= vc.rgb;

	// ---- the weathering: where this pixel is, and which way it faces (the surface's own, not the normal map's)
	vec3 n = normalize(wnrm);
	float is_wall = 1.0 - smoothstep(0.5, 0.75, abs(n.y));
	float is_floor = smoothstep(0.6, 0.85, n.y);
	float is_ceil = smoothstep(0.6, 0.85, -n.y);
	float along = abs(n.x) > abs(n.z) ? wpos.z : wpos.x;
	vec2 sd = vec2(seed * 0.137, seed * 0.291);
	vec2 plane = mix(wpos.xz, vec2(along, wpos.y), is_wall) + sd;
	bool baked = UV2.x + UV2.y > 0.0005;
	float up = UV2.x, down = UV2.y;
	// broad mottling: patches a metre or three across, darker and lighter
	float m = nz(plane * 0.07) * 0.6 + nz(plane * 0.29 + 0.37) * 0.4;
	float g = mottle * smoothstep(0.38, 0.78, m);
	// walls: streaks down from the top (or from the damp), a tide line at the foot
	float wet = (1.0 - vc.a) * is_wall;
	float src = smoothstep(0.42, 0.72, nz(vec2(along * 0.09 + sd.x, 0.71)));
	float runs = smoothstep(0.52, 0.68, nz(vec2(along * 2.1 + sd.y, wpos.y * 0.04)));
	runs *= 0.6 + 0.4 * nz(vec2(along * 3.1, wpos.y * 0.2));
	float from_top = baked ? exp(-down / max(streak_len, 0.1)) : 0.35;
	float streak = streaks * runs * clamp(src * from_top + wet * 1.3, 0.0, 1.0) * is_wall;
	float tide_top = 0.22 + 0.55 * nz(vec2(along * 0.19 + sd.x, 0.13)) + wet * 0.35;
	float below = baked ? smoothstep(tide_top + 0.015, tide_top - 0.06, up) : 0.0;
	float line = baked ? 1.0 - smoothstep(0.0, 0.022, abs(up - tide_top)) : 0.0;
	float tide_amt = tide * is_wall * (below * (0.35 + 0.3 * nz(vec2(along * 0.8, up * 2.0))) + line * 0.9) * (0.5 + 0.5 * src);
	// ceilings: blotches with darker rims where water stood and dried
	float cs = nz(wpos.xz * 0.21 + sd + 0.5);
	float ceil_amt = ceiling_stains * is_ceil * (smoothstep(0.6, 0.66, cs) * 0.35 + (1.0 - smoothstep(0.0, 0.025, abs(cs - 0.62))) * 0.55);
	// floors: dirt in drifts, scuffs; worn paths rubbed pale and dull
	float fd = smoothstep(0.45, 0.8, nz(wpos.xz * 0.55 + sd)) * (0.6 + 0.4 * nz(wpos.xz * 4.0));
	float worn = is_floor * (1.0 - vc.a) * wear;
	float floor_amt = floor_dirt * is_floor * fd * (1.0 - worn * 0.6);
	float amount = clamp((g + streak * 1.1 + tide_amt + ceil_amt + floor_amt * 0.5 + wet * 0.25) * grime, 0.0, 0.88);
	float lum = dot(c, vec3(0.299, 0.587, 0.114));
	c = mix(c, vec3(lum), desaturate + amount * 0.3);
	c *= mix(vec3(1.0), grime_tint, amount);
	// the worn path: the finish gone, paler and greyer, its grain flattened
	float wl = dot(c, vec3(0.333));
	c = mix(c, vec3(wl) * 1.18 + 0.012, worn * 0.55);
	ALBEDO = c;
	ROUGHNESS = clamp(r - wet * 0.25 - line * tide * 0.15 + worn * 0.1, 0.05, 1.0);
	METALLIC = metallic;
	SPECULAR = specular * (1.0 - amount * 0.4);

	// ---- the normal map, without tangents
	if (use_normal) {
		vec3 wn = vec3(0.0, 1.0, 0.0);
		if (projection == 0) {
			vec3 tn = two_channel(texture(normal_tex, UV));
			tn.xy *= normal_scale;
			// a cotangent frame from the screen derivatives (view space)
			vec3 dp1 = dFdx(VERTEX), dp2 = dFdy(VERTEX);
			vec2 du1 = dFdx(UV), du2 = dFdy(UV);
			vec3 N = normalize(NORMAL);
			vec3 dp2p = cross(dp2, N), dp1p = cross(N, dp1);
			vec3 T = dp2p * du1.x + dp1p * du2.x;
			vec3 B = dp2p * du1.y + dp1p * du2.y;
			float inv = inversesqrt(max(max(dot(T, T), dot(B, B)), 1e-12));
			NORMAL = normalize(mat3(T * inv, -B * inv, N) * tn);
		} else {
			vec3 pp = tpos * vec3(1.0, -1.0, 1.0);
			vec3 nx = two_channel(texture(normal_tex, pp.zy * vec2(-1.0, 1.0)));
			vec3 ny = two_channel(texture(normal_tex, pp.xz));
			vec3 nzz = two_channel(texture(normal_tex, pp.xy));
			vec3 N = normalize(tnrm);
			vec3 sg = sign(N);
			// whiteout blend per axis (each projection's frame is the axes themselves)
			nx = vec3(nx.xy * normal_scale + N.zy, abs(nx.z) * abs(N.x));
			ny = vec3(ny.xy * normal_scale + N.xz, abs(ny.z) * abs(N.y));
			nzz = vec3(nzz.xy * normal_scale + N.xy, abs(nzz.z) * abs(N.z));
			vec3 tn = normalize(nx.zyx * vec3(sg.x, 1.0, 1.0) * w.x + ny.xzy * vec3(1.0, sg.y, 1.0) * w.y + nzz.xyz * vec3(1.0, 1.0, sg.z) * w.z);
			wn = projection == 2 ? tn : normalize(mat3(MODEL_MATRIX) * tn);
			NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
		}
	}
}
