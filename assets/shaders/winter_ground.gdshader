// Act 22's ground: the winter woods' snow, the snowplow's road and the frozen crust near the lodge
// (Poly Haven's snow textures, made small and cold by tools/Textures/make_snow.py), tiled in world space.
// Vertex colour: R = the plowed road, A = frozen (the glazed, icy crust near the lodge); G = a little
// shade under the trees. A faint sheen only where it has frozen (the ice), never a glare.
shader_type spatial;
render_mode diffuse_lambert, specular_schlick_ggx;

uniform sampler2D tex_snow : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_plowed : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_packed : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D nor_snow : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D nor_plowed : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D detail_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float tile = 0.28;
uniform float road_tile = 0.36;
uniform float detail_tile = 1.6;

varying vec3 wpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 uv = wpos.xz * tile;
	// two scales of the snow, so its tiling never lines up
	vec3 snow = texture(tex_snow, uv).rgb * 0.6 + texture(tex_snow, uv * 0.37 + vec2(0.31, 0.17)).rgb * 0.4;
	vec3 road = texture(tex_plowed, wpos.xz * road_tile).rgb;
	vec3 packed = texture(tex_packed, uv * 1.3).rgb;
	float r = COLOR.r, ice = COLOR.a;
	vec3 c = mix(snow, road, r);
	c = mix(c, mix(packed, road * 0.85 + packed * 0.15, r), ice * 0.85);
	float near = clamp(1.0 - length(VERTEX) / 30.0, 0.0, 1.0);
	c *= mix(1.0, texture(detail_tex, wpos.xz * detail_tile).r, near);
	c *= 1.0 - COLOR.g * 0.35;
	ALBEDO = c;
	vec3 n1 = texture(nor_snow, uv).rgb;
	vec3 n2 = texture(nor_plowed, wpos.xz * road_tile).rgb;
	NORMAL_MAP = mix(n1, n2, r);
	NORMAL_MAP_DEPTH = 0.6;
	ROUGHNESS = mix(0.92, 0.38, ice);
	SPECULAR = mix(0.15, 0.4, ice);
}
