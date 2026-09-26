// The thing in the lake (Act 12) and in the pit (Act 18): wet, grimy octopus flesh, after the owner's
// references. Maroon-crimson on the underside where the suckers are, a slick green-teal down the back,
// dark veins wandering through it, grime settled in the folds, and a gooey, uneven slime over all of it
// (glossy clear-coat in wet patches, duller where it has dried), with a little light coming through the
// thinner flesh. Bumpy: the surface normal is pushed about by the same noise, so it catches light like
// something alive rather than a smooth pipe.
//
// `rot` (0..1) is the pit fight's decay (was rot_skin.gdshader): the hide goes mottled grey-green, dull,
// opens into wet sores, and at the end sloughs off to something pale underneath.
// Everything is in object space, so the pattern stays put on each moving limb.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform float scale = 0.9;
/// Object-space direction of the back (the green side): the suckers are on the other side.
uniform vec3 back_dir = vec3(0.0, 0.0, -1.0);
uniform float back_amount : hint_range(0.0, 1.0) = 0.7;
uniform vec4 flesh : source_color = vec4(0.40, 0.08, 0.11, 1.0);
uniform vec4 flesh_dark : source_color = vec4(0.16, 0.02, 0.04, 1.0);
uniform vec4 back : source_color = vec4(0.04, 0.13, 0.11, 1.0);
uniform vec4 grime : source_color = vec4(0.07, 0.05, 0.03, 1.0);
uniform float bump = 0.55;

uniform float rot : hint_range(0.0, 1.0) = 0.0;
uniform vec4 rotten : source_color = vec4(0.33, 0.36, 0.24, 1.0);
uniform vec4 dead : source_color = vec4(0.13, 0.11, 0.09, 1.0);
uniform vec4 sore : source_color = vec4(0.42, 0.04, 0.04, 1.0);
uniform vec4 bone : source_color = vec4(0.78, 0.74, 0.62, 1.0);

varying vec3 v_obj;
varying vec3 v_nrm;

void vertex() {
	v_obj = VERTEX;
	v_nrm = NORMAL;
}

// noise projected three ways and blended by the surface's facing (no stripes on any side)
float tri(vec3 p, vec3 w, float s, vec2 off) {
	return texture(noise_tex, p.yz * s + off).r * w.x + texture(noise_tex, p.xz * s + off.yx).r * w.y + texture(noise_tex, p.xy * s + off * 1.7).r * w.z;
}

float field(vec3 p, vec3 w) {
	return tri(p, w, scale, vec2(0.0)) * 0.55 + tri(p, w, scale * 2.3, vec2(0.31, 0.77)) * 0.3 + tri(p, w, scale * 5.1, vec2(0.13)) * 0.15;
}

void fragment() {
	vec3 p = v_obj;
	vec3 w = pow(abs(normalize(v_nrm)), vec3(4.0));
	w /= (w.x + w.y + w.z + 1e-4);
	float n1 = tri(p, w, scale, vec2(0.0));
	float n2 = tri(p, w, scale * 2.3, vec2(0.31, 0.77));
	float n3 = tri(p, w, scale * 4.1, vec2(0.13, 0.5));
	float n4 = tri(p, w, scale * 9.0, vec2(0.57, 0.21));

	// underside flesh: maroon, darker in blotches, veined
	vec3 col = mix(flesh.rgb, flesh_dark.rgb, smoothstep(0.35, 0.85, n1) * 0.7) * (0.85 + 0.3 * n4);
	float vein = 1.0 - smoothstep(0.0, 0.03, abs(n2 - 0.5));
	float vein2 = 1.0 - smoothstep(0.0, 0.02, abs(n3 - 0.45));
	col = mix(col, flesh_dark.rgb * 0.45, max(vein, vein2 * 0.7) * 0.85);
	// pale, raw-looking patches where the skin is stretched thin
	col = mix(col, flesh.rgb * vec3(1.6, 1.25, 1.2), smoothstep(0.66, 0.8, n4) * 0.35);
	// the back: slick green-teal, shading into the red down the flanks
	float dorsal = smoothstep(-0.25, 0.55, dot(normalize(v_nrm), normalize(back_dir))) * back_amount;
	vec3 backCol = back.rgb * (0.65 + 0.7 * n3) + vec3(0.02, 0.05, 0.03) * n4;
	col = mix(col, backCol, dorsal);
	// grime settled in the folds
	float g = smoothstep(0.58, 0.82, n1 * 0.6 + n3 * 0.4);
	col = mix(col, grime.rgb, g * 0.65);

	// the pit's rot
	float r1 = smoothstep(0.0, 0.55, rot);
	float mottle = n1 * 0.6 + n2 * 0.4;
	vec3 rotted = mix(rotten.rgb, dead.rgb, smoothstep(0.45, 0.75, mottle)) * (0.8 + 0.4 * n3);
	col = mix(col, rotted, r1 * smoothstep(0.25 - rot * 0.4, 0.55, mottle + rot * 0.3));
	float sorePat = smoothstep(0.64 - rot * 0.16, 0.7 - rot * 0.14, n2) * smoothstep(0.1, 0.4, rot);
	col = mix(col, sore.rgb * (0.7 + 0.5 * n1), sorePat);
	float slough = smoothstep(0.72, 0.78, n1 * 0.5 + n3 * 0.5 + (1.0 - rot) * 0.6) * smoothstep(0.6, 1.0, rot);
	col = mix(col, bone.rgb * (0.8 + 0.3 * n2), slough);

	// slime: wet patches glossy, the rest duller; the rot dries it out
	float slime = smoothstep(0.35, 0.7, n3 * 0.6 + n4 * 0.4) * (1.0 - r1 * 0.7);

	// bumps: push the normal about by the noise's slope (object space, carried into view space)
	float e = 0.04;
	float f0 = field(p, w);
	vec3 grad = vec3(field(p + vec3(e, 0.0, 0.0), w) - f0, field(p + vec3(0.0, e, 0.0), w) - f0, field(p + vec3(0.0, 0.0, e), w) - f0) / e;
	vec3 gv = (VIEW_MATRIX * (MODEL_MATRIX * vec4(grad, 0.0))).xyz;
	NORMAL = normalize(NORMAL - gv * bump * 0.06);

	ALBEDO = col;
	ROUGHNESS = mix(0.42, 0.06, slime) + r1 * 0.3;
	SPECULAR = 0.6 + sorePat * 0.3;
	CLEARCOAT = slime;
	CLEARCOAT_ROUGHNESS = 0.08;
	RIM = mix(0.7, 0.2, r1);
	RIM_TINT = 0.6;
	// thin flesh: a little light comes through, red
	BACKLIGHT = flesh.rgb * 0.45 * (1.0 - dorsal) * (1.0 - r1 * 0.6);
}
