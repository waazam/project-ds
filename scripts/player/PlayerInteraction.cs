using Godot;
using ProjectDS.Systems;

namespace ProjectDS.Player;

/// <summary>
/// Looks straight out from the centre of the screen and focuses the
/// <see cref="Interactable"/> under the crosshair: within its reach and not
/// behind a wall. Focus drives the dim highlight and the HUD prompt (UI reads
/// <see cref="Focused"/> / <see cref="PromptText"/>); E presses and holds arrive
/// through PlayerInput, so a disabled or paused player can't use anything.
/// This is the only place interaction is decided.
///
/// A disabled or hidden interactable's pick sphere never shadows what lies
/// behind it (the boarded door's 0.9 m sphere must not swallow a note pinned on
/// the door): the probe re-casts past up to three such spheres before giving up.
/// </summary>
public partial class PlayerInteraction : Node
{
	[Export] public float ProbeDistance = 4f;

	public Interactable Focused { get; private set; }
	public string PromptText { get; private set; } = "";

	private PlayerController _player;
	// One query object for the lifetime of the node: only the ray's ends change per physics frame.
	// The exclude list is the player alone, except while re-casting past a disabled pick sphere.
	private PhysicsRayQueryParameters3D _query;
	private Godot.Collections.Array<Rid> _exclude;
	private const int MaxRecasts = 3;

	public override void _Ready()
	{
		_player = GetParent<PlayerController>();
		_exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
		_query = new PhysicsRayQueryParameters3D
		{
			CollisionMask = 1u | Interactable.PickLayer,
			CollideWithAreas = true,
			Exclude = _exclude,
		};
	}

	public override void _PhysicsProcess(double delta)
	{
		var input = _player.PlayerInput;
		// A blocked one (hands full) stays focusable so its prompt can say why; Probe skips disabled or hidden ones.
		var target = input.Enabled && !input.Modal ? Probe() : null;
		SetFocus(target);
		if (Focused == null) { PromptText = ""; return; }

		PromptText = Focused.GetPrompt(_player);
		if (!Focused.CanInteract(_player)) return;

		if (Focused.HoldSeconds <= 0f)
		{
			if (input.InteractPressed) Focused.Interact(_player);
			return;
		}
		if (input.InteractHeld)
		{
			Focused.HoldProgress = Mathf.Min(1f, Focused.HoldProgress + (float)delta / Focused.HoldSeconds);
			if (Focused.HoldProgress >= 1f) { Focused.HoldProgress = 0f; Focused.Interact(_player); }
		}
		else Focused.HoldProgress = 0f;
	}

	private Interactable Probe()
	{
		var cam = _player.CameraRig?.Camera;
		if (cam == null) return null;
		var from = cam.GlobalPosition;
		_query.From = from;
		_query.To = from + -cam.GlobalBasis.Z * ProbeDistance;
		var space = _player.GetWorld3D().DirectSpaceState;
		Interactable result = null;
		for (int cast = 0; ; cast++)
		{
			var hit = space.IntersectRay(_query);
			if (hit.Count == 0) break;
			// The first thing hit must be a pick volume; a wall in between blocks it.
			if (hit["collider"].AsGodotObject() is not Area3D area || area.GetParent() is not Interactable i) break;
			if (i.Enabled && i.IsVisibleInTree())
			{
				float dist = from.DistanceTo((Vector3)hit["position"]);
				result = dist <= i.MaxDistance ? i : null;
				break;
			}
			// A disabled or hidden one must not shadow what lies behind it: look past it and cast again.
			if (cast >= MaxRecasts) break;
			_exclude.Add(area.GetRid());
			_query.Exclude = _exclude;   // the native side copies the list, so it must be assigned again
		}
		if (_exclude.Count > 1)
		{
			_exclude.Resize(1);   // back to the player alone
			_query.Exclude = _exclude;
		}
		return result;
	}

	private void SetFocus(Interactable next)
	{
		if (next == Focused) return;
		if (Focused != null && IsInstanceValid(Focused)) Focused.SetFocused(false);
		Focused = next;
		Focused?.SetFocused(true);
	}
}
