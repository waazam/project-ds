// Act 12's lake. The surface really moves: four long-crested swells (near-still at sunrise,
// white-capped chop after the breach), a dome where the creature rises under the water, and one
// big ring wave it sends out. Every term mirrors LakeShape.WaveHeight in C#, which the rowboat
// rides, and the wave tables themselves arrive as uniforms from there, so the two never drift.
//
// Vertex colour: r = how much of the waves this vertex carries (0 at the shoreline, 1 in open
// water); g = depth (0 in the shallows, 1 out deep). The mesh is Lake-local and never rotated,
// so VERTEX.xz is the same Lake-local space the C# side uses.
//
// Colour is mostly reflection: the sky's sunrise gradient (horizon peach to zenith blue) chosen by
// the reflected ray's elevation, weighted by Fresnel, over a dark peaty body; the low sun's own
// highlight comes from the real light through a near-mirror roughness, which draws the glitter path.
shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;

uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform vec4 wave_dir_len[4];   // xy direction, z wavelength, w phase
uniform vec4 wave_amps[4];      // x calm amplitude, y storm amplitude
uniform float wave_time = 0.0;
uniform float wave_intensity : hint_range(0.0, 1.0) = 0.0;
uniform vec3 bulge = vec3(0.0);   // xy = Lake-local x,z; z = height
uniform vec4 ring = vec4(0.0);    // xy = origin; z = radius; w = height
uniform vec4 shadow = vec4(0.0);     // xy centre, z radius, w darkness 0..1
uniform vec4 deep_eye = vec4(0.0);   // xy centre, z radius, w open 0..1
uniform vec3 deep_eye_pupil = vec3(0.5, 0.0, 0.0);   // x dilation, yz look offset (in the eye's own axes)
uniform vec2 deep_eye_axis = vec2(1.0, 0.0);   // the almond's long axis (Lake-local xz, unit)
/// The rowboat's footprint (xy centre, z yaw, w on): no water is drawn inside it.
uniform vec4 hull = vec4(0.0);
uniform vec2 hull_size = vec2(1.95, 0.7);   // half length, half beam at the gunwale
uniform float bulge_radius = 5.5;
uniform float ring_width = 3.2;

uniform vec3 deep_color : source_color = vec3(0.03, 0.045, 0.055);
uniform vec3 shallow_color : source_color = vec3(0.1, 0.09, 0.06);
uniform vec3 sky_horizon : source_color = vec3(0.9, 0.58, 0.42);
uniform vec3 sky_zenith : source_color = vec3(0.26, 0.3, 0.46);
uniform vec3 foam_color : source_color = vec3(0.8, 0.78, 0.74);
uniform float reflect_strength = 0.8;
/// Lifts the reflection toward the storm's grey once the chop is up.
uniform vec3 storm_sky : source_color = vec3(0.42, 0.36, 0.38);

varying float v_fade;
varying float v_crest;
varying float v_ring;
varying float v_bulge;
varying vec2 v_local;

void vertex() {
	vec2 p = VERTEX.xz;
	float fade = COLOR.r;
	float h = 0.0;
	vec2 slope = vec2(0.0);
	float amp_sum = 0.0;
	for (int i = 0; i < 4; i++) {
		vec2 d = wave_dir_len[i].xy;
		float k = 6.2831853 / wave_dir_len[i].z;
		float om = sqrt(9.8 * k);
		float a = mix(wave_amps[i].x, wave_amps[i].y, wave_intensity) * fade;
		float arg = k * dot(d, p) - om * wave_time + wave_dir_len[i].w;
		h += a * sin(arg);
		slope += d * (a * k * cos(arg));
		amp_sum += a;
	}
	vec2 bd = p - bulge.xy;
	float g = bulge.z * exp(-dot(bd, bd) / (bulge_radius * bulge_radius));
	h += g;
	slope += bd * (-2.0 / (bulge_radius * bulge_radius)) * g;
	vec2 rd = p - ring.xy;
	float r = length(rd);
	float u = (r - ring.z) / ring_width;
	float rg = ring.w * exp(-u * u) * fade;
	h += rg;
	if (r > 0.01) slope += rd / r * (-2.0 * u / ring_width) * rg;

	VERTEX.y += h;
	NORMAL = normalize(vec3(-slope.x, 1.0, -slope.y));
	v_fade = fade;
	v_crest = h / max(amp_sum, 0.03);
	v_ring = rg;
	v_bulge = g;
	v_local = p;
}

