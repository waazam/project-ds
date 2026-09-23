using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// A simple hinged interior door for the forester station (Act 13): locked (E just says so) until
/// either another puzzle calls <see cref="Unlock"/>, or - if <see cref="RequiredTool"/> is set -
/// the player uses it while carrying that tool (which it consumes, the way the cabin's axe/hammer
/// do). Either way, once unlocked E swings it open on its hinge and it stays open for good.
/// This node's own origin is the hinge line — the caller (<see cref="StationInterior"/>) parents
/// the door-leaf mesh to it with local X=0 at the hinge, so opening is just rotating this node.
/// The doorway itself carries no collision (an empty gap in the wall); a separate blocking
/// collider stands in the opening while locked and is freed the instant it opens, so the player
/// can walk through as soon as the door starts swinging rather than waiting on the animation.
/// </summary>
public partial class StationDoor : Node3D
{
	[Export] public string LockedPrompt = "It's locked.";
	[Export] public string UnlockedPrompt = "Open the door";
	[Export] public ToolKind RequiredTool = ToolKind.None;
	[Export] public float OpenDegrees = 105f;
	[Export] public float OpenSeconds = 0.8f;

	public bool Locked { get; private set; } = true;
	public bool IsOpen { get; private set; }

	private PickupInteractable _use;
	private StaticBody3D _blocker;

	/// <summary>Adds the interact point and the locked-state blocking collider (a box the size of the
	/// doorway opening, in this node's local space).</summary>
	public void Setup(Vector3 pickPosition, Vector3 blockerSize, Vector3 blockerLocalCenter)
	{
		_use = new PickupInteractable
		{
			Name = "Use", Prompt = LockedPrompt, PickRadius = 0.8f, MaxDistance = 2.8f, Position = pickPosition,
			PromptFor = PromptFor,
		};
		_use.Interacted += OnUsed;
		AddChild(_use);

		_blocker = new StaticBody3D { Name = "Blocker", CollisionLayer = 1, CollisionMask = 0 };
		_blocker.AddChild(new CollisionShape3D { Position = blockerLocalCenter, Shape = new BoxShape3D { Size = blockerSize } });
		AddChild(_blocker);
	}

	private string PromptFor(PlayerController player)
	{
		if (!Locked) return UnlockedPrompt;
		if (RequiredTool != ToolKind.None && player?.Inventory is { } inv && inv.HasTool(RequiredTool))
			return $"Use the {RequiredTool.ToString().ToLowerInvariant()}";
		return LockedPrompt;
	}

	/// <summary>Called by whatever puzzle guards this door once it's solved (no tool needed): unlocks
	/// it AND swings it open right away, so nothing physically blocks the doorway a moment longer
	/// than the puzzle itself does. (An E-press-driven open, for a door a player walks up to and
	/// uses themselves once it's unlocked, is still available below via RequiredTool/OnUsed.)</summary>
	public void Unlock()
	{
		if (!Locked) return;
		Locked = false;
		Open();
	}

	private void OnUsed(PlayerController player)
	{
		if (IsOpen) return;
		if (Locked)
		{
			if (RequiredTool == ToolKind.None || player?.Inventory is not { } inv || !inv.HasTool(RequiredTool)) return;
			inv.Consume(RequiredTool);
			Locked = false;
		}
		Open();
	}

	private void Open()
	{
		if (IsOpen) return;
		IsOpen = true;
		if (_use != null) _use.Enabled = false;
		if (_blocker != null) { _blocker.QueueFree(); _blocker = null; }
		var tween = CreateTween();
		tween.TweenProperty(this, "rotation:y", Rotation.Y + Mathf.DegToRad(OpenDegrees), OpenSeconds)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
	}
}
