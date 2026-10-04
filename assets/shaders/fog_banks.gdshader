// Drifting fog banks (2026-10-04, the owner: "bake in some other shaders to make everything stand out more"): a fog
// volume that rides with the camera, its density from two octaves of 3D noise in world space scrolled by a slow wind,
// so the banks and wisps drift past through the volumetric fog instead of moving with the player. They lie low (thinning
// with height above the ground) and fade out at the volume's sides. Lit only by what lights the fog (the sun between the
// trunks, the lantern's beam): no glow of their own, never a glare.
shader_type fog;

uniform sampler3D noise_tex : repeat_enable, filter_linear;
uniform float density = 0.05;
uniform float scale = 0.03;          // noise cycles a metre
uniform vec3 wind = vec3(0.55, 0.0, 0.22);
uniform float ground_y = 0.0;        // the ground under the camera (world)
uniform float height = 7.0;          // how high the banks reach above it
uniform vec3 tint : source_color = vec3(0.72, 0.74, 0.77);

void fog() {
	vec3 w = WORLD_POSITION;
	vec3 drift = wind * TIME;
	float n1 = texture(noise_tex, (w - drift) * scale).r;
	float n2 = texture(noise_tex, (w - drift * 0.6) * scale * 2.7 + vec3(0.31, 0.17, 0.53)).r;
	float banks = smoothstep(0.42, 0.78, n1 * 0.72 + n2 * 0.28);
	float low = clamp(1.0 - (w.y - ground_y) / height, 0.0, 1.0);
	low *= low;
	float side = smoothstep(0.0, 0.22, min(min(UVW.x, 1.0 - UVW.x), min(UVW.z, 1.0 - UVW.z)));
	DENSITY = density * banks * low * side;
	ALBEDO = tint;
}