void fragment() {
	if (hull.w > 0.5) {
		// the same plan shape as Rowboat.GunwaleHalf (pointed bow at local -Z), a hair inside the planks
		vec2 d = v_local - hull.xy;
		float s = sin(hull.z), c = cos(hull.z);
		float bz = d.x * s + d.y * c;    // along the boat's +Z (toward the stern)
		float bx = d.x * c - d.y * s;    // along its +X
		float t = bz / hull_size.x;
		float half_w = hull_size.y * (t < 0.0 ? sqrt(max(0.0, 1.0 - t * t * 0.98)) : 1.0 - 0.32 * t * t) * 0.93;
		if (abs(t) < 0.97 && abs(bx) < half_w) discard;
	}
	vec2 uv = v_local * 0.25;
	float t = wave_time * 0.045;
	float n1 = texture(noise_tex, uv * 0.5 + vec2(t, t * 0.6)).r;
	float n2 = texture(noise_tex, uv * 1.3 + vec2(-t * 0.7, t * 0.9)).r;
	float n3 = texture(noise_tex, uv * 3.1 + vec2(t * 1.4, -t * 1.1)).r;
	float n = n1 * 0.5 + n2 * 0.35 + n3 * 0.15;

	// fine ripples on top of the real waves (stronger in the chop)
	float amp = 0.5 + wave_intensity * 0.9;
	float dx = texture(noise_tex, uv * 1.3 + vec2(-t * 0.7 + 0.02, t * 0.9)).r - n2;
	float dy = texture(noise_tex, uv * 1.3 + vec2(-t * 0.7, t * 0.9 + 0.02)).r - n2;
	vec3 N = normalize(NORMAL + (VIEW_MATRIX * vec4(dx * amp, 0.0, dy * amp, 0.0)).xyz);
	NORMAL = N;

	vec3 Rw = (INV_VIEW_MATRIX * vec4(reflect(-VIEW, N), 0.0)).xyz;
	float up = clamp(Rw.y, 0.0, 1.0);
	vec3 sky = mix(sky_horizon, sky_zenith, smoothstep(0.03, 0.5, up));
	sky = mix(sky, storm_sky, wave_intensity * 0.55);
	float fres = 0.03 + 0.97 * pow(1.0 - clamp(dot(N, VIEW), 0.0, 1.0), 5.0);

	vec3 body = mix(shallow_color, deep_color, COLOR.g) * (0.85 + 0.3 * n);

	float cap_noise = n2 * 0.6 + n3 * 0.4;
	float cap = smoothstep(0.4, 0.85, v_crest + (cap_noise - 0.5) * 0.9) * smoothstep(0.1, 0.7, wave_intensity);
	float ring_foam = smoothstep(0.12, 0.7, v_ring) * (0.55 + 0.45 * cap_noise);
	float bulge_foam = smoothstep(0.35, 1.1, v_bulge) * (0.45 + 0.55 * n3);
	float shore = (1.0 - smoothstep(0.0, 0.07, v_fade)) * smoothstep(0.5, 0.72, n3 + 0.1 * sin(wave_time * 0.9 + v_local.x * 0.3));
	float foam = clamp(cap * 0.55 + ring_foam + bulge_foam + shore * 0.55, 0.0, 1.0);

	// Something vast under the water: a shadow sliding beneath the surface, and in it an eye.
	// Everything here sits "down there": murked toward the water's own colour, and the surface's
	// Fresnel reflection lies over it, so it shows looking down into the lake and hides at a glance.
	float reflect_hide = 1.0;
	if (shadow.w > 0.001) {
		float sd = length(v_local - shadow.xy) / max(shadow.z, 0.01);
		float sh = shadow.w * (1.0 - smoothstep(0.35, 1.0, sd + (n1 - 0.5) * 0.35));
		body *= 1.0 - 0.85 * sh;
		reflect_hide = 1.0 - 0.35 * sh;
	}
	vec3 eye_glow = vec3(0.0);
	if (deep_eye.w > 0.001) {
		vec2 dq = (v_local - deep_eye.xy) / max(deep_eye.z, 0.01);
		vec2 q = vec2(dot(dq, deep_eye_axis), dot(dq, vec2(-deep_eye_axis.y, deep_eye_axis.x)));
		// an almond: the lids close vertically (along z) toward the middle line
		float lid = pow(max(1.0 - q.x * q.x, 0.0), 0.75) * 0.52 * deep_eye.w;
		float inside = smoothstep(0.02, -0.02, abs(q.y) - lid) * step(abs(q.x), 1.0);
		vec2 iq = q - deep_eye_pupil.yz * 0.22;
		float ir = length(iq);
		// a sick yellow-white laced with red veins, darkening toward the lids
		float veins = smoothstep(0.62, 0.72, n3 + n2 * 0.3) * smoothstep(0.35, 0.9, length(q));
		vec3 sclera = mix(vec3(0.62, 0.56, 0.4), vec3(0.5, 0.06, 0.04), veins * 0.8);
		sclera *= 1.0 - 0.55 * smoothstep(0.55, 1.0, abs(q.y) / max(lid, 0.001));
		vec3 iris = mix(vec3(0.62, 0.14, 0.04), vec3(0.24, 0.02, 0.02), smoothstep(0.1, 0.42, ir)) * (0.8 + 0.4 * n2);
		float iris_m = smoothstep(0.43, 0.39, ir);
		// a goat's slit, widening as it dilates
		float slit = mix(0.03, 0.26, deep_eye_pupil.x);
		vec2 pq = vec2(iq.x / slit, iq.y / 0.37);
		float pupil_m = smoothstep(1.05, 0.95, length(pq));
		vec3 eye = mix(sclera, iris, iris_m);
		eye = mix(eye, vec3(0.0), pupil_m);
		// seen through a couple of metres of peaty water
		eye = mix(body, eye, 0.78);
		body = mix(body, eye, inside);
		// it gives off a faint light of its own, so it reads through the water's glare
		eye_glow = (vec3(0.8, 0.09, 0.02) * iris_m * (1.0 - pupil_m) * 0.7 + sclera * (1.0 - iris_m) * 0.22) * inside;
		reflect_hide = min(reflect_hide, 1.0 - 0.8 * inside);
	}

	ALBEDO = mix(body, foam_color, foam);
	EMISSION = sky * fres * reflect_strength * reflect_hide * (1.0 - foam * 0.8) + eye_glow;
	ROUGHNESS = mix(mix(0.05, 0.22, wave_intensity), 0.8, foam);
	SPECULAR = 0.5;
	METALLIC = 0.0;
}
