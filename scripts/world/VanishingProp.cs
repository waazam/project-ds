using Godot;

namespace ProjectDS.World;

/// <summary>
/// A "did I see that" object. It stays put until the player (group "player")
/// either comes within HideDistance, or has genuinely seen it (in the current
/// camera's frustum, unobstructed, within SeenDistance) and it has since left
/// the view. Then it hides for good. Nothing animates: it is simply gone the
/// next time you look.
///
/// Children are hidden and their collision disabled; SilenceZones underneath are deactivated.
/// </summary>
[GlobalClass]
public partial class VanishingProp : Node3D
{
	[Signal] public delegate void VanishedEventHandler();

	[Export] public float HideDistance = 22f;
	[Export] public float SeenDistance = 75f;
	/// <summary>Local point (roughly the visual centre) used for view and line-of-sight checks.</summary>
	[Export] public Vector3 LookPoint = new(0, 0.6f, 0);
	/// <summary>Seconds it must be continuously in view to count as seen.</summary>
	[Export] public float SeenTime = 0.25f;
	/// <summary>Seconds out of view (after being seen) before it goes.</summary>
	[Export] public float GoneAfter = 0.2f;
	[Export] public bool RequireLineOfSight = true;
	[Export(PropertyHint.Layers3DPhysics)] public uint OcclusionMask = 1;

	public bool HasBeenSeen { get; private set; }
	public bool IsGone { get; private set; }

	private float _inView, _outView;

	public override void _PhysicsProcess(double delta)
	{
		if (IsGone) return;
		var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
		if (player == null) return; // nothing to haunt
		float dt = (float)delta;

		Vector3 look = GlobalTransform * LookPoint;
		var flatDist = new Vector2(player.GlobalPosition.X - look.X, player.GlobalPosition.Z - look.Z).Length();
		if (flatDist < HideDistance) { Vanish(); return; }

		var cam = GetViewport().GetCamera3D();
		if (cam == null) return;
		bool inView = cam.IsPositionInFrustum(look) && cam.GlobalPosition.DistanceTo(look) < SeenDistance;
		if (inView && RequireLineOfSight)
		{
			var q = PhysicsRayQueryParameters3D.Create(cam.GlobalPosition, look, OcclusionMask);
			if (player is CollisionObject3D co) q.Exclude = new Godot.Collections.Array<Rid> { co.GetRid() };
			var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
			if (hit.Count > 0)
			{
				// ignore hits on our own colliders
				var col = hit["collider"].AsGodotObject() as Node;
				inView = col != null && IsAncestorOf(col);
			}
		}

		if (inView)
		{
			_inView += dt;
			_outView = 0;
			if (_inView >= SeenTime) HasBeenSeen = true;
		}
		else
		{
			_inView = 0;
			if (HasBeenSeen)
			{
				// Only vanish when it's out of the frustum entirely, not just blocked by a trunk.
				if (!cam.IsPositionInFrustum(look)) _outView += dt;
				if (_outView >= GoneAfter) Vanish();
			}
		}
	}

	public void Vanish()
	{
		if (IsGone) return;
		IsGone = true;
		Visible = false;
		SetAll(this);
		EmitSignal(SignalName.Vanished);
	}

	private static void SetAll(Node n)
	{
		foreach (var c in n.GetChildren())
		{
			if (c is CollisionShape3D cs) cs.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
			if (c is CollisionObject3D co) co.SetDeferred(CollisionObject3D.PropertyName.ProcessMode, (int)ProcessModeEnum.Disabled);
			if (c is ProjectDS.Audio.SilenceZone sz) sz.Active = false;
			SetAll(c);
		}
	}
}
