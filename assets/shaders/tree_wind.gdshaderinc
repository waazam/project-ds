// The breeze through the woods (the owner: subtle, the trees swaying a little in the wind's direction).
// A whole tree leans with the wind by the square of its height (the base never moves, the crown the
// most), breathing with slow gusts that roll across the forest, plus a little sway of its own.
// Everything that makes up a tree (trunk, stubs, boughs, leaves) calls this with its mesh-local vertex,
// so they move together. Grass and ferns are low, so it barely touches them.

//
// The weather pass (2026-10-03): the wind is the shared one now (the global `wind`, m/s, and its gusts in `wind_gust`,
// the Weather node's), so the trees lean the way the snow and the rain are blowing, and further in a gale. At the
// default (0.8, 0, 0.6), 1 m/s, they sway exactly as they did; the lean is capped so a blizzard bends them, never
// lays them flat.
global uniform vec3 wind;
global uniform float wind_gust;
const float WIND_STRENGTH = 0.00045;   // metres of lean per metre of height squared (~0.35 m at 28 m), per m/s of wind

vec3 tree_wind(vec3 v, mat4 model, float time) {
	vec3 base = model[3].xyz;
	float h = max(v.y, 0.0);
	float speed = length(wind.xz);
	vec2 dir = speed > 0.001 ? wind.xz / speed : vec2(0.8, 0.6);
	float strength = min(speed, 3.5);
	float gust = 0.55 + 0.45 * sin(time * 0.31 + base.x * 0.012 + base.z * 0.009) * sin(time * 0.17 + base.z * 0.015) + 0.45 * wind_gust;
	float own = 0.22 * sin(time * (0.9 + 0.15 * strength) + base.x * 0.37 + base.z * 0.23);
	float lean = WIND_STRENGTH * strength * h * h * (gust + own);
	// the offset is in world space; carry it back into the mesh's space (MultiMesh instances are scaled/turned)
	vec3 world = vec3(dir.x, 0.0, dir.y) * lean;
	return v + (inverse(mat3(model)) * world);
}
