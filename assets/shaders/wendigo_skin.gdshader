// The wendigo's hide (2026-10-04, the owner: "figuring out incredible shaders for our monsters (bosses) so they look
// imposing"). Its baked albedo and normal maps (tools/Blender/wendigo.py), and over them:
// - light that falls off hard: a lit side and a shadowed side with a sharp edge between (wrapped a little so the edge
//   isn't a cut), so the gaunt body reads as ribs and hollows, a starved thing carved out of the dark;
// - hoarfrost settled on everything facing up (its shoulders, the tops of its arms, its back), a pale rough crust;
// - frostbite: its hands and feet gone black and wet, creeping up the limbs (the lore's frostbitten extremities);
// - a cold, wet glint where light catches the stretched skin.
// Nothing flashes; nothing glows but what already did.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform float roughness = 0.72;
uniform float frost = 0.45;
uniform vec3 frost_color : source_color = vec3(0.7, 0.75, 0.82);
/// Frostbite from the model's feet up (metres, model space): black below `bite_low`, fading out by `bite_high`; the
/// hands hang down to about a metre and a half, so they go too.
uniform float bite_low = 0.55;
uniform float bite_high = 1.75;
uniform vec3 bite_color : source_color = vec3(0.035, 0.03, 0.04);
/// How hard the light's edge is (0 soft as usual .. 1 a sharp terminator).
uniform float hard_light = 0.75;
uniform float glint = 0.35;

varying vec3 v_model;
varying vec3 v_world_n;

void vertex() {
	v_model = VERTEX;
	v_world_n = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 col = texture(albedo_tex, UV).rgb;
	float n = texture(noise_tex, UV * 6.0).r;
	float n2 = texture(noise_tex, UV * 19.0 + vec2(0.3, 0.7)).r;
	// frostbite: black, glossy, ragged at its edge
	float bite = 1.0 - smoothstep(bite_low, bite_high, v_model.y + (n - 0.5) * 0.5);
	col = mix(col, bite_color, bite * 0.85);
	// hoarfrost: on what faces up, in a broken crust
	float up = clamp(v_world_n.y, 0.0, 1.0);
	float crust = smoothstep(0.35, 0.75, up + (n2 - 0.5) * 0.45) * frost * (1.0 - bite * 0.7);
	col = mix(col, frost_color * (0.85 + 0.25 * n2), crust * 0.75);
	ALBEDO = col;
	NORMAL_MAP = texture(normal_tex, UV).rgb;
	NORMAL_MAP_DEPTH = 1.2;
	ROUGHNESS = mix(mix(roughness, 0.25, bite), 0.95, crust);
	SPECULAR = mix(0.4, 0.6, bite);
}

void light() {
	// the hard terminator: the lambert term pushed toward a step, wrapped a touch
	float nl = dot(NORMAL, LIGHT);
	float soft = clamp(nl, 0.0, 1.0);
	float hard = smoothstep(-0.04, 0.22, nl) * (0.55 + 0.45 * soft);
	DIFFUSE_LIGHT += mix(soft, hard, hard_light) * ATTENUATION * LIGHT_COLOR / PI;
	// a cold wet glint off the stretched skin (and the frostbite most of all)
	vec3 h = normalize(VIEW + LIGHT);
	float sp = pow(clamp(dot(NORMAL, h), 0.0, 1.0), mix(18.0, 64.0, 1.0 - ROUGHNESS)) * (1.0 - ROUGHNESS) * glint;
	SPECULAR_LIGHT += sp * ATTENUATION * LIGHT_COLOR * step(0.0, nl);
}
