using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Something on the Act 1 shot list that is not a bird: a child of the thing it
/// describes (sign, cabin, tent, mushrooms, stairs) or built in code by the thing
/// itself (the creek, under the footbridge). CameraTool asks every subject in
/// group "photo_subjects" to score itself for the shot; birds are judged first
/// and by their own unchanged rule.
///
/// Point subjects: one or more look points (local); a photo counts if any point
/// is within the distance band, inside the cone around the camera's axis and,
/// if required, not hidden behind world geometry (a hit on the subject's own
/// collider, i.e. anything under <see cref="OwnerPath"/>, counts as clear).
/// Vista subjects (the overlook): the player stands within <see cref="StandRadius"/>
/// of this node and looks roughly along <see cref="BearingDegrees"/>, not down.
/// </summary>
[GlobalClass]
public partial class PhotoSubject : Node3D
{
	/// <summary>Id in PhotoLog.Entries (also the save flag "photo_" + id).</summary>
	[Export] public string Id = "";
	[Export] public bool Vista;
	/// <summary>Local points the camera must have in the frame (point subjects).</summary>
	[Export] public Vector3[] LookPoints = { Vector3.Zero };
	[Export] public float MinDistance = 2f;
	[Export] public float MaxDistance = 14f;
	/// <summary>Half-angle of the cone around the camera's forward axis.</summary>
	[Export] public float ConeDegrees = 14f;
	[Export] public bool RequireLineOfSight = true;
	/// <summary>The node whose colliders count as the subject itself (a ray hitting them is clear). Default: the parent.</summary>
	[Export] public NodePath OwnerPath = "..";

	[ExportGroup("Vista")]
	[Export] public float StandRadius = 6f;
	/// <summary>Camera yaw (degrees, PlayerCameraRig convention: 0 = -Z, positive turns left toward -X).</summary>
	[Export] public float BearingDegrees;
	[Export] public float YawToleranceDegrees = 30f;
	[Export] public float MinPitchDegrees = -15f;

	public bool Captured => PhotoLog.Instance?.Has(Id) ?? false;

	public override void _Ready() => AddToGroup("photo_subjects");

	/// <summary>Whether this shot gets the subject. Lower score = more central (point subjects: the smallest angle to a look point).</summary>
	public bool TryScore(Camera3D cam, PlayerController player, out float score)
	{
		score = float.MaxValue;
		if (cam == null || string.IsNullOrEmpty(Id)) return false;
		Vector3 origin = cam.GlobalPosition;
		Vector3 fwd = -cam.GlobalBasis.Z;

		if (Vista)
		{
			if (player == null) return false;
			var flat = new Vector2(player.GlobalPosition.X - GlobalPosition.X, player.GlobalPosition.Z - GlobalPosition.Z);
			if (flat.Length() > StandRadius) return false;
			float yaw = Mathf.Atan2(-fwd.X, -fwd.Z);
			float pitch = Mathf.Asin(Mathf.Clamp(fwd.Y, -1f, 1f));
			float dYaw = Mathf.Abs(Mathf.AngleDifference(yaw, Mathf.DegToRad(BearingDegrees)));
			if (dYaw > Mathf.DegToRad(YawToleranceDegrees)) return false;
			if (pitch < Mathf.DegToRad(MinPitchDegrees)) return false;
			score = dYaw;
			return true;
		}

		float cosLimit = Mathf.Cos(Mathf.DegToRad(ConeDegrees));
		var xf = GlobalTransform;
		foreach (var local in LookPoints)
		{
			Vector3 p = xf * local;
			Vector3 to = p - origin;
			float dist = to.Length();
			if (dist < MinDistance || dist > MaxDistance || dist < 0.01f) continue;
			float dot = fwd.Dot(to / dist);
			if (dot < cosLimit) continue;
			if (RequireLineOfSight && !Clear(origin, p, player)) continue;
			float angle = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f));
			if (angle < score) score = angle;
		}
		return score < float.MaxValue;
	}

	private bool Clear(Vector3 from, Vector3 to, PlayerController player)
	{
		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null) return true;
		var q = PhysicsRayQueryParameters3D.Create(from, to, 1u);
		if (player is CollisionObject3D co) q.Exclude = new Godot.Collections.Array<Rid> { co.GetRid() };
		var hit = space.IntersectRay(q);
		if (hit.Count == 0) return true;
		// A hit on the subject's own geometry (the sign's plank, the cabin's wall) is the subject, not cover.
		var col = hit["collider"].AsGodotObject() as Node;
		var self = GetNodeOrNull<Node>(OwnerPath) ?? this;
		return col != null && (col == self || self.IsAncestorOf(col));
	}
}
