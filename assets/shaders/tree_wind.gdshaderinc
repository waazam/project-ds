// The breeze through the woods (the owner: subtle, the trees swaying a little in the wind's direction).
// A whole tree leans with the wind by the square of its height (the base never moves, the crown the
// most), breathing with slow gusts that roll across the forest, plus a little sway of its own.
// Everything that makes up a tree (trunk, stubs, boughs, leaves) calls this with its mesh-local vertex,
// so they move together. Grass and ferns are low, so it barely touches them.

const vec2 WIND_DIR = vec2(0.8, 0.6);
const float WIND_STRENGTH = 0.00045;   // metres of lean per metre of height squared (~0.35 m at 28 m)

vec3 tree_wind(vec3 v, mat4 model, float time) {
	vec3 base = model[3].xyz;
	float h = max(v.y, 0.0);
	float gust = 0.55 + 0.45 * sin(time * 0.31 + base.x * 0.012 + base.z * 0.009) * sin(time * 0.17 + base.z * 0.015);
	float own = 0.22 * sin(time * 0.9 + base.x * 0.37 + base.z * 0.23);
	float lean = WIND_STRENGTH * h * h * (gust + own);
	// the offset is in world space; carry it back into the mesh's space (MultiMesh instances are scaled/turned)
	vec3 world = vec3(WIND_DIR.x, 0.0, WIND_DIR.y) * lean;
	return v + (inverse(mat3(model)) * world);
}
