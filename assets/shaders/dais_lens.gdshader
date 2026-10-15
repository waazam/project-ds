// Act 20 (2026-10-14h): the signal lenses set round the dais's iron ring, two kinds of thick pressed glass in brass
// bezels, ruby and green, each with a lamp under it. Unlit they're dark glass that catches the lantern; lit, the lamp
// shows through from the crown of the dome. The power off: the ruby ones burn and the green are dark. The power back:
// a green chaser runs round the ring (UV.x once round it), its tail longer each lap, the ruby going out behind it,
// until every green one burns. The glass is shaded like any other (the lantern glints on it); only its lamp glows.
shader_type spatial;
render_mode cull_back;

uniform float head = 0.0;        // the chaser's head, in turns
uniform float tail = 0.0;        // how much of the ring behind the head is green (0 none, 1 all)
uniform float intensity = 1.0;
uniform vec3 red : source_color = vec3(1.0, 0.12, 0.05);
uniform vec3 green : source_color = vec3(0.3, 1.0, 0.42);
uniform float glow = 2.4;

varying float green_lens;

void vertex() {
	green_lens = COLOR.g;
}

void fragment() {
	float behind = fract(head - UV.x);
	float g = tail >= 0.999 ? 1.0 : (tail <= 0.0 ? 0.0 : 1.0 - smoothstep(tail - 0.03, tail, behind));
	float spark = (tail > 0.0 && tail < 0.999) ? exp(-behind * 28.0) : 0.0;
	bool is_green = green_lens > 0.5;
	float lit = is_green ? clamp(g + spark, 0.0, 1.5) : 1.0 - g;
	// the glass, dark and thick; the lamp brightest at the dome's crown, where the filament shows through
	ALBEDO = is_green ? vec3(0.03, 0.11, 0.05) : vec3(0.2, 0.02, 0.018);
	ROUGHNESS = 0.14;
	SPECULAR = 0.7;
	float crown = mix(1.35, 0.4, clamp(UV.y, 0.0, 1.0));
	EMISSION = (is_green ? green : red) * lit * intensity * glow * crown;
}
