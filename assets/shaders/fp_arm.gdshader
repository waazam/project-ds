// The first-person arms' cloths (2026-10-14f, the owner, a fourth time: "skeletor robot hands"; FirstPersonHand.Dress):
// one shader for the three parts of tools/Blender/fp_arms.py's models, so a hand brings one pipeline, not three.
// (2026-10-14g, the owner: "the player's arms need more texture, and the hands too") Each cloth's detail is sized to
// read at the game's 640x360 (a pixel there is about a millimetre at arm's length): stitches, seams, scratches, creases
// and dirt a few pixels across, never a fine pattern that would only shimmer.
// - kind 0, the work glove's leather: its own baked colour map ((2026-10-15) a charcoal-black winter work glove: rubbed
//   to a worn grey over the knuckles and fingertips, a blacker synthetic grip palm, grime in the palm, the fingertips and
//   the creases, scuffs, scratches, the seams' grey thread) and baked normal map (the folds bunched in the bends, the creases over the knuckles, the stitched seams);
//   over them the leather's pebbled grain at two sizes, the dirt held in its hollows. The vertices carry the baked
//   occlusion (r), how worn it is (g) and the palm (b): the worn places polished, a slight sheen the lantern catches.
//   (2026-10-15) A black glove would vanish into the dark: the leather over the back keeps a low sheen and a rim, so its
//   silhouette and fingers stay drawn under the lantern; the palm's grip panel is matte, finer grained.
// - kind 1, the jacket's knitted storm cuff (2026-10-15: a dark charcoal wool): wool ribs running along the arm (its UVs run round it), its stitches,
//   darker down in the ribs, a little fuzz.
// - kind 2, the jacket's cotton duck sleeve (its UVs: u round the arm, v metres up it from the hem): the weave, slubbed
//   threads, two felled seams (a raised welt, two rows of pale stitches) up the sleeve, the hem's double row of
//   stitching, its edge frayed pale and grimy, fine creases bunched above the cuff and at the elbow, dirt rubbed into
//   the weave. Its colour (sun-faded on top, darker underneath, mud, a grease mark; 2026-10-15 a worn dark navy) is the
//   model's.
// Every normal is in the mesh's own tangent frame (the models carry their tangents), so either hand works.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;

/// 0 the glove's leather, 1 the knitted cuff, 2 the canvas sleeve
uniform int kind = 0;
uniform sampler2D baked_normal : hint_normal, filter_linear_mipmap, repeat_enable;
uniform bool has_baked = false;
uniform float baked_strength = 1.0;
/// The glove's baked colour map (2026-10-14g); without one, the vertices' colours
uniform sampler2D baked_albedo : source_color, filter_linear_mipmap, repeat_enable;
uniform bool has_albedo = false;
/// The leather's grain (a seamless pebbled normal map) and the canvas's slubs
uniform sampler2D grain : hint_normal, filter_linear_mipmap, repeat_enable;
uniform float grain_scale = 24.0;
uniform float grain_strength = 0.22;
uniform vec3 tint : source_color = vec3(1.0);
uniform float roughness = 0.74;
/// The worn, polished places (the knuckles, the fingertips): a slight sheen
uniform float worn_roughness = 0.5;
uniform float specular = 0.24;
uniform float worn_specular = 0.36;
/// (2026-10-15) the black glove's rim: its edges and fingers lit against the dark (more, and with more sheen, and a
/// strong light close by, the flamethrower's pilot, washed the black out silver)
uniform float rim = 0.2;
/// The palm's synthetic grip panel: matte, finer grained
uniform float palm_roughness = 0.86;
uniform float palm_specular = 0.18;
/// The knit's ribs round the cuff and its stitches along it
uniform float ribs = 44.0;
uniform float stitches = 22.0;
/// The sleeve: its girth (metres round, for the weave and the seams' widths) and its seams' places round it (u)
uniform float girth = 0.215;
uniform float seam_a = 0.36;
uniform float seam_b = 0.86;
/// The sleeve's and the leather's colour over the model's. (2026-10-15: neutral now: v5's warm bias, there to keep the
/// olive canvas olive, turned the navy one grey)
uniform vec3 sleeve_warmth = vec3(1.0, 1.0, 1.0);
uniform vec3 leather_warmth = vec3(0.9, 0.9, 0.9);
/// (2026-10-15) How much of the colour is taken out of the light falling on each cloth (light()): a black glove under the
/// lantern's orange fill read tan-brown, under the cool rim grey; with most of the light's colour taken out it reads
/// charcoal under both. The navy sleeve and the cuff likewise (the orange fill turned the blue to a muddy grey).
uniform float glove_neutral = 0.75;
uniform float sleeve_neutral = 0.6;

