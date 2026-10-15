// (2026-10-14h, the old-and-abandoned pass) Act 15's floor strips kept, but old: each segment's diffuser yellowed and
// filthy, its brightness its own (some all but dead, some failing), grime lying over them along the floor. The signal
// colour and level come from the hall (red light, green light): this only dirties it, it never changes it.
// Vertex colour, per segment: r its brightness (0 dead .. 1), g its failing (0 steady; else a phase: it sags and buzzes
// on its own time), b how yellowed. Unshaded-looking where lit, but a lit surface, so the lantern finds the dirt on it.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

uniform vec3 signal_color : source_color = vec3(0.4, 0.95, 0.42);
uniform float level = 1.0;
uniform float energy = 1.2;
uniform float flicker_ok = 1.0;   // 0 with Reduce Flashing: the failing ones only sag, slowly
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;

varying vec3 w_pos;

void vertex() {
	w_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float seg = COLOR.r, ph = COLOR.g, yel = COLOR.b;
	// grime lying over it: blotches along the floor, and the dirt of years in the diffuser
	float g1 = texture(noise_tex, w_pos.xz * vec2(0.9, 0.23)).r;
	float g2 = texture(noise_tex, w_pos.xz * vec2(3.1, 1.3) + 0.4).r;
	float g3 = texture(noise_tex, w_pos.xz * vec2(9.0, 4.0) + 0.8).r;
	float grime = clamp(mix(0.3, 1.15, smoothstep(0.3, 0.68, g1)) * mix(0.55, 1.1, g2) * mix(0.6, 1.08, smoothstep(0.3, 0.6, g3)), 0.18, 1.0);
	float fl = 1.0;
	if (ph > 0.004) {
		float t = TIME * (0.25 + ph * 0.5) + ph * 37.0;
		float sag = smoothstep(0.32, 0.6, texture(noise_tex, vec2(t * 0.11, ph * 3.0)).r);
		float buzz = 0.88 + 0.12 * sin(TIME * (41.0 + ph * 13.0) + ph * 9.0);
		fl = mix(mix(0.25, 1.0, sag), mix(0.08, 1.0, sag) * buzz, flicker_ok);
	}
	// yellowed: the old plastic takes the colour toward a dirty ochre (it still reads as the signal)
	vec3 col = mix(signal_color, signal_color * vec3(1.3, 0.9, 0.45) + vec3(0.07, 0.04, 0.0), yel * 0.7);
	float lv = level * seg * grime * fl;
	ALBEDO = mix(vec3(0.2, 0.19, 0.14), col * 0.6, clamp(lv, 0.0, 1.0)) * mix(0.6, 1.0, grime);
	EMISSION = col * lv * energy;
	ROUGHNESS = 0.55;
	SPECULAR = 0.4;
}
