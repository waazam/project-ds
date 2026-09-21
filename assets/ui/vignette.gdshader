// A soft near-black vignette for menu overlays, so text sits in shadow.
shader_type canvas_item;

uniform float strength = 0.6;
uniform vec3 tint = vec3(0.043, 0.051, 0.063);  // website --night

void fragment() {
	vec2 d = UV - 0.5;
	d.x *= 1.3;
	float v = smoothstep(0.15, 0.75, length(d)) * strength;
	COLOR = vec4(tint, v);
}
