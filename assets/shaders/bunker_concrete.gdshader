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
	ROUGHNESS = mix(base_rough, wet_rough, wet);
	SPECULAR = mix(0.3, 0.7, wet);
	EMISSION = alb * ambient_floor;
}