varying float v_f0;
varying float v_rim;
varying float v_neutral;

float hash1(float x) { return fract(sin(x * 127.1) * 43758.5453); }
float hash2(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
// smooth value noise, 0..1 (the canvas's slubs and its dirt: soft, not the leather grain's hard cells)
float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash2(i), hash2(i + vec2(1.0, 0.0)), u.x), mix(hash2(i + vec2(0.0, 1.0)), hash2(i + vec2(1.0, 1.0)), u.x), u.y);
}

// A felled seam across x (metres from its line): the welt's height, its slope, and how much of it is pale thread.
void felled(float x, float along, out float h, out float dh, out float thread) {
	float w = 0.0034;
	float ax = abs(x);
	// the welt: a raised band, a groove down the middle where the two panels meet
	float band = 1.0 - smoothstep(w * 0.75, w, ax);
	float groove = exp(-pow(x / 0.00045, 2.0));
	h = band * 0.6 - groove * 0.8;
	// (its slope: up out of the groove, down off the welt's edges)
	dh = x / 0.00045 * groove * 1.5 - sign(x) * exp(-pow((ax - w * 0.85) / 0.0006, 2.0)) * 1.4;
	// two rows of stitches, each a few millimetres long, along the welt
	float row = exp(-pow((ax - w * 0.55) / 0.00042, 2.0));
	float dash = smoothstep(0.15, 0.3, fract(along / 0.0042)) * (1.0 - smoothstep(0.75, 0.9, fract(along / 0.0042)));
	thread = row * dash;
}

