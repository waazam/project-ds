using System.Threading;
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
///
/// Only fires before Act 2 has happened, so it can never collide with Act 11's
/// climb of the same staircase.
/// </summary>
public partial class FirstClimbEvent : StoryTrigger
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
	[Export] public float LookDownPitchDegrees = -78f;

	[ExportGroup("Blackout and wake")]
	[Export] public float BlackoutSeconds = 2.5f;
	[Export] public float BlackHoldSeconds = 3f;
	/// <summary>Seconds for sight to come back and the head to lift at the bridge.</summary>
	[Export] public float WakeSeconds = 6f;
	/// <summary>Camera pitch at the moment of waking: face toward the ground.</summary>
	[Export] public float WakePitchDegrees = -55f;

	protected override bool AlreadyHappened(StoryManager s) => s.Current >= Checkpoint.Act2StairsClimbed;
	protected override bool CanFire(StoryManager s, PlayerController p) => s.Current < Checkpoint.Act2StairsClimbed;

	protected override void Fire(PlayerController player)
	{
		player.GetNodeOrNull<PlayerInventory>("Inventory")?.TakeAwayCamera();
		var feet = player.GetNodeOrNull<PlayerFootsteps>("Footsteps");
		var postMat = StoryBeat.PostMaterial(this);
		float baseVignette = postMat != null ? (float)postMat.GetShaderParameter("vignette") : 0f;
		var fader = StoryBeat.Fader(this);

		_ = Cutscene.Run(this, async ct =>
		{
			feet?.SetPhysicsProcess(false);
			try
			{
				await Climb(player, postMat, baseVignette, fader, ct);
			}
			finally
			{
				// Whatever happens, vision comes back and the feet work again.
				postMat?.SetShaderParameter("vignette", baseVignette);
				if (fader != null && IsInstanceValid(fader)) fader.BlackAlpha = 0f;
				if (feet != null && IsInstanceValid(feet)) feet.SetPhysicsProcess(true);
			}
			StoryBeat.ReachCheckpoint(player, Checkpoint.Act2StairsClimbed);
			// The caption plays with control already back.
			_ = Cutscene.Run(this, _ => StoryBeat.Caption(this, "It's getting late. Get back to the cabin.", 1.2f, 3.5f, 1.2f));
		}, lockInput: true, freezeBody: true);
	}

	private async Task Climb(PlayerController player, ShaderMaterial postMat, float baseVignette, ScreenFader fader, CancellationToken ct)
	{
		Vector3 dest = GetNode<Node3D>(TopMarkerPath).GlobalPosition;
		player.Velocity = Vector3.Zero;

		var tween = player.CreateTween();
		tween.TweenProperty(player, "global_position", dest, ClimbSeconds)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);

		// Vision slowly falls shut and drifts open again, over and over, like heavy, involuntary
		// blinking, deepening into full blackness by the end, frame by frame alongside the tween.
		double t = 0;
		while (tween.IsValid() && tween.IsRunning())
		{
			t += GetProcessDeltaTime();
			float phase = (float)(t / Mathf.Max(BlinkSeconds, 0.1f)) * Mathf.Tau;
			float closed = Mathf.Sin(phase - Mathf.Pi / 2f) * 0.5f + 0.5f;   // 0 = open .. 1 = closed
			postMat?.SetShaderParameter("vignette", Mathf.Lerp(baseVignette, BlinkVignette, closed));
			// Early blinks are just a heavy vignette; from BlackoutStartFraction on, the closed
			// part of each blink ramps toward total, screen-covering blackness.
			float blackout = Mathf.Clamp((float)(t / Mathf.Max(ClimbSeconds, 0.1f) - BlackoutStartFraction) / Mathf.Max(1f - BlackoutStartFraction, 0.05f), 0f, 1f);
			if (fader != null) fader.BlackAlpha = closed * blackout;
			try { await Cutscene.Frame(this, ct); }
			catch (System.OperationCanceledException) { tween.Kill(); throw; }
		}
		// Vision resolves fully open and clear the instant they arrive at the top.
		postMat?.SetShaderParameter("vignette", baseVignette);
		if (fader != null) fader.BlackAlpha = 0f;

		StoryBeat.Cabin(this)?.SetBoarded(true);

		// Still no control: the camera tips down on its own to stare off the top step and holds there.
		var rig = player.CameraRig;
		float levelPitch = rig.Pitch;
		var pan = player.CreateTween();
		pan.TweenMethod(Callable.From<float>(rig.SetPitch), rig.Pitch, Mathf.DegToRad(LookDownPitchDegrees), LookDownPanSeconds)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await Cutscene.Tween(this, pan, ct);
		if (LookDownHoldSeconds > 0f) await Cutscene.Wait(this, LookDownHoldSeconds, ct);

		// Then everything goes black. The stairs let go of them: they come to on the near bank of
		// the footbridge, face down in the dirt, and lift their head slowly as their sight clears —
		// the first taste of lost time.
		if (fader != null)
		{
			var toBlack = player.CreateTween();
			toBlack.TweenProperty(fader, nameof(ScreenFader.BlackAlpha), 1f, BlackoutSeconds)
				.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
			await Cutscene.Tween(this, toBlack, ct);
			fader.BlackAlpha = 1f;
		}
		await Cutscene.Wait(this, BlackHoldSeconds, ct);

		var (wakePos, wakeYaw) = WakeSpot(this);
		player.Teleport(wakePos, wakeYaw);
		rig.SetPitch(Mathf.DegToRad(WakePitchDegrees));
		postMat?.SetShaderParameter("vignette", BlinkVignette);

		float wakePitch = rig.Pitch;
		var wake = player.CreateTween();
		wake.TweenMethod(Callable.From<float>(p =>
		{
			if (fader != null) fader.BlackAlpha = 1f - p;
			postMat?.SetShaderParameter("vignette", Mathf.Lerp(BlinkVignette, baseVignette, p));
			rig.SetPitch(Mathf.LerpAngle(wakePitch, levelPitch, Mathf.SmoothStep(0f, 1f, p)));
		}), 0f, 1f, WakeSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await Cutscene.Tween(this, wake, ct);
		GD.Print("[story] Act 2: woke at the bridge");
	}

	/// <summary>
	/// Where the stairs leave the player after the first climb, and where Continue puts them at
	/// this checkpoint: on the ground a few metres off the cabin-side end of the footbridge,
	/// facing back down the trail toward the cabin.
	/// </summary>
	public static (Vector3 pos, float yaw) WakeSpot(Node n)
	{
		var bridge = n.GetTree().GetFirstNodeInGroup("bridge_marker") as Node3D;
		Vector3 p = bridge != null ? bridge.GlobalPosition + new Vector3(0, 0, WakeOffsetFromBridge) : Vector3.Zero;
		var terrain = GroundSnap.FindTerrain(n);
		if (terrain != null) p.Y = terrain.HeightAt(p.X, p.Z);
		return (p + Vector3.Up * 0.15f, Mathf.Pi);   // yaw π = facing +Z, back toward the cabin
	}

	/// <summary>Metres along +Z from the bridge's centre to the wake spot (the bridge is 9 m long).</summary>
	public const float WakeOffsetFromBridge = 7.5f;
}
