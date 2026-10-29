using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// The game's opening, on a new game only. First the drive on a parchment map, with the assignment and the
/// editor's page (<see cref="PrologueMap"/>; STORY.md Rev 5, 2026-10-16: it replaces the two black cards, and
/// there is no drive scene). The map dissolves straight to the player standing at the open back of their
/// car in the trailhead lot, looking down at the camera in the trunk. The camera lifts out of the trunk toward them and is in hand (the Act 1 camera; no
/// pickup on the ground). Their head comes up toward the trail, control returns, and a quiet line
/// under the picture says how the camera works.
///
/// Placement (in the trailhead level, group "opening"): a child Marker3D "Stand" where the player
/// stands (its -Z faces the trunk) and a child Node3D "TrunkCamera" holding the camera prop in the
/// trunk (it is animated toward the eye and then hidden). The car itself is a ParkProp next to it.
///
/// Restore: only GameFlow's fresh-game path runs it. On Continue the camera comes back through the
/// saved inventory, and the trunk camera is hidden whenever the camera has already been taken.
/// </summary>
public partial class OpeningAtCar : Node3D
{
	[Export] public float MapDissolveSeconds = 2.4f;
	[Export] public string HowToLine = "Right mouse: raise the camera.   Space: focus.   Left mouse: take a picture.   Tab: your photos.";
	[Export] public NodePath StandPath = "Stand";
	[Export] public NodePath TrunkCameraPath = "TrunkCamera";
	/// <summary>Where the head turns to once the camera is in hand (usually the trailhead sign). Empty: straight ahead of Stand, turned half round.</summary>
	[Export] public NodePath LookAfterPath = new();
	[Export] public float LookIntoTrunkPitch = -38f;

	public override void _EnterTree() => AddToGroup("opening");

	public override void _Ready()
	{
		// A camera already taken (Continue, or a replay of the level) is not in the trunk.
		if (StoryManager.Instance is { } s && s.HasFlag(StoryManager.Flag.PickupTakenCamera))
			GetNodeOrNull<Node3D>(TrunkCameraPath)?.Hide();
	}

	/// <summary>Where GameFlow puts the player before the fade-in.</summary>
	public (Vector3 pos, float yaw) StandPose()
	{
		var stand = GetNodeOrNull<Node3D>(StandPath) ?? this;
		var fwd = -stand.GlobalBasis.Z;
		return (stand.GlobalPosition + Vector3.Up * 0.1f, Mathf.Atan2(-fwd.X, -fwd.Z));
	}

	/// <summary>The whole opening. The caller holds the input lock (GameFlow's own reference) and the screen is black.</summary>
	public async Task Run(PlayerController player, ScreenFader fader, bool quick, CancellationToken ct)
	{
		var rig = player.CameraRig;
		float levelPitch = 0f;
		rig.SetPitch(Mathf.DegToRad(LookIntoTrunkPitch));

		if (!quick)
		{
			// The map over the black, then it dissolves to the trunk: the fader's black is cleared under the map while
			// the map still covers the whole picture, so the trunk is only ever seen through the map going.
			await Cutscene.Wait(this, 0.4f, ct);
			var map = new PrologueMap { Name = "PrologueMap" };
			AddChild(map);
			try
			{
				await map.Play(ct);
				// The rig resets its pitch when the level boots: look into the trunk again right before the picture comes up.
				rig.SetPitch(Mathf.DegToRad(LookIntoTrunkPitch));
				// (the level's shaders built before the picture comes up: on a slow machine the map holds on the trailhead)
				await Systems.ShaderWarmup.WaitReady(this, ct);
				fader.BlackAlpha = 0f;
				await map.Dissolve(MapDissolveSeconds, ct);
			}
			finally { if (IsInstanceValid(map)) map.QueueFree(); }
			await Cutscene.Wait(this, 1.0f, ct);
		}
		else
		{
			rig.SetPitch(Mathf.DegToRad(LookIntoTrunkPitch));
			await Systems.ShaderWarmup.WaitReady(this, ct);
			await fader.Fade(0f, 0.2f, ct);
		}
		if (!Alive(player)) return;

		// The camera comes up out of the trunk toward the eye, then it is in hand.
		var cam = GetNodeOrNull<Node3D>(TrunkCameraPath);
		if (cam != null && cam.Visible)
		{
			var eye = rig.Camera;
			Vector3 from = cam.GlobalPosition;
			Vector3 to = eye.GlobalPosition + (-eye.GlobalBasis.Z) * 0.45f + Vector3.Down * 0.22f;
			var lift = CreateTween();
			lift.TweenProperty(cam, "global_position", to, quick ? 0.1f : 1.1f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			lift.Parallel().TweenProperty(cam, "global_rotation", eye.GlobalRotation, quick ? 0.1f : 1.1f);
			await Cutscene.Tween(this, lift, ct);
			if (!Alive(player)) return;
			cam.Hide();
			cam.GlobalPosition = from;
		}
		var inv = player.Inventory;
		if (inv != null && !inv.HasCamera) inv.TryPickup(ToolKind.Camera);
		StoryManager.Instance?.SetFlag(StoryManager.Flag.PickupTakenCamera);

		// Head up and round toward the trail.
		float yaw = rig.Yaw;
		if (GetNodeOrNull<Node3D>(LookAfterPath) is { } look)
		{
			var d = look.GlobalPosition - player.GlobalPosition;
			yaw = Mathf.Atan2(-d.X, -d.Z);
		}
		else yaw += Mathf.Pi;
		float startYaw = rig.Yaw, startPitch = rig.Pitch;
		var turn = CreateTween();
		turn.TweenMethod(Callable.From<float>(p =>
		{
			rig.SnapBehind(Mathf.LerpAngle(startYaw, yaw, p));
			player.Visual.Rotation = new Vector3(0, rig.Yaw, 0);
			rig.SetPitch(Mathf.Lerp(startPitch, levelPitch, p));
		}), 0f, 1f, quick ? 0.1f : 2.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		await Cutscene.Tween(this, turn, ct);
		GD.Print("[story] opening: camera in hand at the car");
	}

	/// <summary>False once the level is being left (a scene change mid-opening).</summary>
	private bool Alive(PlayerController p) => IsInstanceValid(this) && IsInsideTree() && IsInstanceValid(p) && p.IsInsideTree();

	/// <summary>The how-to line, after control is back (so it never holds the player up).</summary>
	public Task ShowHowTo(CancellationToken ct) =>
		string.IsNullOrEmpty(HowToLine) || Subtitle.Instance == null ? Task.CompletedTask : Subtitle.Instance.Show(KeyHint.ForDevice(HowToLine), 1.0f, 5.5f, 1.4f, ct);
}
