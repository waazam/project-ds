// The weather's particles (the weather pass, 2026-10-03): snow, rain, dust and spores, all from this one process
// shader. They live in a box that travels with the camera and wraps round it: a flake that drifts out of one side
// comes back in at the other, one that falls out of the bottom (or settles on the ground, a roof, a bough) starts
// again at the top. So the air is equally thick wherever you are and however fast you go, and nothing trails behind
// you or pops in ahead. Every particle has its own fall, flutter and spin; the shared wind (the global `wind`, with
// its gusts in `wind_gust`) carries them all.
//
// What the draw shader (weather_flake.gdshader) gets: COLOR.rgb its velocity (m/s), COLOR.a its share of the
// density (it fades in and out with `density`, never pops), CUSTOM.x its spin angle, CUSTOM.z its frame in the atlas.
shader_type particles;
render_mode disable_velocity, disable_force;

global uniform vec3 wind;
global uniform float wind_gust;

/// The box's middle (the camera, led a little the way it's going: the script's) and its half-size.
uniform vec3 centre;
uniform vec3 half_extent = vec3(14.0, 8.0, 14.0);
/// 0..1: the share of the particles that show (the rest fade out): the snow's heaviness.
uniform float density = 1.0;
/// Its own fall (m/s, min..max), and how much the wind carries it (1 snow, 0.25 rain's heavier drops).
uniform vec2 fall = vec2(0.7, 1.3);
uniform float wind_carry = 1.0;
/// The flutter: a lazy sideways wander (m/s) and how quickly it turns (per second); the spin (radians a second).
uniform float flutter = 0.3;
uniform float flutter_rate = 0.9;
uniform float spin = 1.2;
/// Its size (metres, min..max) and its frames in the 4 x 4 atlas (first, count).
uniform vec2 size_range = vec2(0.02, 0.04);
uniform vec2 frames = vec2(0.0, 16.0);
/// 1: what lands (on the ground, a roof, a bough: the heightfield collider) starts again at the top. 0: it ignores
/// them (dust and spores indoors).
uniform float settle = 1.0;
/// A little random speed-up per particle on the gusts (snow: the light ones swirl more).
uniform float gust_swirl = 1.0;
/// Interiors (the church, ...): boxes, each its world-to-local transform and half-size; nothing shows inside one (the
/// roof collider sees roofs from above, but a great vault's own surfaces may face inward).
uniform int shelter_count = 0;
uniform mat4 shelter_inv[8];
uniform vec3 shelter_half[8];

float hash(uint x) {
	x ^= x >> 16u; x *= 0x7feb352du;
	x ^= x >> 15u; x *= 0x846ca68bu;
	x ^= x >> 16u;
	return float(x) / 4294967295.0;
}

vec3 wrap(vec3 p) {
	vec3 rel = p - centre;
	rel = mod(rel + half_extent, half_extent * 2.0) - half_extent;
	return centre + rel;
}

void start() {
	uint s = NUMBER * 747796405u + RANDOM_SEED * 2891336453u + INDEX * 277803737u;
	float r0 = hash(s), r1 = hash(s + 1u), r2 = hash(s + 2u), r3 = hash(s + 3u), r4 = hash(s + 4u), r5 = hash(s + 5u), r6 = hash(s + 6u);
	if (RESTART_POSITION) {
		vec3 p = centre + (vec3(r0, r1, r2) * 2.0 - 1.0) * half_extent;
		float sz = mix(size_range.x, size_range.y, r3 * r3);
		TRANSFORM = mat4(vec4(sz, 0.0, 0.0, 0.0), vec4(0.0, sz, 0.0, 0.0), vec4(0.0, 0.0, sz, 0.0), vec4(p, 1.0));
	}
	if (RESTART_CUSTOM) {
		// x: spin angle; y: its own phase and pace (fall and flutter); z: its frame; w: its density rank (stable)
		CUSTOM = vec4(r4 * 6.2831853, r5, floor(frames.x + min(r6 * frames.y, frames.y - 0.001)), hash(INDEX * 2654435761u + 17u));
	}
	if (RESTART_VELOCITY) VELOCITY = vec3(0.0);
	if (RESTART_COLOR) COLOR = vec4(0.0, -1.0, 0.0, 0.0);
}

void process() {
	vec3 p = TRANSFORM[3].xyz;
	float pace = CUSTOM.y;
	float t = TIME * flutter_rate + pace * 31.0;
	// its fall, the wind carrying it (the gusts more for the light ones), and its own lazy wander
	float own = mix(fall.x, fall.y, pace);
	float gust = 1.0 + wind_gust * gust_swirl * (0.6 + 0.8 * pace);
	vec3 v = vec3(0.0, -own, 0.0) + wind * wind_carry * gust
		+ flutter * vec3(sin(t) + 0.5 * sin(t * 2.3 + 1.7), 0.35 * sin(t * 1.7 + 0.4), cos(t * 0.8 + pace * 5.0) + 0.5 * sin(t * 1.9));
	p += v * DELTA;
	// settled (on the ground, a roof, a bough): up to the top of the box again, a little way off, unseen until it's
	// under open sky (in a great hall the box's top is still under the roof: there it stays hidden, so it never snows
	// indoors, and from inside only the snow beyond the windows shows)
	bool hit = settle > 0.5 && COLLIDED;
	if (hit) {
		p.y = centre.y + half_extent.y - 0.05;
		p.x += (fract(pace * 91.7 + TIME * 0.37) - 0.5) * half_extent.x;
		p.z += (fract(pace * 53.3 + TIME * 0.29) - 0.5) * half_extent.z;
	}
	p = wrap(p);
	TRANSFORM[3].xyz = p;
	VELOCITY = v;
	CUSTOM.x += spin * DELTA * (pace - 0.5) * 2.0;
	// its share of the density, faded (a 6% band: no pops as the snow thickens or thins)
	float keep = clamp((density - CUSTOM.w) / 0.06 + 0.5, 0.0, 1.0);
	if (hit) keep = 0.0;
	for (int i = 0; i < shelter_count; i++) {
		vec3 l = (shelter_inv[i] * vec4(p, 1.0)).xyz;
		if (all(lessThan(abs(l), shelter_half[i]))) { keep = 0.0; break; }
	}
	COLOR = vec4(v, keep);
}
