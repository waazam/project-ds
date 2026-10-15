// Act 22's ground: the winter woods' snow, the snowplow's road and the frozen crust near the lodge
// (Poly Haven's snow textures, made small and cold by tools/Textures/make_snow.py), tiled in world space.
// Vertex colour: R = the plowed road, A = frozen (the glazed, icy crust near the lodge); G = a little
// shade under the trees; B = 1 on the road's ribbon, whose UV is then (across, along) in metres.
// A faint sheen only where it has frozen (the ice), never a glare.
// The plowed walls' sheer faces (2026-09-30): the snow laid on them from the side (not stretched down them from
// above), cut in the plow's layers and the blower's grooves, a touch bluer, like the owner's references.
// (2026-10-14, the forest pass: the owner's survey, "a smooth, low-poly surface with faint cracks that reads as clay")
// The wind's work on it now, in the shading (the 4 m grid can't carry it): broad drifts lying across the wind,
// sastrugi (sharp, wind-cut ridges running with it, here and there) and soft lumps, their lee and their hollows a cold blue-grey,
// the crests a touch brighter; the photo's dark crack lines softened; grain and a broad mottle that carry to the fog;
// a crystal glinting here and there as the eye moves. On the road: the plow's packed tyre ruts, and two lines of old boot
// prints half filled in by the snow.
shader_type spatial;
render_mode diffuse_lambert, specular_schlick_ggx;

uniform sampler2D tex_snow : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_plowed : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D tex_packed : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D nor_snow : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D nor_plowed : hint_normal, filter_linear_mipmap, repeat_enable;
uniform sampler2D detail_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
/// smooth seamless noise (the wind's relief, the mottle); without it, the ground is as it was
uniform sampler2D relief_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform float relief = 1.0;
/// the way the wind that shaped it blows (xz)
uniform vec2 wind_dir = vec2(0.9, 0.42);
uniform vec3 hollow_tint : source_color = vec3(0.74, 0.8, 0.94);
uniform float glisten = 0.4;
uniform float tile = 0.28;
uniform float road_tile = 0.36;
uniform float detail_tile = 1.6;

varying vec3 wpos;
varying vec3 wnorm;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnorm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

