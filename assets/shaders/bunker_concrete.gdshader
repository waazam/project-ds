// Bunker concrete (hallway, CRT room, maze, the outside facade).
// - World-space triplanar mapping of a low-res texture: every face, curved or
//   flat, gets the same metre scale, so nothing smears or stretches.
// - Vertex colour tints; vertex alpha is dryness (0 = standing water: darker
//   and glossy, so wet floor strips catch the lights).
// - Grime from a second low-frequency texture, heavier near the floor.
// - ambient_floor: a faint self-light so walls still read in near darkness
//   (fogged like everything else).
// - breathe: an optional slow warp of the maze walls ("distorted realities").
//   The offset depends only on world position, so faces never crack apart.
shader_type spatial;
render_mode diffuse_lambert, specular_schlick_ggx;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D grime_tex : filter_linear_mipmap, repeat_enable;
// (2026-10-13g, the texture-mood pass) the scan's relief and roughness, so the walls have the early acts' weathered
// depth under the lantern instead of a flat print of concrete; off (0) when there is no scan
uniform sampler2D nor_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rough_tex : filter_linear_mipmap, repeat_enable;
uniform float normal_strength = 0.0;
uniform float rough_map = 0.0;
uniform float tile = 0.5;
uniform float grime_tile = 0.11;
uniform float grime_amount = 0.5;
uniform vec3 tint : source_color = vec3(1.0);
uniform float ambient_floor = 0.05;
uniform float base_rough = 0.92;
uniform float wet_rough = 0.14;
uniform float low_grime = 0.35;
uniform float breathe = 0.0;
uniform float breathe_speed = 0.3;

varying vec3 wpos;
varying vec3 wnrm;
varying float lheight;

void vertex() {
	vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	if (breathe > 0.0) {
		float h = smoothstep(0.1, 2.6, VERTEX.y);
		float a = TIME * breathe_speed + wp.x * 0.37 + wp.z * 0.29;
		float b = TIME * breathe_speed * 0.71 + wp.z * 0.41 - wp.x * 0.23;
		VERTEX += vec3(sin(a), 0.0, cos(b)) * breathe * h;
		wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	}
	wpos = wp;
	wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	lheight = VERTEX.y;
}

vec3 tri_normal(sampler2D t, vec3 p, vec3 n, float s, float k) {
	vec3 w = pow(abs(n), vec3(6.0));
	w /= (w.x + w.y + w.z);
	vec3 nx = texture(t, p.zy * s).xyz * 2.0 - 1.0;
	vec3 ny = texture(t, p.xz * s).xyz * 2.0 - 1.0;
	vec3 nz = texture(t, p.xy * s).xyz * 2.0 - 1.0;
	nx.xy *= k; ny.xy *= k; nz.xy *= k;
	vec3 tx = vec3(nx.xy + n.zy, abs(nx.z) * n.x);
	vec3 ty = vec3(ny.xy + n.xz, abs(ny.z) * n.y);
	vec3 tz = vec3(nz.xy + n.xy, abs(nz.z) * n.z);
	return normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
}

vec3 triplanar(sampler2D t, vec3 p, vec3 n, float s) {
	vec3 w = pow(abs(n), vec3(6.0));
	w /= (w.x + w.y + w.z);
	return texture(t, p.zy * s).rgb * w.x
		+ texture(t, p.xz * s).rgb * w.y
		+ texture(t, p.xy * s).rgb * w.z;
}

void fragment() {
	vec3 n = normalize(wnrm);
	vec3 c = triplanar(albedo_tex, wpos, n, tile);
	float g = triplanar(grime_tex, wpos + vec3(7.3, 0.0, 3.1), n, grime_tile).r;
	float wet = 1.0 - COLOR.a;
	float wall = 1.0 - abs(n.y);
	float low = 1.0 - smoothstep(0.0, 0.55, lheight);
	vec3 alb = c * COLOR.rgb * tint;
	alb *= mix(1.0, 0.55 + g * 0.7, grime_amount);
	alb *= 1.0 - low_grime * low * wall;
	alb *= mix(1.0, 0.45, wet);
	ALBEDO = alb;
	float r = base_rough;
	if (rough_map > 0.0) r = mix(base_rough, clamp(triplanar(rough_tex, wpos, n, tile).r * 1.05 + 0.08, 0.55, 1.0), rough_map);
	ROUGHNESS = mix(r, wet_rough, wet);
	if (normal_strength > 0.0) {
		vec3 wn = tri_normal(nor_tex, wpos, n, tile, normal_strength);
		NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
	}
	SPECULAR = mix(0.3, 0.7, wet);
	EMISSION = alb * ambient_floor;
}
