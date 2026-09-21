using Godot;

namespace ProjectDS.Player;

/// <summary>
/// A running gas tank. Drains while actually running (held key and moving),
/// refills faster at rest than on the move, and — once fully spent — locks
/// running out again until it has climbed back to <see cref="MinToResumeRun"/>,
/// so sprint can't be feathered on and off one frame at a time at empty.
/// </summary>
public partial class PlayerStamina : Node
{
	[Export] public float DrainPerSecond = 0.22f;
	[Export] public float RegenPerSecondMoving = 0.10f;
	[Export] public float RegenPerSecondResting = 0.22f;
	[Export] public float MinToResumeRun = 0.16f;

	public float Value { get; private set; } = 1f;
	public bool CanRun { get; private set; } = true;

	private PlayerController _player;

	public override void _Ready() => _player = GetParent<PlayerController>();

	public override void _PhysicsProcess(double delta)
	{
		// The autotest bot runs almost continuously for hundreds of metres at a stretch; every wait
		// budget throughout AutoTest.cs was tuned against an unlimited sprint, long before this system
		// existed. Gate the drain on real input rather than retune a long chain of unrelated timeouts.
		if (_player.PlayerInput.Scripted) { Value = 1f; CanRun = true; return; }

		float dt = (float)delta;
		if (_player.IsRunning)
		{
			Value = Mathf.Max(0f, Value - DrainPerSecond * dt);
		}
		else
		{
			float regen = _player.GroundSpeed < 0.15f ? RegenPerSecondResting : RegenPerSecondMoving;
			Value = Mathf.Min(1f, Value + regen * dt);
		}

		if (Value <= 0f) CanRun = false;
		else if (Value >= MinToResumeRun) CanRun = true;
	}
}