float hash3(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }
float hash2(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

// the wind's relief (metres): drifts across it, sastrugi along it, ripples; and (out) how deep in a hollow
float wind_relief(vec2 p, out float drift) {
	vec2 wd = normalize(wind_dir);
	vec2 a = vec2(dot(p, wd), dot(p, vec2(-wd.y, wd.x)));
	float dr = texture(relief_tex, vec2(a.x * 0.03, a.y * 0.012)).r * 0.65 + texture(relief_tex, p * 0.05 + 0.4).r * 0.35;
	drift = dr - 0.5;
	// (sparse: a ridge here and there where the wind scoured, not a field of ripples, which read as sand)
	float s1 = texture(relief_tex, vec2(a.x * 0.045, a.y * 0.22) + vec2(0.37, 0.71)).r;
	float patchy = smoothstep(0.45, 0.7, texture(relief_tex, p * 0.025 + vec2(0.8, 0.1)).r);
	float sast = pow(1.0 - abs(s1 * 2.0 - 1.0), 6.0) * 0.06 * patchy;
	float lumps = (texture(relief_tex, p * 0.35 + vec2(0.6, 0.2)).r - 0.5) * 0.05;
	return drift * 0.9 + sast + lumps;
}

void fragment() {
	vec2 uv = wpos.xz * tile;
	// two scales of the snow, so its tiling never lines up; its dark crack lines (clay, the owner's survey) softened
	vec3 snow = texture(tex_snow, uv).rgb * 0.6 + texture(tex_snow, uv * 0.37 + vec2(0.31, 0.17)).rgb * 0.4;
	vec3 snowAvg = textureLod(tex_snow, vec2(0.5), 10.0).rgb;
	snow = mix(snow, snowAvg, 0.5 * relief);
	vec3 road = texture(tex_plowed, wpos.xz * road_tile).rgb;
	road = mix(road, textureLod(tex_plowed, vec2(0.5), 10.0).rgb, 0.35 * relief);   // (its cracks softened too)
	vec3 packed = texture(tex_packed, uv * 1.3).rgb;
	float r = COLOR.r, ice = COLOR.a;
	vec3 c = mix(snow, road, r);
	c = mix(c, mix(packed, road * 0.85 + packed * 0.15, r), ice * 0.85);
	float flat_up = smoothstep(0.72, 0.95, wnorm.y);

	// the wind's relief: off the road, on the open snow (less where it has glazed)
	float wild = (1.0 - r) * (1.0 - ice * 0.5) * flat_up * relief;
	vec3 wn = wnorm;
	if (wild > 0.001) {
		float d0, dd;
		const float e = 0.08;
		float h0 = wind_relief(wpos.xz, d0);
		float hx = wind_relief(wpos.xz + vec2(e, 0.0), dd);
		float hz = wind_relief(wpos.xz + vec2(0.0, e), dd);
		vec3 rn = normalize(vec3(-(hx - h0) / e, 1.0, -(hz - h0) / e));
		wn = normalize(mix(wnorm, normalize(wnorm + vec3(rn.x, 0.0, rn.z) * 0.8), wild));
		// the lee faces and the hollows cold blue-grey, the crests a touch brighter
		float lee = clamp(dot(rn.xz, normalize(wind_dir)) * 6.0, 0.0, 1.0);
		float hollow = smoothstep(0.02, -0.18, d0);
		float crest = smoothstep(0.06, 0.2, d0) + smoothstep(0.03, 0.08, h0 - d0 * 0.9) * 0.6;
		c = mix(c, c * hollow_tint, clamp(lee * 0.55 + hollow * 0.6, 0.0, 1.0) * wild);
		c *= 1.0 + crest * 0.07 * wild;
	}
	// the road (its ribbon): the plow's packed tyre ruts, and old boot prints half filled in
	if (COLOR.b > 0.5 && r > 0.3) {
		float across = UV.x, along = UV.y;
		float rut = 1.0 - smoothstep(0.1, 0.26, abs(abs(across) - 0.92));
		float shoulder = smoothstep(0.2, 0.3, abs(abs(across) - 0.92)) * (1.0 - smoothstep(0.3, 0.5, abs(abs(across) - 0.92)));
		float tread = smoothstep(0.35, 0.65, fract(along * 7.0 + (across > 0.0 ? 0.3 : 0.0))) * 0.5 + 0.5;
		float rn2 = texture(relief_tex, vec2(along * 0.06, across * 0.3)).r;
		c *= 1.0 - rut * (0.12 + 0.05 * tread) * r * mix(0.6, 1.2, rn2);
		c = mix(c, c * vec3(0.95, 0.94, 0.92), rut * r * 0.5);
		c *= 1.0 + shoulder * 0.05 * r;
		// boot prints: two lines, an older one (most filled) the other way
		for (int line = 0; line < 2; line++) {
			float fl = float(line);
			float mid = (line == 0 ? -1.75 : 1.55) + 0.18 * sin(along * 0.11 + fl * 2.0) + 0.08 * sin(along * 0.37 + 1.3);
			float stride = line == 0 ? 0.39 : 0.42;
			float k = floor(along / stride);
			float side = mod(k, 2.0) < 1.0 ? -1.0 : 1.0;
			float cy = (k + 0.5) * stride;
			vec2 lp = vec2(across - (mid + side * 0.12), along - cy);
			float rot = 0.12 * side + (hash2(vec2(k, fl)) - 0.5) * 0.2;
			lp = vec2(lp.x * cos(rot) - lp.y * sin(rot), lp.x * sin(rot) + lp.y * cos(rot));
			// a sole and a heel, soft (snow has fallen in them since)
			float sole = length((lp - vec2(0.0, 0.035)) / vec2(0.055, 0.1));
			float heel = length((lp - vec2(0.0, -0.1)) / vec2(0.045, 0.05));
			float dpr = min(sole, heel);
			float keep = step(0.12, hash2(vec2(k * 1.7, fl + 3.0))) * (line == 0 ? 0.75 : 0.42) * mix(0.6, 1.0, hash2(vec2(k, 9.0 + fl)));
			float print = (1.0 - smoothstep(0.55, 1.25, dpr)) * keep;
			float rim = smoothstep(1.0, 1.3, dpr) * (1.0 - smoothstep(1.3, 1.8, dpr)) * keep;
			c = mix(c, c * hollow_tint * 0.92, print * r);
			c *= 1.0 + rim * 0.08 * r;
		}
	}
	// a sheer face: the snow from the side, in layers and grooves
	float steep = 1.0 - smoothstep(0.45, 0.8, abs(wnorm.y));
	if (steep > 0.0) {
		float along = abs(wnorm.x) > abs(wnorm.z) ? wpos.z : wpos.x;
		vec2 suv = vec2(along, wpos.y) * tile;
		vec3 face = texture(tex_snow, suv).rgb * 0.65 + texture(tex_snow, suv * 0.41 + vec2(0.2, 0.6)).rgb * 0.35;
		float n = texture(tex_snow, vec2(along * 0.05, wpos.y * 0.3)).r;
		float layers = 0.95 + 0.05 * sin(wpos.y * 4.0 + n * 4.0);
		float grooves = 0.97 + 0.03 * sin(along * 6.0 + n * 2.0);
		face *= layers * grooves * vec3(0.9, 0.95, 1.04);
		c = mix(c, face, steep);
	}
	float near = clamp(1.0 - length(VERTEX) / 30.0, 0.0, 1.0);
	c *= mix(1.0, texture(detail_tex, wpos.xz * detail_tile).r, near);
	// a broad mottle and a finer grain that carry on out to the fog (the mid distance was a flat sheet)
	float mottle = texture(relief_tex, wpos.xz * 0.11 + vec2(0.21, 0.63)).r;
	float grain = texture(relief_tex, wpos.xz * 1.3 + vec2(0.5, 0.1)).r;
	c *= mix(1.0, (0.93 + 0.12 * mottle) * (0.97 + 0.05 * grain), relief);
	c *= 1.0 - COLOR.g * 0.35;
	ALBEDO = c;
	NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
	// (2026-10-15, the scan: under the lantern's low light the open snow read as a crumpled bedsheet, the photo's sharp
	// creases all over it) The open snow's normal softened: blurred a mip or two and two scales of it averaged, and laid in
	// shallower; the road keeps its own, crisp.
	vec3 n1 = mix(texture(nor_snow, uv, 1.5).rgb, texture(nor_snow, uv * 0.37 + vec2(0.31, 0.17), 1.0).rgb, 0.45);
	vec3 n2 = texture(nor_plowed, wpos.xz * road_tile).rgb;
	NORMAL_MAP = mix(n1, n2, r);
	NORMAL_MAP_DEPTH = mix(0.3, 0.6, r);
	ROUGHNESS = mix(0.92, 0.38, ice);
	SPECULAR = mix(0.15, 0.4, ice);
	// a crystal here and there catching the eye as it moves (fresh_snow's): never on its own, never a glare
	if (glisten > 0.0 && near > 0.4) {
		vec3 cell = floor(wpos * 60.0);
		if (hash3(cell) > 0.994) {
			vec3 facet = normalize(vec3(hash3(cell + 1.3), hash3(cell + 4.7), hash3(cell + 9.1)) * 2.0 - 1.0 + wnorm * 1.2);
			vec3 v = normalize((INV_VIEW_MATRIX * vec4(VIEW, 0.0)).xyz);
			float g = pow(clamp(dot(facet, v), 0.0, 1.0), 90.0);
			EMISSION = vec3(0.75, 0.82, 0.95) * g * glisten * (1.0 - r * 0.7) * near;
		}
	}
}
