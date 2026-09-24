using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// A world item the player can take with [E]. Lantern, Compass, Camera and the
/// newel post are equipped gear; Axe, Key and Hammer occupy the single tool slot
/// until used (rules live in PlayerInventory and are unchanged).
///
/// Interaction goes through an <see cref="Interactable"/> child
/// (<see cref="PickupInteractable"/>): the item highlights and shows its prompt
/// only while it is under the centre crosshair. A blocked pickup explains itself
/// ("Hands full: you're carrying the axe", "Locked: it needs the key").
///
/// Presentation: the item mesh comes from <see cref="ItemMeshes"/> and is
/// placed so it can always be seen. On the first physics frames it
/// <list type="bullet">
/// <item>moves onto its building's marker if there is one (Cabin "LanternSpot"/
/// "CompassSpot", Shed "HammerSpot", or groups "spot_lantern"/"spot_compass"/
/// "spot_hammer"), then</item>
/// <item>snaps down onto the actual collider under it with a small lift, so it
/// is never buried in a slope or floating over a porch.</item>
/// </list>
/// Some items rest on scenery that stays once they're taken (the axe's stump,
/// the key's stone).
///
/// If <see cref="RequiredCheckpoint"/> is set, the item stays hidden and
/// unusable until the story reaches it. Restore: a taken pickup records a flag
/// (the newel post uses StoryManager's own flag) and doesn't come back on Continue.
/// An optional <see cref="TakenLine"/> is captioned when the player takes it in
/// play (the newel post: "It's warm."), never on a restore.
/// </summary>
[Tool]
[GlobalClass]
public partial class Pickup : Area3D
{
	[Export] public ToolKind Kind = ToolKind.Axe;
	/// <summary>Kept for existing scenes; prompts use <see cref="HumanName"/>.</summary>
	[Export] public string Label = "Axe";
	[Export] public Checkpoint RequiredCheckpoint = Checkpoint.None;
	/// <summary>If set, the player must be holding this tool to take the item; it's consumed (a locked shed).</summary>
	[Export] public ToolKind RequiredKey = ToolKind.None;
	/// <summary>Gap left between the item and the surface it snaps onto.</summary>
	[Export] public float Lift = 0.004f;
	[Export] public bool SnapToSurface = true;
	/// <summary>Move onto the building marker for this kind (LanternSpot etc.) if there is one.</summary>
	[Export] public bool UseSpot = true;
	/// <summary>A thought captioned a moment after the player takes it (never on a restore), e.g. the newel post's "It's warm."</summary>
	[Export] public string TakenLine = "";

	public const string TakenFlagPrefix = "pickup_taken_";
	/// <summary>Set when more than one pickup of the same kind exists in the story (Act 4's key and the
	/// station clock's key): each then remembers being taken on its own.</summary>
	[Export] public string TakenId = "";
	public string TakenFlag => TakenFlagPrefix + (string.IsNullOrEmpty(TakenId) ? Kind.ToString().ToLowerInvariant() : TakenId);
	public bool Taken { get; private set; }
	public PickupInteractable Use => _use;

	private Node3D _item, _rest;
	private PickupInteractable _use;
	private ItemMeshes.Built _built;
	private bool _revealed;
	private bool _waitingForCheckpoint;
	private int _settleFrame;
	private bool _settled;
	private float _glowBase;
	private double _t;

	public override void _Ready()
	{
		// The Area3D is only a container now; the Interactable child owns the pick volume.
		CollisionLayer = 0;
		CollisionMask = 0;
		Monitoring = false;
		Monitorable = false;
		BuildVisual();
		if (Engine.IsEditorHint()) return;

		if (AlreadyTaken())
		{
			MarkTaken();
			return;
		}
		_revealed = RequiredCheckpoint == Checkpoint.None
			|| (StoryManager.Instance != null && StoryManager.Instance.Current >= RequiredCheckpoint);
		Visible = _revealed;
		// Not yet in the story: the checkpoint event reveals it (no per-frame poll).
		if (!_revealed && StoryManager.Instance is { } story)
		{
			_waitingForCheckpoint = true;
			story.CheckpointReached += OnCheckpoint;
		}

		_use = new PickupInteractable
		{
			Name = "Interactable",
			Prompt = "Take the " + HumanName(Kind),
			PickRadius = _built.PickRadius,
			PickOffset = _built.PickCenter,
			HighlightRoot = new NodePath("../Item"),
			Enabled = _revealed,
			CanUse = CanTake,
			PromptFor = PromptFor,
		};
		_use.Interacted += OnInteract;
		AddChild(_use);
	}

	/// <summary>Shows the item now regardless of checkpoint (dev previews only; the story reveals it normally).</summary>
	public void Reveal()
	{
		_revealed = true;
		Visible = true;
		if (_use != null) _use.Enabled = true;
		StopWaiting();
	}

	private void OnCheckpoint(Checkpoint cp)
	{
		if (!Taken && cp >= RequiredCheckpoint) Reveal();
	}

