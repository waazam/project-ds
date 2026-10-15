// (the gushy-water pass, 2026-10-15; was terrain_water.gdshader, a flat dark ribbon with one scrolling sheen)
// A creek's running water. The ribbon's UV.x runs across it (0..1, bank to bank), UV.y down it in metres; the
// vertex colour's red is how steep the bed is there (0 flat .. 1 a chute), its green how far down the creek
// (unused). What it does:
//  - flows: two phase-offset layers of noise dragged down the flow and blended (a flow map's trick, so the
//    current can run fast in the middle and slow at the banks without the pattern shearing apart over time);
//  - waves: the surface heaves in small flow-aligned standing ripples and lumps carried downstream (more in the
//    middle and on steep runs), and its normal follows;
//  - foam: streaks of white water combed out along the flow where it runs steep or narrows, and a broken white
//    lace where it touches the banks and the rocks (from the depth of what's under it: the shallower, the frothier);
//  - depth: see-through and olive at the shallow edges (the stones show), dark peat-brown where deep;
//  - glints: the sky's dull light picked out on the wave faces, moving with them;
//  - debris: now and then a dark leaf or a twig riding the middle of the current.
// `blood` (0..1) runs it red: Act 6's blood rain draining into it (thicker, slower, glossier, darker).
// Low-frequency noise only, so it holds still under the 640x360 post shader (no per-pixel shimmer).
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_always, specular_schlick_ggx;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform vec3 deep_color : source_color = vec3(0.03, 0.033, 0.022);
uniform vec3 shallow_color : source_color = vec3(0.13, 0.13, 0.075);
uniform vec3 foam_color : source_color = vec3(0.5, 0.52, 0.48);
uniform vec3 sky_tint : source_color = vec3(0.12, 0.13, 0.145);
uniform vec3 leaf_color : source_color = vec3(0.16, 0.1, 0.04);
uniform float flow_speed = 1.1;          // metres a second in the middle on the flat
uniform float foam_amount = 1.0;
uniform float wave_height = 0.035;
uniform float width = 3.7;               // metres across the ribbon (UV.x 0..1)
uniform float blood : hint_range(0.0, 1.0) = 0.0;
uniform vec3 blood_color : source_color = vec3(0.15, 0.009, 0.008);
uniform vec3 blood_deep : source_color = vec3(0.055, 0.002, 0.003);

varying float v_steep;
varying float v_centre;

float speed_at(float x, float steep) {
	float c = 1.0 - pow(abs(x * 2.0 - 1.0), 2.0);          // 1 mid-stream, 0 at the banks
	return flow_speed * (0.3 + 0.9 * c) * (1.0 + 2.2 * steep) * (1.0 - 0.6 * blood);
}

void vertex() {
	v_steep = COLOR.r;
	v_centre = 1.0 - pow(abs(UV.x * 2.0 - 1.0), 2.0);
	float sp = speed_at(UV.x, v_steep);
	vec2 m = vec2(UV.x * width, UV.y);
	// lumps carried down the flow, and standing ripples over the bed (steady in place, wobbling)
	float lump = textureLod(noise_tex, vec2(m.x * 0.21, m.y * 0.13 - TIME * sp * 0.13), 0.0).r - 0.5;
	float stand = sin(m.y * 3.1 + sin(m.x * 1.7) * 1.4 + sin(TIME * 2.3 + m.y) * 0.6) * 0.5;
	float h = wave_height * (0.35 + 0.65 * v_centre) * (1.0 + 1.6 * v_steep) * (1.0 - 0.6 * blood);
	VERTEX.y += h * (lump * 1.6 + stand * 0.7);
}

