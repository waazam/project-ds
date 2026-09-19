using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// Sits at the foot of the first staircase the player finds. Stepping into it
/// takes movement away from the player entirely: they rise to the top landing
/// in a slow, silent, unnaturally smooth line, with no footsteps and no say in
/// it, their vision slowly closing and opening like heavy eyelids the whole
/// way up, then get control back. This is the story's Act 2 trigger; it
/// boards the cabin door and marks the checkpoint once the climb finishes.
/// </summary>
public partial class FirstClimbEvent : Area3D
{
	[Export] public NodePath TopMarkerPath = "../TopTrigger";
	[Export] public float ClimbSeconds = 13f;
	/// <summary>Seconds for one full close-and-open cycle of the vignette while climbing.</summary>
	[Export] public float BlinkSeconds = 4.5f;
	/// <summary>How far the vignette closes in at its darkest (the shader's own value, well beyond normal).</summary>
	[Export] public float BlinkVignette = 2.4f;

	private bool _triggered;

	public override void _Ready() => BodyEntered += OnBodyEntered;

	private void OnBodyEntered(Node3D body)
	{
		if (_triggered || body is not PlayerController player) return;
		_triggered = true;
		_ = Climb(player);
	}

	private async Task Climb(PlayerController player)
	{
		var top = GetNode<Node3D>(TopMarkerPath);
		Vector3 dest = top.GlobalPosition;

		player.PlayerInput.SetEnabled(false);
		player.SetPhysicsProcess(false);
		var feet = player.GetNodeOrNull<PlayerFootsteps>("Footsteps");
		feet?.SetPhysicsProcess(false);
		player.Velocity = Vector3.Zero;

		var postMat = GetTree().Root.FindChild("Screen", true, false) is ColorRect screen ? screen.Material as ShaderMaterial : null;
		float baseVignette = postMat != null ? (float)postMat.GetShaderParameter("vignette") : 0f;

		var tween = player.CreateTween();
		tween.TweenProperty(player, "global_position", dest, ClimbSeconds)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

		// Vision slowly falls shut and drifts open again, over and over, like heavy, involuntary
		// blinking. Runs alongside the position tween rather than gating on it, so the actual
		// climb-finished signal below (the only thing the checkpoint waits on) stays exact.
		bool climbing = true;
		_ = PulseVision(postMat, baseVignette, () => climbing);
		await player.ToSignal(tween, Tween.SignalName.Finished);
		climbing = false;
		postMat?.SetShaderParameter("vignette", baseVignette);

		player.SetPhysicsProcess(true);
		feet?.SetPhysicsProcess(true);
		player.PlayerInput.SetEnabled(true);

		if (GetTree().GetFirstNodeInGroup("cabin") is Cabin cabin) cabin.SetBoarded(true);
		StoryManager.Instance.ReachCheckpoint(Checkpoint.Act2StairsClimbed, player.GlobalPosition, player.CameraRig.Yaw);

		if (GetTree().Root.FindChild("ScreenFader", true, false) is ScreenFader fader)
			await fader.ShowCaption("", "It's getting late. Get back to the cabin.", 1.2f, 3.5f, 1.2f);
	}

	private async Task PulseVision(ShaderMaterial postMat, float baseVignette, System.Func<bool> stillClimbing)
	{
		if (postMat == null) return;
		double t = 0;
		while (stillClimbing())
		{
			t += GetProcessDeltaTime();
			float phase = (float)(t / Mathf.Max(BlinkSeconds, 0.1f)) * Mathf.Tau;
			float closed = Mathf.Sin(phase - Mathf.Pi / 2f) * 0.5f + 0.5f;   // 0 = open .. 1 = closed
			postMat.SetShaderParameter("vignette", Mathf.Lerp(baseVignette, BlinkVignette, closed));
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}
}
