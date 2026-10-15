// (the gushy-water pass, 2026-10-15) Wet, running blood on a surface: a pool spreading (shape 0: UV 0..1 across a
// disc, the middle at 0.5, `spread` its radius 0..1) or a run (shape 1: UV.x across 0..1, UV.y along in metres, the
// way it runs). Thick and dark, not a flat red: dark where it's deep, a little lighter and browner where it thins to
// its edge, with a raised rim (the meniscus: a thin bright highlight line and a darker band just inside it), a slow
// heavy creep along the run or out from the middle, rings rolling out from `source` when something pours in, and
// glossy wet highlights that move with it. Clots: darker blobs that drift slower than the rest.
shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque, specular_schlick_ggx;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec3 color : source_color = vec3(0.24, 0.012, 0.012);
uniform vec3 deep : source_color = vec3(0.07, 0.0, 0.004);
uniform float shape = 0.0;          // 0 a pool (disc), 1 a run (strip)
uniform float spread : hint_range(0.0, 1.0) = 1.0;
uniform float speed = 0.12;         // metres a second it creeps
uniform float ripple = 0.0;         // 0..1: rings out from `source` (a pour landing in it)
uniform vec2 source = vec2(0.5, 0.5);
uniform float scale = 1.0;          // the pattern's size: pools in metres across
uniform float opacity : hint_range(0.0, 1.0) = 1.0;

void vertex() {
	if (ripple > 0.0) {
		float r = length(UV - source);
		VERTEX.y += ripple * 0.006 * sin(r * 40.0 - TIME * 4.0) * exp(-r * 4.0);
	}
}

void fragment() {
	vec2 uv = UV;
	float edge;            // 1 inside .. 0 at the edge
	vec2 p;                // the pattern's coordinates (creeping)
	if (shape < 0.5) {
		vec2 d = uv - 0.5;
		float r = length(d) * 2.0;
		float a = atan(d.y, d.x);
		// a lobed, uneven rim (tongues of it reaching out), grown with `spread`
		// (two sizes of lobe: one alone left a near-perfect ellipse)
		vec2 ca = vec2(cos(a), sin(a));
		float lobe = texture(noise_tex, ca * 0.35 + 0.31).r * 0.6 + texture(noise_tex, ca * 1.1 + vec2(0.57, 0.12)).r * 0.4;
		float rim = spread * (0.5 + 0.75 * lobe);
		edge = clamp((rim - r) / max(0.02, rim) * 4.0, 0.0, 1.0);
		p = d * scale - normalize(d + 1e-4) * TIME * speed * 0.5;
	} else {
		float x = abs(uv.x * 2.0 - 1.0);
		float wob = texture(noise_tex, vec2(uv.y * 0.35, 0.61)).r;
		edge = clamp((0.85 + 0.25 * wob - x) * 3.0, 0.0, 1.0);
		p = vec2(uv.x * 0.6, uv.y - TIME * speed) * scale;
	}
	float n1 = texture(noise_tex, p * 0.9).r;
	float n2 = texture(noise_tex, p * 2.1 + vec2(0.4, 0.2)).r;
	float clot = smoothstep(0.62, 0.8, texture(noise_tex, p * 1.3 + TIME * speed * 0.3 + 0.7).r);
	// depth: thin at the rim, pooling thick in the middle and in the clots
	float thick = smoothstep(0.0, 0.6, edge) * (0.7 + 0.3 * n1) + clot * 0.3;
	vec3 c = mix(color + vec3(0.012, 0.004, 0.0), deep, clamp(thick, 0.0, 1.0));
	c = mix(c, deep * 0.6, clot * 0.6);
	// the meniscus: a darker band just inside the rim
	float band = smoothstep(0.0, 0.12, edge) * (1.0 - smoothstep(0.12, 0.3, edge));
	c *= 1.0 - band * 0.45;
	// rings rolling out from where it's pouring in
	float rr = length(uv - source);
	float ring = ripple * (0.5 + 0.5 * sin(rr * 40.0 - TIME * 4.0)) * exp(-rr * 4.0);
	ALBEDO = c;
	// (glossy, but not mirror-sharp: at 640x360 a sharp highlight on the fine relief sparkled pixel by pixel)
	ROUGHNESS = mix(0.13, 0.24, clot) + (1.0 - edge) * 0.1;
	SPECULAR = 0.55;   // (0.9 read as chrome under the lantern)
	// glossy relief: slow swells and the raised rim, all moving with it
	float dx = texture(noise_tex, p * 0.9 + vec2(0.03, 0.0)).r - n1;
	float dy = texture(noise_tex, p * 0.9 + vec2(0.0, 0.03)).r - n1;
	float rimN = band * 2.0;
	NORMAL = normalize(NORMAL + (VIEW_MATRIX * vec4((dx * 1.3 + ring * 0.6) * (1.0 + rimN), 0.0, (dy * 1.3 + ring * 0.6) * (1.0 + rimN), 0.0)).xyz);
	ALPHA = clamp(smoothstep(0.0, 0.08, edge) * opacity, 0.0, 1.0);
}
