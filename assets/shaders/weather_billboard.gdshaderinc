// The weather's particles drawn (weather_flake.gdshader, weather_spark.gdshader): each a card facing the camera,
// turned to its spin, or (stretch > 0) drawn out along its own motion as it crosses the view (rain, a blizzard's
// flakes, embers). Faded out right at the eye (never a smear across it) and toward the edges of the box they wrap
// in (never a pop). Needs `render_mode skip_vertex_transform`; the particles' COLOR.rgb is their velocity. (The fade
// and the atlas cell come back out: a shader function may not write the varyings itself.)

/// Seconds of travel a streak spans (0: a card turned to its spin).
uniform float stretch = 0.0;
/// Gone within this of the eye; whole by three times it.
uniform float near_fade = 0.4;
/// The box's half-size (the particles' wrap): they fade out over the outer `edge_band` of it.
uniform vec3 box_half = vec3(14.0, 8.0, 14.0);
uniform float edge_band = 0.3;
uniform vec2 atlas_grid = vec2(4.0, 4.0);

varying float v_fade;
varying vec2 v_cell;

void weather_vertex(inout vec3 vertex, inout vec3 normal, mat4 model, mat4 view, mat4 inv_view, vec4 col, vec4 custom, out float fade, out vec2 cell) {
	vec3 centre = model[3].xyz;
	float size = length(model[0].xyz);
	vec3 cam = inv_view[3].xyz;
	vec3 right = inv_view[0].xyz, up = inv_view[1].xyz, fwd = -inv_view[2].xyz;
	vec2 corner = vertex.xy;   // (a unit quad: -0.5..0.5)
	vec3 ax_r, ax_u;
	float len_u = size;
	vec3 vel = col.rgb;
	vec3 sv = vel - fwd * dot(vel, fwd);
	float sl = length(sv);
	if (stretch > 0.0 && sl > 0.05) {
		ax_u = sv / sl;
		ax_r = normalize(cross(ax_u, fwd));
		len_u = size + sl * stretch;
	} else {
		float a = custom.x;
		ax_r = right * cos(a) + up * sin(a);
		ax_u = -right * sin(a) + up * cos(a);
	}
	vec3 world = centre + ax_r * corner.x * size + ax_u * corner.y * len_u;
	vertex = (view * vec4(world, 1.0)).xyz;
	normal = (view * vec4(-fwd, 0.0)).xyz;
	// the fades: at the eye; toward the box's edges (its middle is the camera, near enough)
	vec3 rel = centre - cam;
	float d = length(rel);
	float edge = max(abs(rel.x) / box_half.x, max(abs(rel.y) / box_half.y, abs(rel.z) / box_half.z));
	fade = smoothstep(near_fade, near_fade * 3.0, d) * (1.0 - smoothstep(1.0 - edge_band, 0.97, edge)) * col.a;
	float f = custom.z;
	cell = vec2(mod(f, atlas_grid.x), floor(f / atlas_grid.x));
}

vec2 weather_uv(vec2 uv, vec2 cell) {
	return (uv + cell) / atlas_grid;
}
