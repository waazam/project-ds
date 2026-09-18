using Godot;

namespace ProjectDS.Audio;

/// <summary>
/// Marks a place where the forest stops making sound. Drop one under anything
/// that should hush the woods (a staircase, a scripted event). The
/// ForestAmbienceManager reads every zone in the "silence_zones" group; zones
/// never touch audio themselves.
///
/// Silence is 0 at OuterRadius and beyond, 1 at InnerRadius and within,
/// shaped by Falloff (&gt;1 = the hush arrives late and then fast).
/// </summary>
[GlobalClass]
public partial class SilenceZone : Node3D
{
	[Export] public float InnerRadius = 8f;
	[Export] public float OuterRadius = 70f;
	[Export(PropertyHint.Range, "0.3,4")] public float Falloff = 1.4f;
	/// <summary>Peak silence this zone can cause (1 = total).</summary>
	[Export(PropertyHint.Range, "0,1")] public float Strength = 1f;
	[Export] public bool Active = true;

	public override void _EnterTree() => AddToGroup("silence_zones");

	/// <summary>0..1 silence this zone applies at a world position (horizontal distance).</summary>
	public float SilenceAt(Vector3 worldPos)
	{
		if (!Active) return 0f;
		var d = new Vector2(worldPos.X - GlobalPosition.X, worldPos.Z - GlobalPosition.Z).Length();
		if (d >= OuterRadius) return 0f;
		if (d <= InnerRadius) return Strength;
		float t = 1f - (d - InnerRadius) / (OuterRadius - InnerRadius);
		t = t * t * (3f - 2f * t);
		return Mathf.Pow(t, Falloff) * Strength;
	}
}