void fragment() {
	vec2 m = vec2(UV.x * width, UV.y);                         // metres: across, along
	float sp = speed_at(UV.x, v_steep);
	// two layers, half a cycle apart, each dragged down the flow and reset; blended so neither reset shows
	float cyc = 1.6;
	float ph0 = fract(TIME / cyc), ph1 = fract(TIME / cyc + 0.5);
	float w0 = 1.0 - abs(ph0 * 2.0 - 1.0);
	vec2 o0 = vec2(0.0, -ph0 * cyc * sp), o1 = vec2(0.37, 0.21 - ph1 * cyc * sp);
	// coarse body (big swirls) and fine chop, both stretched along the flow
	vec2 big = m * vec2(0.42, 0.16), fine = m * vec2(1.05, 0.42);
	float nb = mix(texture(noise_tex, big + o1 * vec2(1.0, 0.16)).r, texture(noise_tex, big + o0 * vec2(1.0, 0.16)).r, w0);
	float nf = mix(texture(noise_tex, fine + o1 * vec2(1.0, 0.42) + 0.5).r, texture(noise_tex, fine + o0 * vec2(1.0, 0.42) + 0.5).r, w0);
	float n = nb * 0.6 + nf * 0.4;

	// depth of the water under this pixel (what's behind it, from the depth buffer, minus the surface)
	float d = texture(depth_tex, SCREEN_UV).r;
	vec4 vb = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, d, 1.0);
	vb.xyz /= vb.w;
	float depth = max(0.0, VERTEX.z - vb.z);                   // metres along the view ray
	float shallow = 1.0 - smoothstep(0.03, 0.55, depth);

	// colour: olive and see-through in the shallows, peat-dark in the deep, the flow's streaks lighter
	float edge = 1.0 - v_centre;
	vec3 deep = mix(deep_color, blood_deep, blood);
	vec3 shal = mix(shallow_color, blood_color, blood);
	vec3 c = mix(deep, shal, shallow * 0.85);
	c *= 0.8 + 0.55 * smoothstep(0.45, 0.8, n);
	// foam: combed streaks along the flow where it runs fast or steep, and a lace at the banks and the rocks
	float turb = clamp(v_steep * 1.6 + 0.25 * v_centre * smoothstep(0.5, 1.5, sp), 0.0, 1.0);
	float streak = smoothstep(0.68 - 0.1 * turb, 0.84 - 0.06 * turb, nf * 0.6 + nb * 0.4);
	float contact = (1.0 - smoothstep(0.02, 0.16 + 0.12 * nf, depth)) * smoothstep(0.35, 0.65, nf + 0.2 * nb);
	float foam = clamp(streak * (0.2 + 0.55 * turb) + contact * 0.9 + edge * edge * 0.15 * smoothstep(0.55, 0.75, nf), 0.0, 1.0);
	foam *= foam_amount * (1.0 - blood);
	c = mix(c, foam_color, foam * 0.75);
	// a leaf or a twig riding the current now and then (cells moving at the middle's speed; one in a few has one)
	vec2 lc = vec2(m.x * 1.4, m.y * 0.9 - TIME * flow_speed * 0.9 * (1.0 - 0.6 * blood));
	vec2 cell = floor(lc), f = fract(lc) - 0.5;
	float hsh = fract(sin(dot(cell, vec2(127.1, 311.7))) * 43758.5453);
	float ang = hsh * 6.28;
	vec2 rf = mat2(vec2(cos(ang), sin(ang)), vec2(-sin(ang), cos(ang))) * (f - (vec2(fract(hsh * 7.3), fract(hsh * 13.1)) - 0.5) * 0.4);
	float leaf = step(0.95, hsh) * (1.0 - smoothstep(0.7, 1.0, length(rf * vec2(7.0, 15.0)))) * v_centre;
	c = mix(c, leaf_color * (0.6 + 0.4 * fract(hsh * 3.7)), leaf * 0.9);

	ALBEDO = c;
	float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);
	// dull overcast sheen on the wave faces, a little brighter where it's churned
	EMISSION = mix(sky_tint, vec3(0.07, 0.01, 0.01), blood) * (0.02 + fres * 0.22) * (0.6 + 0.8 * nf) * (1.0 - foam * 0.5);
	// (not mirror-glossy: a sharp sky reflection turned the whole run into a pale grey sheet at a glance)
	ROUGHNESS = mix(mix(mix(0.22, 0.45, nf), 0.8, foam), 0.14, blood);
	SPECULAR = mix(0.3, 0.7, blood);
	// the ripple normal: the slopes of the fine layer (both blended), stronger where it's rough
	float e = 0.03;
	float fx = mix(texture(noise_tex, fine + o1 * vec2(1.0, 0.42) + 0.5 + vec2(e, 0.0)).r, texture(noise_tex, fine + o0 * vec2(1.0, 0.42) + 0.5 + vec2(e, 0.0)).r, w0) - nf;
	float fy = mix(texture(noise_tex, fine + o1 * vec2(1.0, 0.42) + 0.5 + vec2(0.0, e)).r, texture(noise_tex, fine + o0 * vec2(1.0, 0.42) + 0.5 + vec2(0.0, e)).r, w0) - nf;
	float bump = (2.2 + 3.0 * turb) * (1.0 - 0.55 * blood);
	NORMAL = normalize(NORMAL + (VIEW_MATRIX * vec4(fx * bump, 0.0, fy * bump, 0.0)).xyz);
	// see-through at the very edge, opaque once it's a hand deep (and blood is never clear)
	ALPHA = clamp(mix(0.97, 0.45, shallow * shallow) + foam * 0.5 + blood * 0.5, 0.0, 1.0);
}