void fragment() {
	vec2 n = vec2(0.0);
	vec3 c;
	float rough = roughness;
	float spec = specular;
	if (kind == 0) {
		float ao = COLOR.r;
		float wear = COLOR.g;
		float palm = COLOR.b;
		c = has_albedo ? texture(baked_albedo, UV).rgb : COLOR.rgb;
		c *= 0.4 + 0.6 * ao;
		if (has_baked) n = (texture(baked_normal, UV).xy * 2.0 - 1.0) * baked_strength;
		// (the palm's grip panel: a finer, flatter grain)
		vec2 g = texture(grain, UV * grain_scale * (1.0 + 0.8 * palm)).xy * 2.0 - 1.0;
		vec2 g2 = texture(grain, UV * grain_scale * 2.63 + vec2(0.37, 0.71)).xy * 2.0 - 1.0;
		// (the worn places smoothed: less grain where it's polished)
		n += (g + g2 * 0.55) * grain_strength * (1.0 - 0.45 * wear);
		// (the grain's hollows hold the dirt)
		c *= 1.0 - 0.28 * clamp(length(g) - 0.2, 0.0, 1.0);
		rough = mix(mix(roughness, worn_roughness, wear), palm_roughness, palm * 0.8);
		spec = mix(mix(specular, worn_specular, wear), palm_specular, palm * 0.8);
	} else if (kind == 1) {
		c = COLOR.rgb;
		float u = UV.x * ribs;
		float rib = sin(u * 6.2831853);
		float across = abs(fract(u) - 0.5);
		float st = sin(UV.y * stitches * 6.2831853 + across * 7.0);
		n = vec2(cos(u * 6.2831853) * 0.55, st * 0.22);
		vec2 fz = texture(grain, UV * vec2(3.0, 1.5)).xy * 2.0 - 1.0;
		c *= (0.7 + 0.3 * (rib * 0.5 + 0.5)) * (0.92 + 0.12 * fz.x);
		rough = 0.96;
		spec = 0.12;
	} else {
		c = COLOR.rgb;
		float x = UV.x * girth;      // metres round the arm
		float v = UV.y;              // metres up the arm from the hem's edge
		// the weave: a plain duck weave, its threads drawn a little coarse; faded out where it would be finer than a
		// pixel (so it never shimmers), leaving the slubs and the colour
		vec2 wv = vec2(x, v) / 0.0011;
		float fw = fwidth(wv.x) + fwidth(wv.y);
		float wk = 1.0 - smoothstep(0.35, 0.9, fw);
		float cx = cos(wv.x * 3.14159), cy = cos(wv.y * 3.14159);
		float over = sign(sin(wv.x * 1.5708) * sin(wv.y * 1.5708));
		n += vec2(-sin(wv.x * 3.14159) * (0.5 + 0.5 * over), -sin(wv.y * 3.14159) * (0.5 - 0.5 * over)) * 0.08 * wk;
		c *= 1.0 + (cx * cy) * 0.03 * wk;
		// slubs: thicker, uneven threads, streaks a few millimetres to centimetres long each way
		float s1 = vnoise(vec2(x * 30.0, v * 350.0));   // (threads running round the arm, each a little thicker or thinner)
		float s2 = vnoise(vec2(x * 350.0, v * 30.0) + 7.0);   // (and along it)
		float slub = (s1 - 0.5) * 0.5 + (s2 - 0.5) * 0.5;
		c *= 1.0 + slub * 0.16 * (0.4 + 0.6 * (1.0 - smoothstep(0.5, 1.5, fw * 0.3)));
		n += vec2(s2 - 0.5, s1 - 0.5) * 0.12;
		// the seams up the sleeve
		float h, dh, th;
		float sa = (fract(UV.x - seam_a + 0.5) - 0.5) * girth;
		felled(sa, v, h, dh, th);
		float sb = (fract(UV.x - seam_b + 0.5) - 0.5) * girth;
		float h2, dh2, th2;
		felled(sb, v, h2, dh2, th2);
		float above = smoothstep(-0.001, 0.002, v);
		n.x += (dh + dh2) * 0.35 * above;
		c *= 1.0 + (h + h2) * 0.12 * above;            // the welts' crests rubbed paler
		vec3 thread = vec3(0.075, 0.068, 0.046);   // (2026-10-15: a shade darker, with the cloth)
		c = mix(c, thread, clamp(th + th2, 0.0, 1.0) * 0.35 * above);
		// the hem: two rows of stitching round it, and its edge frayed: pale broken threads, grime
		float hr = exp(-pow((v - 0.0075) / 0.0005, 2.0)) + exp(-pow((v - 0.0135) / 0.0005, 2.0));
		float hd = smoothstep(0.15, 0.3, fract(x / 0.0042)) * (1.0 - smoothstep(0.75, 0.9, fract(x / 0.0042)));
		c = mix(c, thread, clamp(hr * hd, 0.0, 1.0) * 0.35);
		float ex = floor(x / 0.0016);
		float strand = hash1(ex) * 0.006;               // how far up each thread's fraying runs
		float fray = (1.0 - smoothstep(0.0, 0.002 + strand, v)) * step(0.45, hash1(ex + 17.0));
		float edge = 1.0 - smoothstep(-0.0015, 0.0045, v);
		c = mix(c, c * 0.55 + vec3(0.012, 0.01, 0.006), edge * 0.6);      // grime at the edge
		c = mix(c, vec3(0.095, 0.087, 0.058), fray * 0.75);               // the frayed threads, pale
		n.y += fray * 0.4 * sign(hash1(ex + 3.0) - 0.5);
		// creases: bunched above the cuff (the first ten centimetres) and at the elbow; fine dark lines that wander
		float bunch = exp(-max(v - 0.01, 0.0) / 0.045) * smoothstep(0.0, 0.008, v);
		float elbow = smoothstep(0.17, 0.2, v) * (1.0 - smoothstep(0.25, 0.27, v));
		float wob = vnoise(vec2(x * 60.0, v * 25.0));
		float cr = sin(v / 0.0085 * 6.2831853 + wob * 7.0 + sin(x / girth * 6.2831853 * 2.0) * 1.8);
		float crease = smoothstep(0.55, 0.95, cr) * (bunch + elbow) * smoothstep(0.2, 0.6, wob + 0.25);
		c *= 1.0 - 0.5 * crease;
		n.y += cos(v / 0.0085 * 6.2831853 + wob * 7.0) * 0.35 * (bunch + elbow) * smoothstep(0.2, 0.6, wob + 0.25);
		// dirt rubbed into the weave in blotches (the model's colour has the big ones)
		float d1 = vnoise(vec2(x, v) * 70.0) * 0.65 + vnoise(vec2(x, v) * 260.0) * 0.35;
		c = mix(c, c * vec3(0.66, 0.6, 0.52), smoothstep(0.55, 0.8, d1) * 0.5);
		rough = 0.88 - 0.1 * crease;
		spec = 0.14;
	}
	// (2026-10-14g: "in some shots the sleeve reads dark navy-black") the held things are lit by their own fills, a cool rim
	// and the sky: the cloths and the leather carry a little extra warmth so they stay olive and brown under that cool
	// light, not grey-blue
	c *= kind == 2 ? sleeve_warmth : (kind == 0 ? leather_warmth : vec3(1.0));
	ALBEDO = c * tint;
	NORMAL_MAP = vec3(clamp(n, vec2(-1.0), vec2(1.0)) * 0.5 + 0.5, 1.0);
	ROUGHNESS = rough;
	// (the cloths dull: a sheen on the canvas washed the sleeve out pale in the sun)
	v_f0 = 0.16 * spec * spec;
	v_rim = kind == 0 ? rim * (1.0 - 0.5 * COLOR.b) : 0.03;
	v_neutral = kind == 0 ? glove_neutral : sleeve_neutral;
}

