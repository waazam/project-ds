// A faint cold rim round a creature's silhouette (2026-10-04, the owner: "make everything stand out more"), drawn as a
// material overlay over its own: where its surface turns away from the eye, a little cold light is added, so its outline
// separates from the dark and the fog behind it. It doesn't light the creature; dim, never a glow or a glare. Faded out
// up close (in the face, a rim reads as a halo) and with distance into the fog (where the fog should keep it).
shader_type spatial;
render_mode blend_add, unshaded, depth_draw_never, depth_test_default, cull_back, fog_disabled;

uniform vec3 rim_color : source_color = vec3(0.55, 0.62, 0.75);
uniform float strength = 0.09;
uniform float power = 3.0;
uniform float near_fade = 1.2;
uniform float far_fade = 40.0;

void fragment() {
	float f = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), power);
	float dist = length(VERTEX);
	float fade = smoothstep(near_fade * 0.5, near_fade * 2.0, dist) * (1.0 - smoothstep(far_fade * 0.5, far_fade, dist));
	ALBEDO = rim_color * f * strength * fade;
}
