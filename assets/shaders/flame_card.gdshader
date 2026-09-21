shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, skip_vertex_transform;

// Billboarded flame tongue for FireVfx (one MultiMesh instance per card). The quad
// turns about world Y to face the camera, keeps its instance scale, and draws a
// tongue of fire from scrolling value noise: white-yellow core, orange body, deep
// red ragged edges that break off into licks near the top.
// INSTANCE_CUSTOM.x = seed (0..1), .y = speed scale, .z = sway.
uniform float intensity : hint_range(0.0, 1.0) = 1.0;
uniform vec3 core_color : source_color = vec3(1.0, 0.86, 0.5);
uniform vec3 body_color : source_color = vec3(1.0, 0.42, 0.07);
uniform vec3 edge_color : source_color = vec3(0.55, 0.08, 0.02);
uniform float brightness = 2.2;

varying float v_seed;
varying float v_speed;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vnoise(vec2 p) {
	vec2 i = floor(p), f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
float fbm(vec2 p) {
	float s = 0.0, a = 0.5;
	for (int i = 0; i < 4; i++) { s += a * vnoise(p); p = p * 2.03 + vec2(1.7, 9.2); a *= 0.5; }
	return s;
}

void vertex() {
	v_seed = INSTANCE_CUSTOM.x;
	v_speed = 0.7 + INSTANCE_CUSTOM.y;
	vec3 origin = MODEL_MATRIX[3].xyz;
	float sx = length(MODEL_MATRIX[0].xyz);
	float sy = length(MODEL_MATRIX[1].xyz) * mix(0.35, 1.0, intensity);
	vec3 to_cam = CAMERA_POSITION_WORLD - origin;
	vec3 right = normalize(vec3(to_cam.z, 0.0, -to_cam.x) + vec3(1e-4, 0.0, 0.0));
	float sway = sin(TIME * (1.3 + v_seed) + v_seed * 20.0) * INSTANCE_CUSTOM.z * VERTEX.y;
	vec3 world = origin + right * (VERTEX.x * sx + sway) + vec3(0.0, 1.0, 0.0) * VERTEX.y * sy;
	VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
	NORMAL = (VIEW_MATRIX * vec4(normalize(to_cam), 0.0)).xyz;
}

void fragment() {
	float v = 1.0 - UV.y;                  // 0 at the base, 1 at the tip
	float x = UV.x - 0.5;
	float t = TIME * v_speed;
	float n = fbm(vec2(UV.x * 2.6 + v_seed * 13.0, v * 2.2 - t * 1.9));
	float n2 = fbm(vec2(UV.x * 5.0 - v_seed * 7.0, v * 4.0 - t * 3.1));
	// narrowing tongue, pushed around by the noise
	float width = mix(0.46, 0.06, pow(v, 0.8));
	float d = abs(x + (n - 0.5) * 0.35 * v) / max(width, 0.001);
	float body = 1.0 - smoothstep(0.55, 1.0, d + (n2 - 0.5) * 0.5);
	// the flame thins out and breaks up with height
	float f = body * clamp(1.15 - v * (1.25 - n * 0.6), 0.0, 1.0);
	f *= smoothstep(0.0, 0.08, v);         // soft base
	f = clamp(f * (0.9 + n2 * 0.5), 0.0, 1.0);
	vec3 col = mix(edge_color, body_color, smoothstep(0.08, 0.45, f));
	col = mix(col, core_color, smoothstep(0.62, 0.95, f) * (1.0 - v * 0.6));
	float flick = 0.85 + 0.15 * sin(TIME * 13.0 + v_seed * 40.0);
	ALBEDO = col * brightness * flick;
	ALPHA = f * intensity;
}
