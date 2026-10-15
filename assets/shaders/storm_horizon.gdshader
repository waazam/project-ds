// (2026-10-14, the forest pass) The storm's far light: the owner's survey found the Act 3-5 storm frames almost wholly
// black, rain streaks and a few specks on nothing. Low over the woods, one way, the cloud is thinner and a cold, dim
// light comes through it (a town's glow far off, a moon behind the rack), and now and then lightning far away lights the
// cloud from inside without a sound. Drawn on a ring round the camera inside the fog's reach and added over what lies
// behind it, so the trunks between stand black against it and the frame has a shape; never bright enough to break the
// dark. RainVfx drives it.
shader_type spatial;
render_mode unshaded, fog_disabled, blend_add, depth_draw_never, cull_disabled, shadows_disabled;

uniform sampler2D noise_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform vec3 glow_color : source_color = vec3(0.32, 0.4, 0.55);
/// 0..1: the storm (the rain's intensity, out of doors)
uniform float strength = 0.0;
/// the far lightning lighting the cloud (0..~1.5)
uniform float flicker = 0.0;
/// where the light is (world xz direction from the eye), and how wide it spreads round the horizon (cosine falloff power)
uniform vec2 toward = vec2(0.0, -1.0);
uniform float spread = 2.2;
uniform float gain = 0.14;

varying vec3 v_world;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 d = normalize(v_world - CAMERA_POSITION_WORLD);
	float el = asin(clamp(d.y, -1.0, 1.0));
	vec2 h = normalize(d.xz + vec2(1e-5));
	float az = atan(h.y, h.x);
	// round the horizon: strongest toward the light, a little all round
	float face = pow(max(dot(h, normalize(toward)), 0.0), spread) + 0.12;
	// low in the sky: from just under the horizon, at its strongest a few degrees up, gone by twenty-odd
	float band = smoothstep(-0.05, 0.05, el) * (1.0 - smoothstep(0.08, 0.42, el));
	// the cloud's underside: broken, slowly moving
	vec2 cp = vec2(az * 3.0 + TIME * 0.004, el * 9.0);
	float cl = texture(noise_tex, cp * 0.35).r * 0.65 + texture(noise_tex, cp * 1.1 + vec2(0.3, 0.7)).r * 0.35;
	float cloud = mix(0.45, 1.25, smoothstep(0.3, 0.75, cl));
	// the far lightning: inside the cloud toward the light, flaring its brighter parts most
	float fl = flicker * (0.5 + cl) * pow(max(dot(h, normalize(toward)), 0.0), 1.2);
	float light = band * (face * cloud * strength + fl * 1.6 * strength);
	ALBEDO = glow_color * light * gain;
}
