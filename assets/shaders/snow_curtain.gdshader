// The far snow (the weather pass, 2026-10-03): past the falling flakes, a moving curtain of them drawn on a ring
// round the camera, two layers at two sizes and speeds (the nearer bigger and quicker: parallax), carried by the
// shared wind, sinking into the fog with distance. It is what makes the snow thick without a million particles, and
// in a blizzard it is most of what closes the view in. Lit like everything else (dark at night but where light is).
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, specular_disabled, shadows_disabled;

global uniform vec3 wind;
global uniform float wind_gust;

uniform sampler2D curtain : source_color, filter_linear_mipmap, repeat_enable;
uniform vec3 tint : source_color = vec3(0.74, 0.77, 0.83);
/// 0..1: how thick (the snow's heaviness; a blizzard's whiteout).
uniform float strength = 0.5;
/// The ring's radius (m), and the metres one tile of the texture covers.
uniform float radius = 18.0;
uniform float tile = 7.0;
/// Its fall (m/s): the snow's.
uniform float fall = 1.0;
/// Where the camera is (the script's: the ring rides with it, the snow on it mustn't).
uniform vec3 eye;
uniform float glow = 0.05;
/// The colour its glow takes: the scene's haze (Weather sets it from the fog), so the flakes stand just out of the
/// haze they fall through, at dusk and at night alike (bright only in a light: never a glare).
uniform vec3 glow_tint : source_color = vec3(0.3, 0.32, 0.36);
uniform float haze_lift = 0.0;

varying vec3 v_local;

void vertex() {
	v_local = VERTEX;
	NORMAL = -normalize(vec3(VERTEX.x, 0.0, VERTEX.z));   // inward, toward the eye
}

vec4 layer(float r, float t, vec2 drift, float scale) {
	float ang = atan(v_local.z, v_local.x);
	vec2 tangent = vec2(-sin(ang), cos(ang));
	// round the ring in metres, up it in metres; carried by the wind's sideways part and its fall, and kept still in
	// the world as the camera strafes
	float u = ang * r + dot(eye.xz, tangent) * 0.8;
	float v = v_local.y + eye.y;
	vec2 uv = vec2(u, v) / (tile * scale) + vec2(-dot(drift, tangent), fall * (1.0 + 0.3 * wind_gust)) * t / (tile * scale);
	return texture(curtain, uv);
}

void fragment() {
	float t = TIME;
	vec2 drift = wind.xz * (1.0 + wind_gust * 0.6);
	float a = layer(radius, t, drift, 1.0).a * 0.65 + layer(radius, t * 1.35, drift * 1.2, 0.55).a * 0.5;
	// thinning toward its top and bottom (it's a band round the eye, not a wall)
	float h = v_local.y;
	float band = smoothstep(-6.0, -1.0, h) * (1.0 - smoothstep(10.0, 20.0, h));
	ALBEDO = tint;
	ALPHA = clamp(a * strength * band, 0.0, 1.0);
	EMISSION = tint * glow + glow_tint * haze_lift;
	ROUGHNESS = 1.0;
	NORMAL = normalize(NORMAL);
}

void light() {
	DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * (0.3 + 0.7 * abs(dot(NORMAL, LIGHT))) / 3.14159;
}