// (2026-10-15) The engine's own lighting (Lambert diffuse, GGX specular, its rim), but with part of each light's colour
// taken out first (v_neutral): so the black glove stays charcoal under the lantern's orange and the cool rim alike.
void light() {
	vec3 lc = LIGHT_COLOR * ATTENUATION;
	float luma = dot(lc, vec3(0.2126, 0.7152, 0.0722));
	// (a strong light close by keeps its colour: the lantern an inch under the hand, taken grey, clipped to a cream-white)
	lc = mix(lc, vec3(luma), v_neutral * (1.0 - smoothstep(8.0, 24.0, luma)));
	float ndl = max(dot(NORMAL, LIGHT), 0.0);
	float ndv = max(dot(NORMAL, VIEW), 1e-4);
	DIFFUSE_LIGHT += lc * ndl / PI;
	// the rim: grazing light round the silhouette, a little of the albedo's colour in it
	float rimv = pow(max(1e-4, 1.0 - ndv), max(0.0, (1.0 - ROUGHNESS) * 16.0));
	DIFFUSE_LIGHT += rimv * v_rim * mix(vec3(1.0), ALBEDO, 0.6) * lc / PI;
	// GGX
	vec3 H = normalize(LIGHT + VIEW);
	float ndh = max(dot(NORMAL, H), 0.0);
	float ldh = max(dot(LIGHT, H), 0.0);
	float a = max(ROUGHNESS * ROUGHNESS, 0.002);
	float a2 = a * a;
	float dd = ndh * ndh * (a2 - 1.0) + 1.0;
	float D = a2 / (PI * dd * dd);
	float G = 0.5 / (ndl * (ndv * (1.0 - a) + a) + ndv * (ndl * (1.0 - a) + a) + 1e-5);
	float F = v_f0 + (1.0 - v_f0) * pow(1.0 - ldh, 5.0);
	SPECULAR_LIGHT += lc * ndl * D * G * F * SPECULAR_AMOUNT;
}
