// Act 11's spotlights (2026-10-29): the shaft of a light beaming straight down from above, drawn as a soft additive
// cone. Brightest down its middle and toward the ground, fading out up into the dark and at its silhouette, so it
// reads as lit air, not as a glass tube. Kept out of the fog (it would brighten it) and faded with distance by hand.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled, shadows_disabled;

uniform vec4 colour : source_color = vec4(0.3, 0.5, 1.0, 1.0);
uniform float strength = 0.35;
uniform float height = 13.0;
uniform float far_fade = 140.0;

varying float up;
varying float dist;

void vertex() {
	up = clamp(VERTEX.y / height + 0.5, 0.0, 1.0);
	dist = length((MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz);
}

void fragment() {
	float face = abs(dot(normalize(NORMAL), normalize(VIEW)));
	float core = pow(face, 2.2);
	float along = mix(1.0, 0.15, smoothstep(0.0, 1.0, up)) * smoothstep(1.0, 0.85, up);
	float fade = 1.0 - smoothstep(far_fade * 0.5, far_fade, dist);
	// a touch of slow drift in it, as if dust turned in the light
	float drift = 0.85 + 0.15 * sin(TIME * 0.7 + up * 9.0 + face * 4.0);
	ALBEDO = colour.rgb * strength * core * along * fade * drift;
}