	private void StopWaiting()
	{
		if (!_waitingForCheckpoint) return;
		_waitingForCheckpoint = false;
		if (StoryManager.Instance is { } story) story.CheckpointReached -= OnCheckpoint;
	}

	public override void _ExitTree() => StopWaiting();

	public static string HumanName(ToolKind kind) => kind switch
	{
		ToolKind.Lantern => "lantern",
		ToolKind.Compass => "compass",
		ToolKind.Axe => "axe",
		ToolKind.Key => "key",
		ToolKind.Hammer => "hammer",
		ToolKind.Camera => "camera",
		ToolKind.NewelPost => "newel post",
		ToolKind.Radio => "walkie-talkie",
		ToolKind.Knife => "knife",
		ToolKind.Lighter => "lighter",
		ToolKind.DeadEye => "eye",
		ToolKind.PaleHand => "hand",
		ToolKind.StairTread => "step",
		_ => "item",
	};

	/// <summary>Gear (as opposed to the puzzle tools: axe, key, hammer, knife).</summary>
	private static bool IsGear(ToolKind kind)
		=> kind is ToolKind.Lantern or ToolKind.Compass or ToolKind.Camera or ToolKind.NewelPost or ToolKind.Radio;

	private static PlayerInventory Inv(PlayerController p) => p?.Inventory;

	private bool CanTake(PlayerController p)
	{
		var inv = Inv(p);
		if (inv == null || !_revealed || Taken) return false;
		if (RequiredKey != ToolKind.None) return inv.HasTool(RequiredKey);
		return true;
	}

	private string PromptFor(PlayerController p)
	{
		var inv = Inv(p);
		string item = HumanName(Kind);
		if (RequiredKey != ToolKind.None)
		{
			if (inv != null && inv.HasTool(RequiredKey)) return $"Unlock it with the {HumanName(RequiredKey)} and take the {item}";
			return $"Locked: it needs the {HumanName(RequiredKey)}";
		}
		return $"Take the {item}";
	}

	private void OnInteract(PlayerController player)
	{
		var inv = Inv(player);
		if (inv == null || !CanTake(player)) return;
		if (RequiredKey != ToolKind.None) inv.Consume(RequiredKey);
		if (!inv.TryPickup(Kind)) return;
		if (Kind == ToolKind.NewelPost) StoryManager.Instance?.MarkNewelPostTaken();
		else StoryManager.Instance?.SetFlag(TakenFlag);
		SayTakenLine();
		MarkTaken();
	}

	/// <summary>The taken line, a beat after the pickup feedback. Runs on the level (this pickup is freed as it's taken).</summary>
	private void SayTakenLine()
	{
		// Self-talk removed (Dan, 2026-09-22): the taken line ("It's warm.") is never shown; the property stays so scenes load.
		if (string.IsNullOrEmpty(TakenLine)) return;
	}

	private bool AlreadyTaken()
	{
		var story = StoryManager.Instance;
		if (story == null) return false;
		return Kind == ToolKind.NewelPost ? story.NewelPostTaken : story.HasFlag(TakenFlag);
	}

	/// <summary>The item is gone; any rest (stump, stone) stays where it was.</summary>
	private void MarkTaken()
	{
		Taken = true;
		if (_use != null) { _use.QueueFree(); _use = null; }
		if (_item != null) { _item.QueueFree(); _item = null; }
		if (_rest == null) QueueFree();
		else Visible = true;
	}

	// ───────────────────────────── placement ─────────────────────────────

	public override void _PhysicsProcess(double delta)
	{
		if (Engine.IsEditorHint() || _settled) return;
		// Wait a couple of physics frames so buildings have built their colliders and markers.
		if (++_settleFrame < 3) return;
		if (_settleFrame == 3)
		{
			MoveToSpot();
			// Old saves have no taken-flag: on a Continue, equipped gear already carried counts as taken.
			// (Only on a Continue: a fresh run that grants gear directly, e.g. a test skip, keeps its world pickups.)
			if (!Taken && IsGear(Kind) && Kind != ToolKind.NewelPost && StoryManager.Instance is { LoadedFromSave: true } && PlayerHas(Kind))
			{
				StoryManager.Instance.SetFlag(TakenFlag);
				MarkTaken();
			}
		}
		if (!SnapToSurface || TrySnap() || _settleFrame > 40)
		{
			_settled = true;
			if (SnapToSurface && _spot == null && Kind != ToolKind.NewelPost) StepOutOfProps();
			if (_rest != null) ItemMeshes.AddRestCollision(Kind, _rest);
		}
	}

	private bool PlayerHas(ToolKind kind)
	{
		var inv = (GetTree().GetFirstNodeInGroup("player") as PlayerController)?.Inventory;
		if (inv == null) return false;
		return kind switch
		{
			ToolKind.Lantern => inv.HasLantern,
			ToolKind.Compass => inv.HasCompass,
			ToolKind.Camera => inv.HasCamera,
			ToolKind.Radio => inv.HasRadio,
			_ => false,
		};
	}

	private Node3D _spot;

