// The earth heaped over the bunker: the forest floor's own textures (leaf
// litter and moss on top, bare dirt on steep faces), mapped in world space at
// the terrain's scale so the mound's skirt disappears into the ground around it.
// Vertex colour carries shade (darker toward the skirt, like the terrain's
// canopy shade).
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;

uniform sampler2D litter_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D dirt_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D moss_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform float tile = 0.42;
uniform float litter_tile = 0.6;

varying vec3 wpos;
varying vec3 wnrm;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 n = normalize(wnrm);
	vec3 w = pow(abs(n), vec3(4.0));
	w /= (w.x + w.y + w.z);
	vec3 dirt = texture(dirt_tex, wpos.zy * tile).rgb * w.x
		+ texture(dirt_tex, wpos.xz * tile).rgb * w.y
		+ texture(dirt_tex, wpos.xy * tile).rgb * w.z;
	vec3 litter = texture(litter_tex, wpos.xz * litter_tile).rgb;
	vec3 moss = texture(moss_tex, wpos.xz * tile).rgb;
	float nz = texture(noise_tex, wpos.xz * 0.07).r;
	float up = smoothstep(0.22, 0.6, n.y);   // litter and moss cling to all but the steepest faces
	vec3 top = mix(litter, moss * 0.9, smoothstep(0.45, 0.68, nz) * 0.75);
	ALBEDO = mix(dirt * 0.75, top, up) * COLOR.rgb;
	ROUGHNESS = 1.0;
}
