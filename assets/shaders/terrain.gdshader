// Forest ground: world-space tiled low-res textures blended by a splat mask
// (R dirt trail, G gravel, B grass, A stream mud) and by slope (rock).
// Vertex colour carries baked canopy shade.
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;

uniform sampler2D tex_floor : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_grass : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_dirt : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_gravel : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_rock : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D mask : filter_linear_mipmap, repeat_disable;
uniform vec2 mask_origin = vec2(-120.0, -360.0);
uniform vec2 mask_size = vec2(240.0, 420.0);
uniform float tile = 0.42;
uniform vec3 mud_color : source_color = vec3(0.12, 0.10, 0.08);

varying vec3 wpos;
varying vec3 wnrm;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec2 uv = wpos.xz * tile;
	vec4 m = texture(mask, (wpos.xz - mask_origin) / mask_size);
	vec3 c = texture(tex_floor, uv).rgb;
	c = mix(c, texture(tex_grass, uv * 1.4).rgb, m.b);
	float slope = 1.0 - clamp(wnrm.y, 0.0, 1.0);
	c = mix(c, texture(tex_rock, uv * 0.7).rgb, smoothstep(0.30, 0.48, slope) * 0.85);
	c = mix(c, mud_color * (0.8 + 0.4 * texture(tex_dirt, uv * 2.0).r), m.a * 0.85);
	c = mix(c, texture(tex_gravel, uv * 1.7).rgb, m.g);
	c = mix(c, texture(tex_dirt, uv * 1.25).rgb, m.r);
	// canopy shade is baked into the vertex colour, but open ground (trail, gravel, grass) stays lighter
	float open_ground = max(max(m.r, m.g), m.b);
	float shade = mix(COLOR.r, 1.0, open_ground * 0.7);
	ALBEDO = c * shade;
	ROUGHNESS = 1.0;
}
