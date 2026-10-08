// Frost at the screen's edges (Act 24: the wendigo near): feathery ice crystals growing in from the corners and the
// sides, pale blue-white, the middle of the view always clear. Its reach follows `amount` (eased by its owner, never
// stepped); the crystals themselves are still: nothing in it moves on its own, nothing flashes.
shader_type canvas_item;

uniform float amount : hint_range(0.0, 1.0) = 0.0;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec2 aspect = vec2(1.7778, 1.0);

void fragment() {
	vec2 uv = UV;
	vec2 d = (uv - 0.5) * aspect;
	float edge = length(d * vec2(0.85, 1.0)) / length(0.5 * aspect * vec2(0.85, 1.0));   // 0 middle .. 1 corners
	// the crystals: feathery, from two scales of noise, sharpened into ridges
	float n1 = texture(noise_tex, uv * aspect * 1.6).r;
	float n2 = texture(noise_tex, uv * aspect * 5.0 + 0.37).r;
	float ridge = 1.0 - abs(n2 * 2.0 - 1.0);
	float crystal = smoothstep(0.55, 0.95, ridge) * 0.6 + n1 * 0.4;
	// how far in it reaches: further where the noise says (ragged), never past the middle third
	float reach = amount * 0.62;
	float frost = smoothstep(1.0 - reach - 0.12, 1.0 - reach + 0.18, edge + (n1 - 0.5) * 0.22);
	float a = frost * (0.35 + 0.65 * crystal) * amount;
	COLOR = vec4(mix(vec3(0.55, 0.68, 0.8), vec3(0.85, 0.92, 0.98), crystal), a * 0.75);
}
