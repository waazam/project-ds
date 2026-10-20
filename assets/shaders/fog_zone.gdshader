// Fog zones (2026-10-19, the owner: dense, oppressive fog, Silent Hill 2's): a local volume of the volumetric fog (Godot's
// froxels, ray-marched against the depth buffer), placed where a scene needs it and driven by FogZone.cs. Its body is
// two octaves of world-space 3D noise drifting on a slow wind (banks and wisps that hold still in the world), shaped by:
// - a layer: densest at the floor, thinning to nothing by layer_height above it (the sewer's, the long hall's);
// - a veil: nothing under reveal_y, thickening over veil_ramp above it (the last staircase's: everything a few metres
//   above the eye is swallowed, and the line rises as they climb);
// - glow lines: light of its own along lines of the box's local X near the floor (the hall's floor strips, which are
//   lit meshes, not lights: the fog over them takes their colour);
// and faded at the box's sides. Lit by the scene's lights like any volumetric fog; its own glow is never a glare.
shader_type fog;

uniform sampler3D noise_tex : repeat_enable, filter_linear;
uniform float density = 0.06;
uniform float strength = 1.0;
uniform vec3 albedo : source_color = vec3(0.7, 0.7, 0.72);
uniform vec3 emission : source_color = vec3(0.0);
uniform float emission_energy = 0.0;
uniform float noise_scale = 0.045;     // noise cycles a metre
uniform float noise_amount = 0.6;      // 0 even .. 1 all banks and gaps
uniform vec3 wind = vec3(0.35, 0.0, 0.18);
uniform float floor_y = 0.0;           // world
uniform float layer_height = 0.0;      // 0: no layer shaping
uniform float reveal_y = -100000.0;    // world
uniform float veil_ramp = 0.0;         // 0: no veil
uniform float edge = 0.15;             // fraction of the box faded at its sides
uniform vec3 glow_color : source_color = vec3(0.0);
uniform float glow_energy = 0.0;
uniform vec3 glow_x = vec3(0.0);       // three lines along local Z, at these local X
uniform float glow_width = 0.6;
uniform float glow_height = 1.0;

void fog() {
	vec3 w = WORLD_POSITION;
	vec3 drift = wind * TIME;
	float n1 = texture(noise_tex, (w - drift) * noise_scale).r;
	float n2 = texture(noise_tex, (w - drift * 0.55) * noise_scale * 2.9 + vec3(0.37, 0.11, 0.71)).r;
	float n = n1 * 0.7 + n2 * 0.3;
	float body = mix(1.0, smoothstep(0.22, 0.8, n) * 1.6, noise_amount);

	float shape = 1.0;
	if (layer_height > 0.0) {
		float h = clamp(1.0 - (w.y - floor_y) / layer_height, 0.0, 1.0);
		shape *= h * h * (3.0 - 2.0 * h);
	}
	if (veil_ramp > 0.0) {
		shape *= smoothstep(reveal_y, reveal_y + veil_ramp, w.y);
	}
	vec3 u = UVW;
	float side = smoothstep(0.0, edge, min(min(u.x, 1.0 - u.x), min(u.z, 1.0 - u.z)));
	side *= smoothstep(0.0, edge * 0.5, min(u.y, 1.0 - u.y));

	float k = strength * body * shape * side;
	DENSITY = density * k;
	ALBEDO = albedo;

	vec3 e = emission * emission_energy;
	if (glow_energy > 0.0) {
		float lx = (u.x - 0.5) * SIZE.x;
		float g = max(exp(-abs(lx - glow_x.x) / glow_width), max(exp(-abs(lx - glow_x.y) / glow_width), exp(-abs(lx - glow_x.z) / glow_width)));
		float gh = exp(-max(w.y - floor_y, 0.0) / glow_height);
		e += glow_color * glow_energy * g * gh;
	}
	EMISSION = e * k;
}
