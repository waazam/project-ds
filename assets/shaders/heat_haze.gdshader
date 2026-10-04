// The air shimmering over a fire (2026-10-04, the owner: "make everything stand out more"): a card stood over the
// flames, turned to the camera round its upright axis, that draws what's behind it a pixel or two out of place, the
// waver rising with the heat. Faded at its edges and thinning upward; small, slow and soft (never a strobe, never a
// shake).
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, fog_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform sampler2D noise_tex : repeat_enable, filter_linear;
uniform float strength = 0.0035;
uniform float intensity = 1.0;

void vertex() {
	// (turned to the camera round its own upright axis)
	vec3 up = normalize(MODEL_MATRIX[1].xyz);
	vec3 right = normalize(cross(up, normalize(INV_VIEW_MATRIX[2].xyz)));
	vec3 fwd = cross(right, up);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(vec4(right * length(MODEL_MATRIX[0].xyz), 0.0), vec4(MODEL_MATRIX[1].xyz, 0.0), vec4(fwd, 0.0), MODEL_MATRIX[3]);
}

void fragment() {
	vec2 n = texture(noise_tex, vec2(UV.x * 1.3, UV.y * 0.9 + TIME * 0.32)).rg - 0.5;
	// (UV.y runs 0 at the card's top to 1 at its foot: eased in over the flames' tips, thinning away upward)
	float fade = smoothstep(0.0, 0.3, UV.x) * smoothstep(1.0, 0.7, UV.x) * smoothstep(1.0, 0.75, UV.y) * smoothstep(0.0, 0.5, UV.y);
	vec2 off = n * strength * fade * intensity;
	ALBEDO = textureLod(screen_tex, SCREEN_UV + off, 0.0).rgb;
	ALPHA = fade * intensity;
}
