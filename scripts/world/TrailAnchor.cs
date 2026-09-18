using Godot;

namespace ProjectDS.World;

/// <summary>
/// Places its parent along the trail: Distance metres from the trailhead,
/// Offset metres to the side (+ = right of the walking direction), then snaps
/// to the ground. Lets set dressing follow the trail if the trail is edited.
/// </summary>
[GlobalClass]
public partial class TrailAnchor : Node
{
	[Export] public float Distance = 50f;
	[Export] public float Offset = 1.5f;
	/// <summary>Yaw relative to the trail direction, degrees (0 = parent -Z faces up the trail).</summary>
	[Export] public float YawDegrees = 0f;
	[Export] public float HeightOffset = 0f;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		if (GetParent() is not Node3D target) return;
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain == null) return;
		Vector3 p = terrain.TrailPoint(Distance, out Vector3 t);
		Vector3 right = t.Cross(Vector3.Up).Normalized();
		p += right * Offset;
		p.Y = terrain.HeightAt(p.X, p.Z) + HeightOffset;
		float yaw = Mathf.Atan2(-t.X, -t.Z) + Mathf.DegToRad(YawDegrees);
		target.GlobalTransform = new Transform3D(Basis.FromEuler(new Vector3(0, yaw, 0)).Scaled(target.Scale), p);
	}
}
