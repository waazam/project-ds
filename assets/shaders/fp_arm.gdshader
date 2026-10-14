// The first-person arms' cloths (2026-10-14f, the owner, a fourth time: "skeletor robot hands"; FirstPersonHand.Dress):
// one shader for the three parts of tools/Blender/fp_arms.py's models, so a hand brings one pipeline, not three.
// - kind 0, the work glove's leather: the model's own baked normal map (its folds bunched in the bends, the wrinkles
//   over the knuckles, the stitched seams), and over it the leather's pebbled grain at two sizes; its colour the
//   model's (worn paler over the knuckles and fingertips, dark in the folds and seams, its occlusion baked in); the worn
//   places polished a little smoother than the rest, so the lantern catches the knuckles, not the whole hand.
// - kind 1, the jacket's knitted storm cuff: wool ribs running along the arm (its UVs run round it), its stitches,
//   darker down in the ribs.
// - kind 2, the jacket's waxed canvas sleeve: faint slubs, dull.
// Every normal is in the mesh's own tangent frame (the models carry their tangents), so either hand works.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

/// 0 the glove's leather, 1 the knitted cuff, 2 the canvas sleeve
uniform int kind = 0;
uniform sampler2D baked_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform bool has_baked = false;
uniform float baked_strength = 1.0;
/// The leather's grain (a seamless pebbled normal map) and the canvas's slubs
uniform sampler2D grain : hint_normal, filter_linear_mipmap, repeat_enable;
uniform float grain_scale = 24.0;
uniform float grain_strength = 0.14;
uniform vec3 tint : source_color = vec3(1.0);
/// The leather a shade darker than the model's colours (the early acts' muted, cold look)
uniform float leather_shade = 0.68;
uniform float roughness = 0.74;
/// The worn, paler places (the knuckles, the fingertips), handled smooth
uniform float worn_roughness = 0.52;
uniform float specular = 0.38;
uniform float rim = 0.14;
/// The knit's ribs round the cuff and its stitches along it
uniform float ribs = 44.0;
uniform float stitches = 22.0;

void fragment() {
	vec3 c = COLOR.rgb * tint;
	vec2 n = vec2(0.0);
	float rough = roughness;
	if (kind == 0) {
		if (has_baked) n = (texture(baked_normal, UV).xy * 2.0 - 1.0) * baked_strength;
		vec2 g = texture(grain, UV * grain_scale).xy * 2.0 - 1.0;
		vec2 g2 = texture(grain, UV * grain_scale * 2.63 + vec2(0.37, 0.71)).xy * 2.0 - 1.0;
		n += (g + g2 * 0.55) * grain_strength;
		float lum = dot(COLOR.rgb, vec3(0.3, 0.59, 0.11));
		float worn = smoothstep(0.05, 0.11, lum);
		c *= leather_shade;
		rough = mix(roughness, worn_roughness, worn);
		// (the grain's hollows hold the dirt)
		c *= 1.0 - 0.18 * clamp(length(g) - 0.25, 0.0, 1.0);
	} else if (kind == 1) {
		float u = UV.x * ribs;
		float rib = sin(u * 6.2831853);
		float across = abs(fract(u) - 0.5);
		float st = sin(UV.y * stitches * 6.2831853 + across * 7.0);
		n = vec2(cos(u * 6.2831853) * 0.55, st * 0.22);
		c *= 0.72 + 0.28 * (rib * 0.5 + 0.5);
		rough = 0.96;
	} else {
		vec2 g = texture(grain, UV * vec2(5.0, 9.0)).xy * 2.0 - 1.0;
		n = g * 0.07;
		c *= 0.94 + 0.12 * g.x;
		rough = 0.86;
	}
	ALBEDO = c;
	NORMAL_MAP = vec3(n * 0.5 + 0.5, 1.0);
	ROUGHNESS = rough;
	// (the cloths dull: a sheen on the canvas washed the sleeve out pale in the sun)
	SPECULAR = kind == 0 ? specular : 0.12;
	RIM = kind == 0 ? rim : 0.04;
	RIM_TINT = 0.5;
}
