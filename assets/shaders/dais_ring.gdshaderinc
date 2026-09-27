// Act 20: the ring of light round the dais. UV.x runs once round the ring (0..1), UV.y across the band.
// Red until the power comes back; then a green chaser runs round it, its tail growing each lap, until
// the whole ring is solid green.
uniform float head = 0.0;        // the chaser's head, in turns
uniform float tail = 0.0;        // how much of the ring behind the head is green (0 none, 1 all)
uniform float intensity = 1.0;
uniform vec3 red : source_color = vec3(1.0, 0.06, 0.04);
uniform vec3 green : source_color = vec3(0.18, 1.0, 0.32);

vec3 ring_colour(float a) {
	float behind = fract(head - a);
	float g = tail >= 0.999 ? 1.0 : (tail <= 0.0 ? 0.0 : 1.0 - smoothstep(tail - 0.03, tail, behind));
	float spark = (tail > 0.0 && tail < 0.999) ? exp(-behind * 28.0) : 0.0;
	return (mix(red, green, g) + green * spark * 1.2) * intensity;
}
