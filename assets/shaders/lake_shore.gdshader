// The ground around Act 12's lake: dark wet mud at the waterline, a pale gravel beach where the
// vertex colour says so (the two landing beaches), grass and needle duff on the banks with moss in
// the damp hollows, and bare rock wherever the bank is steep. World-space tiled, like terrain.gdshader.
// Vertex colour: r = beach (gravel), g = canopy shade baked from the treeline (1 open, lower under trees).
shader_type spatial;
render_mode diffuse_lambert;

uniform sampler2D tex_grass : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_floor : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_moss : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_dirt : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_gravel : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_rock : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_noise : filter_linear_mipmap, repeat_enable;
uniform float water_level = 0.0;
uniform float tile = 0.42;
uniform vec3 mud_color : source_color = vec3(0.12, 0.095, 0.07);

varying vec3 wpos;
varying vec3 wnrm;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec2 uv = wpos.xz * tile;
	float y = wpos.y - water_level;
	float n_big = texture(tex_noise, wpos.xz * 0.02).r;
	float n_mid = texture(tex_noise, wpos.xz * 0.07 + vec2(0.31, 0.17)).r;
	float n_fine = texture(tex_noise, wpos.xz * 0.3 + vec2(0.7, 0.4)).r;

	vec3 grass = texture(tex_grass, uv * 1.3).rgb;
	vec3 duff = texture(tex_floor, uv).rgb;
	vec3 c = mix(grass, duff, smoothstep(0.35, 0.7, n_big + (1.0 - COLOR.g) * 0.8));
	c = mix(c, texture(tex_moss, uv * 0.9).rgb, smoothstep(0.6, 0.8, n_mid + (1.0 - COLOR.g) * 0.3) * 0.8);

	// the shore band: mud right at the water, gravel on the beaches, fading up into the grass
	float band = 1.0 - smoothstep(0.35, 0.9 + n_fine * 0.4, y);
	vec3 mud = mud_color * (0.75 + 0.5 * texture(tex_dirt, uv * 2.0).r);
	vec3 shore = mix(mud, texture(tex_gravel, uv * 1.8).rgb * 0.95, COLOR.r);
	c = mix(c, shore, band);

	float slope = 1.0 - clamp(wnrm.y, 0.0, 1.0);
	vec3 rock = mix(texture(tex_rock, uv * 0.7).rgb, texture(tex_moss, uv).rgb, smoothstep(0.45, 0.65, n_mid) * 0.5);
	c = mix(c, rock, smoothstep(0.32, 0.5, slope) * 0.9);

	// wet: darker and glossier the nearer the waterline
	float wet = 1.0 - smoothstep(-0.05, 0.3 + n_fine * 0.15, y);
	c *= mix(1.0, 0.55, wet);
	ALBEDO = c * mix(0.75, 1.0, COLOR.g);
	ROUGHNESS = mix(0.95, 0.35, wet);
	SPECULAR = mix(0.15, 0.45, wet);
}