	private void MoveToSpot()
	{
		string name = Kind switch
		{
			ToolKind.Lantern => "LanternSpot",
			ToolKind.Compass => "CompassSpot",
			ToolKind.Hammer => "HammerSpot",
			_ => null,
		};
		if (name == null || !UseSpot) return;
		var parent = GetParent();
		_spot = parent?.GetNodeOrNull<Node3D>(name) ?? parent?.FindChild(name, true, false) as Node3D;
		_spot ??= GetTree().GetFirstNodeInGroup("spot_" + Kind.ToString().ToLowerInvariant()) as Node3D;
		if (_spot == null) return;
		GlobalTransform = new Transform3D(_spot.GlobalBasis.Orthonormalized(), _spot.GlobalPosition);
	}

	/// <summary>Drops the item onto the first world collider below it (layer 1). False = nothing there yet.</summary>
	private bool TrySnap()
	{
		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null) return false;
		Vector3 p = GlobalPosition;
		float above = _spot != null ? 0.3f : 0.8f;
		var q = PhysicsRayQueryParameters3D.Create(p + Vector3.Up * above, p + Vector3.Down * 4f, 1u);
		var hit = space.IntersectRay(q);
		if (hit.Count == 0) return false;
		Vector3 hp = (Vector3)hit["position"];
		Vector3 n = ((Vector3)hit["normal"]).Normalized();
		// A marker placed on a surface without collision (a shelf): trust the marker.
		if (_spot != null && hp.Y < p.Y - 0.08f) return true;
		GlobalPosition = new Vector3(p.X, hp.Y + Lift, p.Z);
		if (Kind is ToolKind.Key or ToolKind.Camera or ToolKind.Compass or ToolKind.Hammer && n.Y > 0.5f)
		{
			// Lie with the ground, a little: a small item on a slope shouldn't stand level in the air.
			float ang = Mathf.Min(Mathf.Acos(Mathf.Clamp(n.Y, -1f, 1f)), Mathf.DegToRad(20f));
			Vector3 axis = Vector3.Up.Cross(n);
			if (axis.LengthSquared() > 1e-6f)
				GlobalBasis = new Basis(axis.Normalized(), ang) * GlobalBasis;
		}
		return true;
	}

	/// <summary>
	/// An item dropped where a post, sign or rock already stands would be inside it and
	/// invisible: if its volume overlaps a world collider, move it to the nearest clear
	/// spot within a metre (then snap again).
	/// </summary>
	private void StepOutOfProps()
	{
		var space = GetWorld3D()?.DirectSpaceState;
		if (space == null) return;
		float r = Kind == ToolKind.Axe ? 0.3f : 0.14f;
		float h = Kind == ToolKind.Axe ? 0.45f : 0.24f;
		bool Blocked(Vector3 at)
		{
			var q = new PhysicsShapeQueryParameters3D
			{
				Shape = new SphereShape3D { Radius = r },
				Transform = new Transform3D(Basis.Identity, at + Vector3.Up * h),
				CollisionMask = 1u,
			};
			if (_rest?.GetNodeOrNull<StaticBody3D>("RestBody") is StaticBody3D own)
				q.Exclude = new Godot.Collections.Array<Rid> { own.GetRid() };
			return space.IntersectShape(q, 1).Count > 0;
		}
		Vector3 p = GlobalPosition;
		if (!Blocked(p)) return;
		foreach (float dist in new[] { 0.35f, 0.6f, 0.9f })
			for (int i = 0; i < 8; i++)
			{
				float a = Mathf.Tau * i / 8f;
				Vector3 c = p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * dist;
				if (Blocked(c)) continue;
				GlobalPosition = c;
				_spot = null;
				TrySnap();
				GD.Print($"[pickup] {Name} moved {dist:0.00} m out of a prop");
				return;
			}
	}

	// ───────────────────────────── visuals ─────────────────────────────

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint() || Taken) return;
		_t += delta;
		if (_built.Glow == null || !IsInstanceValid(_built.Glow)) { SetProcess(false); return; }   // only a lit wick needs a frame
		// A lit wick: slow breathing plus a faint quick flutter.
		float f = 0.88f + 0.08f * Mathf.Sin((float)_t * 1.7f) + 0.04f * Mathf.Sin((float)_t * 11.3f + 1.2f);
		_built.Glow.LightEnergy = _glowBase * f;
	}

	private void BuildVisual()
	{
		foreach (var n in new[] { "Item", "Rest", "Generated" })
		{
			var old = GetNodeOrNull(n);
			if (old != null) { RemoveChild(old); old.QueueFree(); }
		}
		_rest = new Node3D { Name = "Rest" };
		AddChild(_rest);
		if (!ItemMeshes.BuildRest(Kind, _rest, (int)Kind * 17))
		{
			RemoveChild(_rest);
			_rest.QueueFree();
			_rest = null;
		}
		_item = new Node3D { Name = "Item" };
		AddChild(_item);
		_built = ItemMeshes.Build(Kind, _item);
		_glowBase = _built.Glow?.LightEnergy ?? 0f;
	}
}
