// (2026-10-22, the style pass) The red beat: at a near miss or a hard scare the world drains to grey for a heartbeat,
// all but its reds (blood, the red light, the lantern's ember), which burn on; the edges close in and pulse once with the
// heart. amount 0..1 (StyleFx eases it in fast and out slow); pulse 0..1 the heartbeat's throb.
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform float amount : hint_range(0.0, 1.0) = 0.0;
uniform float pulse : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec3 c = texture(screen_tex, SCREEN_UV).rgb;
	float l = dot(c, vec3(0.3, 0.59, 0.11));
	// how red it is: red over the others, and saturated enough to count
	float mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b));
	float sat = (mx - mn) / max(mx, 1e-4);
	float redness = smoothstep(0.25, 0.6, sat) * smoothstep(0.0, 0.08, c.r - max(c.g, c.b));
	vec3 grey = vec3(l) * vec3(0.92, 0.95, 1.0);
	vec3 keep = c * vec3(1.25, 0.9, 0.9);
	vec3 graded = mix(grey, keep, redness);
	// the edges close in, darker on the beat
	vec2 d = SCREEN_UV - 0.5;
	float v = dot(d, d);
	graded *= 1.0 - v * (0.9 + 1.4 * pulse);
	graded += vec3(0.12, 0.0, 0.0) * smoothstep(0.12, 0.35, v) * pulse;
	COLOR = vec4(mix(c, graded, amount), 1.0);
}
