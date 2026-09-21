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
/// </summary>
public partial class PlayerInteraction : Node
{
	[Export] public float ProbeDistance = 4f;

	public Interactable Focused { get; private set; }
	public string PromptText { get; private set; } = "";

	private PlayerController _player;

	public override void _Ready() => _player = GetParent<PlayerController>();

	public override void _PhysicsProcess(double delta)
	{
		var input = _player.PlayerInput;
		var target = input.Enabled ? Probe() : null;
		// A blocked one (hands full) stays focusable so its prompt can say why; disabled or hidden ones don't.
		if (target != null && !(target.Enabled && target.IsVisibleInTree())) target = null;
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
		var to = from + -cam.GlobalBasis.Z * ProbeDistance;
		var q = PhysicsRayQueryParameters3D.Create(from, to, 1u | Interactable.PickLayer);
		q.CollideWithAreas = true;
		q.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
		var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(q);
		if (hit.Count == 0) return null;
		// The first thing hit must be the pick volume itself; a wall in between blocks it.
		if (hit["collider"].AsGodotObject() is not Area3D area || area.GetParent() is not Interactable i) return null;
		float dist = from.DistanceTo((Vector3)hit["position"]);
		return dist <= i.MaxDistance ? i : null;
	}

	private void SetFocus(Interactable next)
	{
		if (next == Focused) return;
		if (Focused != null && IsInstanceValid(Focused)) Focused.SetFocused(false);
		Focused = next;
		Focused?.SetFocused(true);
	}
}
