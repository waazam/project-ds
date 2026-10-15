// The library's books (2026-10-14h): an old binding's cloth or leather in one pass. The colour is the book's own
// (its instance colour), over a worn grain; years of dust lie grey on what faces up (the tops of the books, a book left
// lying flat), patchy and thick at the back; the spine's head is a little sun-faded and its foot rubbed dark from hands.
shader_type spatial;
render_mode cull_back;

uniform sampler2D grain : source_color, filter_linear_mipmap, repeat_enable;
uniform float roughness = 0.78;
uniform float specular = 0.28;
uniform vec3 dust_color : source_color = vec3(0.4, 0.37, 0.32);
uniform float dust = 0.75;
uniform vec3 base : source_color = vec3(1.0);
uniform bool pages = false;   // the page block: its own colour, no fading as a spine's

varying vec3 wpos;
varying vec3 wnorm;
varying vec3 lpos;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnorm = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
	lpos = VERTEX;
}

void fragment() {
	vec3 c = (pages ? base : COLOR.rgb * base) * texture(grain, UV * 0.6).rgb;
	if (!pages) {
		// the spine's head paled by the light of decades, its foot dark with hands
		float h = lpos.y + 0.5;
		c = mix(c, vec3(dot(c, vec3(0.33))) * 1.15 + vec3(0.03, 0.025, 0.015), smoothstep(0.75, 1.0, h) * 0.35);
		c *= mix(0.82, 1.0, smoothstep(0.0, 0.18, h));
	}
	// dust on what faces up
	float up = smoothstep(0.5, 0.92, normalize(wnorm).y);
	float patches = vnoise(wpos.xz * 9.0 + wpos.y * 3.0) * 0.6 + vnoise(wpos.xz * 37.0) * 0.4;
	float d = up * dust * (0.55 + 0.45 * patches);
	c = mix(c, dust_color, clamp(d, 0.0, 0.85));
	ALBEDO = c;
	ROUGHNESS = mix(roughness, 1.0, d);
	SPECULAR = specular * (1.0 - d);
}
