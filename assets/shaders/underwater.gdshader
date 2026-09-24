// Under the surface (Act 12's lake, Act 13's flooded room): the world seen through murky water.
// A regrade toward the water's colour that deepens with `amount`, a slow wobble of the image, the
// edges falling off into darkness, and drifting motes. Sits one layer under the PS2 post, like the
// breach's red. Nothing here flashes: every term is slow and continuous.
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear;
uniform float amount : hint_range(0.0, 1.0) = 0.0;
uniform vec3 water_color : source_color = vec3(0.06, 0.1, 0.09);
/// 0..1: how dark it has got (sinking, running out of air)
uniform float darkness : hint_range(0.0, 1.0) = 0.0;

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

void fragment() {
	vec2 uv = SCREEN_UV;
	float t = TIME;
	vec2 wob = vec2(sin(uv.y * 9.0 + t * 1.3), cos(uv.x * 7.0 + t * 1.1)) * 0.006 * amount;
	vec3 c = texture(screen_tex, uv + wob).rgb;
	float l = dot(c, vec3(0.3, 0.59, 0.11));
	vec3 murk = mix(water_color, water_color * 2.2 + vec3(l * 0.35), l);
	c = mix(c, murk, amount * 0.85);
	vec2 d = uv - 0.5;
	float vig = smoothstep(0.25, 0.75, length(d * vec2(1.3, 1.0)));
	c *= 1.0 - amount * vig * 0.8;
	// a few motes of silt drifting upward
	vec2 g = floor((uv + vec2(0.0, t * 0.03)) * vec2(64.0, 36.0));
	float mote = step(0.992, hash(g)) * amount * 0.25;
	c += vec3(mote);
	c *= 1.0 - darkness;
	COLOR = vec4(c, 1.0);
}
