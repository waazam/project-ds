// Act 7's last print on the board (2026-10-16, STORY.md Rev 5): looked at, it animates. Its edges curl off the board
// (curl), the stairs in it begin to burn (stairs: a glow coming up through the picture's middle, from under the steps),
// and then the print itself burns from its edges in (burn), a blackened band ahead of a thin line of embers. Slow and
// soft: nothing flickers fast. The mesh must be subdivided (the curl bends it).
shader_type spatial;
render_mode cull_disabled;

uniform sampler2D tex : source_color, filter_linear_mipmap;
uniform float curl = 0.0;
uniform float stairs = 0.0;
uniform float burn = 0.0;
uniform float glow = 0.32;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p)
{
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

void vertex()
{
	vec2 e = abs(UV - 0.5) * 2.0;
	float edge = max(e.x, e.y);
	float k = max(edge - 0.3, 0.0) / 0.7;
	// lifting off the board toward the room, the corners most, the edges drawing in as they roll
	VERTEX.z += curl * k * k * 0.04 * (0.8 + 0.4 * hash(floor(UV * 3.0)));
	VERTEX.xy *= 1.0 - curl * 0.05 * k * k;
}

void fragment()
{
	vec4 c = texture(tex, UV);
	vec2 e = abs(UV - 0.5) * 2.0;
	float edge = max(e.x, e.y);
	float n = noise(UV * 8.0 + vec2(0.0, TIME * 0.04)) * 0.3 + noise(UV * 21.0) * 0.12;
	// how far in from the edge, ragged
	float d = (1.0 - edge) + n;
	float front = burn * 1.45;
	if (burn > 0.0 && d < front - 0.05) discard;
	float charred = burn > 0.0 ? smoothstep(front + 0.16, front, d) : 0.0;
	float line = burn > 0.0 ? smoothstep(front + 0.035, front, d) : 0.0;
	vec3 col = mix(c.rgb, vec3(0.025, 0.02, 0.018), charred);
	// the stairs in the picture burning: an orange glow up through the middle, under the steps, in slow drifts
	vec2 q = (UV - vec2(0.5, 0.48)) * vec2(1.0, 1.25);
	float pic = smoothstep(0.5, 0.05, length(q)) * (0.55 + 0.45 * noise(UV * 12.0 + vec2(0.0, -TIME * 0.6)));
	col = mix(col, col * vec3(0.5, 0.25, 0.12), stairs * pic * 0.6);
	ALBEDO = col;
	ROUGHNESS = 0.6;
	float slow = 0.8 + 0.2 * sin(TIME * 2.3 + UV.x * 7.0);
	EMISSION = c.rgb * glow * (1.0 - charred) + vec3(1.0, 0.42, 0.1) * (stairs * pic * 1.6 + line * 3.0) * slow;
}
