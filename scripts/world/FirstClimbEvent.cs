using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// Sits at the foot of the first staircase the player finds. Stepping into it
/// takes movement away from the player entirely: they rise to the top landing
/// in a slow, silent, unnaturally smooth line, their vision closing and
/// opening like heavy eyelids the whole way up — each blink deeper than the
/// last, until the last few go to total blackness — clearing fully the moment
/// they arrive. Then, still without control, the camera tips down to stare
/// off the top step for several seconds before they get to move again. This
/// is the story's Act 2 trigger; it boards the cabin door and marks the
/// checkpoint once the whole sequence finishes.
/// </summary>
public partial class FirstClimbEvent : Area3D
{
	[Export] public NodePath TopMarkerPath = "../TopTrigger";
	[Export] public float ClimbSeconds = 13f;
	/// <summary>Seconds for one full close-and-open cycle of the vignette while climbing.</summary>
	[Export] public float BlinkSeconds = 4.5f;
	/// <summary>How far the vignette closes in at its darkest, before blinks start going fully black.</summary>
	[Export] public float BlinkVignette = 2.4f;
	/// <summary>Fraction of the climb (0..1) at which blinks start reaching total blackness, ramping up from there.</summary>
	[Export] public float BlackoutStartFraction = 0.55f;

	[ExportGroup("Look down at the top")]
	[Export] public float LookDownPanSeconds = 3f;
	[Export] public float LookDownHoldSeconds = 5f;
	[Export] public float LookUpSeconds = 2f;
	[Export] public float LookDownPitchDegrees = -78f;

	private bool _triggered;

	public override void _Ready() => BodyEntered += OnBodyEntered;

	private void OnBodyEntered(Node3D body)
	{
		if (_triggered || body is not PlayerController player) return;
		_triggered = true;
		player.GetNodeOrNull<PlayerInventory>("Inventory")?.TakeAwayCamera();
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
		var fader = GetTree().Root.FindChild("ScreenFader", true, false) as ScreenFader;

		var tween = player.CreateTween();
		tween.TweenProperty(player, "global_position", dest, ClimbSeconds)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

		// Vision slowly falls shut and drifts open again, over and over, like heavy, involuntary
		// blinking, deepening into full blackness by the end. Runs alongside the position tween
		// rather than gating on it, so the actual climb-finished signal below (the only thing the
		// checkpoint waits on) stays exact.
		bool climbing = true;
		_ = PulseVision(postMat, fader, baseVignette, () => climbing);
		await player.ToSignal(tween, Tween.SignalName.Finished);
		climbing = false;
		// Vision resolves fully open and clear the instant they arrive at the top.
		postMat?.SetShaderParameter("vignette", baseVignette);
		if (fader != null) fader.BlackAlpha = 0f;

		if (GetTree().GetFirstNodeInGroup("cabin") is Cabin cabin) cabin.SetBoarded(true);

		// Still no control: the camera tips down on its own to stare off the top step, holds,
		// then lifts back to a normal forward view — control returns with the player already
		// looking where they're walking, not stuck staring at their feet.
		var rig = player.CameraRig;
		float levelPitch = rig.Pitch;
		var panTween = player.CreateTween();
		panTween.TweenMethod(Callable.From<float>(rig.SetPitch), rig.Pitch, Mathf.DegToRad(LookDownPitchDegrees), LookDownPanSeconds)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await player.ToSignal(panTween, Tween.SignalName.Finished);
		if (LookDownHoldSeconds > 0f)
			await ToSignal(GetTree().CreateTimer(LookDownHoldSeconds), SceneTreeTimer.SignalName.Timeout);

		var riseTween = player.CreateTween();
		riseTween.TweenMethod(Callable.From<float>(rig.SetPitch), rig.Pitch, levelPitch, LookUpSeconds)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await player.ToSignal(riseTween, Tween.SignalName.Finished);

		player.SetPhysicsProcess(true);
		feet?.SetPhysicsProcess(true);
		player.PlayerInput.SetEnabled(true);

		StoryManager.Instance.ReachCheckpoint(Checkpoint.Act2StairsClimbed, player.GlobalPosition, player.CameraRig.Yaw);

		if (fader != null)
			await fader.ShowCaption("", "It's getting late. Get back to the cabin.", 1.2f, 3.5f, 1.2f);
	}

	private async Task PulseVision(ShaderMaterial postMat, ScreenFader fader, float baseVignette, System.Func<bool> stillClimbing)
	{
		if (postMat == null && fader == null) return;
		double t = 0;
		while (stillClimbing())
		{
			t += GetProcessDeltaTime();
			float phase = (float)(t / Mathf.Max(BlinkSeconds, 0.1f)) * Mathf.Tau;
			float closed = Mathf.Sin(phase - Mathf.Pi / 2f) * 0.5f + 0.5f;   // 0 = open .. 1 = closed
			postMat?.SetShaderParameter("vignette", Mathf.Lerp(baseVignette, BlinkVignette, closed));
			// Early blinks are just a heavy vignette; from BlackoutStartFraction on, the closed
			// part of each blink ramps toward total, screen-covering blackness.
			float blackoutIntensity = Mathf.Clamp((float)(t / Mathf.Max(ClimbSeconds, 0.1f) - BlackoutStartFraction) / Mathf.Max(1f - BlackoutStartFraction, 0.05f), 0f, 1f);
			if (fader != null) fader.BlackAlpha = closed * blackoutIntensity;
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}
}
