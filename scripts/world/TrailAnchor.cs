using Godot;

namespace ProjectDS.World;

/// <summary>
/// Places its parent along the trail: Distance metres from the trailhead,
/// Offset metres to the side (+ = right of the walking direction), then snaps
/// to the ground. Lets set dressing follow the trail if the trail is edited.
/// With <see cref="BeforeGroup"/> set, Distance is measured from the first node
/// in that group instead: that node's trail metres minus <see cref="BeforeMetres"/>
/// (so a thing can sit "12 m before the cabin" whatever the trail does).
/// </summary>
[GlobalClass]
public partial class TrailAnchor : Node
{
	[Export] public float Distance = 50f;
	[Export] public float Offset = 1.5f;
	/// <summary>Yaw relative to the trail direction, degrees (0 = parent -Z faces up the trail).</summary>
	[Export] public float YawDegrees = 0f;
	[Export] public float HeightOffset = 0f;
	/// <summary>If set, the anchor stands this many metres BEFORE the first node of this group along the trail.</summary>
	[Export] public string BeforeGroup = "";
	[Export] public float BeforeMetres = 12f;

	public override void _Ready()
	{
		if (Engine.IsEditorHint()) return;
		if (GetParent() is not Node3D target) return;
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain == null) return;
		float distance = Distance;
		if (!string.IsNullOrEmpty(BeforeGroup) && GetTree().GetFirstNodeInGroup(BeforeGroup) is Node3D landmark)
		{
			terrain.TrailDistance(landmark.GlobalPosition.X, landmark.GlobalPosition.Z, out float s);
			distance = Mathf.Max(0f, s - BeforeMetres);
		}
		Vector3 p = terrain.TrailPoint(distance, out Vector3 t);
		Vector3 right = t.Cross(Vector3.Up).Normalized();
		p += right * Offset;
		p.Y = terrain.HeightAt(p.X, p.Z) + HeightOffset;
		float yaw = Mathf.Atan2(-t.X, -t.Z) + Mathf.DegToRad(YawDegrees);
		target.GlobalTransform = new Transform3D(Basis.FromEuler(new Vector3(0, yaw, 0)).Scaled(target.Scale), p);
	}
}
