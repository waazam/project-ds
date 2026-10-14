using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;

namespace ProjectDS.World;

/// <summary>
/// The first staircase's story (Act 1 into Act 2). Stepping onto the flight takes the camera
/// away, turns the stairs one-way (<see cref="OneWayFlight"/>), and lifts the player off their
/// feet: a straight, silent glide to the top landing with no say in it (movement is taken away,
/// but the mouse is still free to look around on the way up). Landing hands control back. At the
/// top stands the stone newel post with its cap broken off. Inspecting the stump (E) is the
/// trigger: the vignette closes like heavy eyelids, everything goes black, the checkpoint is
/// saved and the player comes to in the Hollow (GameFlow plays the wake-up there).
///
/// Only before Act 2 has happened, so it can never collide with Act 11's staircase.
/// </summary>
public partial class FirstClimbEvent : StoryTrigger
{
	[Export] public NodePath TopMarkerPath = "../TopTrigger";
	/// <summary>How far inside each cheek wall the flight box stays, so a body brushing the outside of
	/// a wall never counts as being on the stairs.</summary>
	[Export] public float FlightInset = 0.05f;
	/// <summary>Head room the flight box keeps above the top landing.</summary>
	[Export] public float FlightHeadroom = 1.4f;
	/// <summary>Seconds for the automatic glide from wherever the player stepped on to the top landing.</summary>
	[Export] public float FloatSeconds = 6.5f;

	[ExportGroup("The collapse")]
	/// <summary>Seconds for the vignette to close, blinking, into black after the post is inspected.</summary>
	[Export] public float CollapseSeconds = 4.5f;
	[Export] public float BlinkSeconds = 1.6f;
	[Export] public float BlinkVignette = 2.4f;
	/// <summary>Seconds of black before the level changes (the wake-up in the hollow is GameFlow's).</summary>
	[Export] public float BlackHoldSeconds = 2f;
	[Export] public string InspectPrompt = "Inspect the post";
	[Export] public string InspectLine = "";   // was "The cap's gone. Broken off." (self-talk removed, Dan 2026-09-22)

	/// <summary>For tests: the player has stepped onto the stairs (the camera is gone, the flight is one-way).</summary>
	public bool OnTheStairs { get; private set; }
	/// <summary>For tests: the automatic glide to the top landing is running (movement is taken away, look is not).</summary>
	public bool Floating { get; private set; }
	/// <summary>For tests: the collapse is running (from E on the post to the level change).</summary>
	public bool Collapsing { get; private set; }
	/// <summary>For tests: the E-point on the broken newel post at the top (built when the stairs are stepped on).</summary>
	public Interactable InspectPost { get; private set; }
	/// <summary>For tests: the one-way wall (null until the stairs are stepped on).</summary>
	public OneWayFlight OneWay { get; private set; }
	/// <summary>For tests: the box that covers the whole flight (null until built).</summary>
	public BoxShape3D FlightBox => GetNodeOrNull<CollisionShape3D>("FlightShape")?.Shape as BoxShape3D;

	protected override bool AlreadyHappened(StoryManager s) => s.Current >= Checkpoint.Act2StairsClimbed;
	protected override bool CanFire(StoryManager s, PlayerController p) => s.Current < Checkpoint.Act2StairsClimbed;

	[ExportGroup("The reveal")]
	/// <summary>The game's title over the stairs (2026-10-13, the owner: "I don't want to see the stairs text on the screen
	/// until the player is close to the stairs and can see them in view"): within this many metres of the flight
	/// (from the eye), and some point of it on screen within <see cref="RevealCone"/> degrees of the middle of the view,
	/// nothing solid between.</summary>
	[Export] public float RevealRange = 18f;
	[Export] public float RevealCone = 36f;

	/// <summary>For the tests and the title drop: the stairs have been seen, close, by the player (this level).</summary>
	public bool Revealed { get; private set; }
	/// <summary>For the tests: how far the player's eye was from the flight when it was revealed.</summary>
	public float RevealedAt { get; private set; }
	private static FirstClimbEvent _revealed;
	/// <summary>The stairs are in view, close (<see cref="ChapterCard.TitleThenStairs"/> waits for this).</summary>
	public static bool StairsRevealed => _revealed != null && IsInstanceValid(_revealed) && _revealed.Revealed;
	private double _poll;

	public override void _Ready()
	{
		base._Ready();
		// Deferred: a sibling (the Act 6 clearing event) may still be setting the flight's length.
		Callable.From(CoverWholeFlight).CallDeferred();
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		if (_revealed == this) _revealed = null;
	}

	public override void _Process(double delta)
	{
		if (Revealed) { SetProcess(false); return; }
		var s = StoryManager.Instance;
		if (s == null || s.Current >= Checkpoint.Act2StairsClimbed || s.HasFlag(ChapterCard.Flag(2))) return;
		_poll -= delta;
		if (_poll > 0) return;
		_poll = 0.1;
		if (GetParent() is not StaircaseBuilder stairs || StoryBeat.Player(this) is not { } player) return;
		var eye = player.CameraRig?.Camera?.GlobalPosition ?? player.GlobalPosition;
		// up the middle of the flight, and the post at the top
		var xf = stairs.GlobalTransform;
		for (int k = 0; k <= 6; k++)
		{
			int i = (stairs.Steps - 1) * k / 6;
			var p = xf * new Vector3(0f, (i + 1) * stairs.Rise + 0.35f, -i * stairs.Run - stairs.Run * 0.5f);
			if (!Act1TreeFalls.InSight(player, p, RevealRange, RevealCone, stairs)) continue;
			Reveal(eye.DistanceTo(p));
			return;
		}
		if (stairs.HasNewel && Act1TreeFalls.InSight(player, stairs.NewelSeatGlobal.Origin + Vector3.Up * 0.3f, RevealRange, RevealCone, stairs))
			Reveal(eye.DistanceTo(stairs.NewelSeatGlobal.Origin));
	}

	private void Reveal(float d)
	{
		Revealed = true;
		RevealedAt = d;
		_revealed = this;
		GD.Print($"[story] Act 1: the stairs, in view, {d:0.0} m off");
		ChapterCard.TitleThenStairs(this);
	}

	/// <summary>
	/// A second trigger box over the flight: from where the cheek walls begin (just past the plinth
	/// the scene's box already covers) to the back of the top landing, from a little under the lowest
	/// walled step to head height over the landing, and just inside the walls. Anyone on any step is
	/// inside it, so however they get onto the flight, the camera goes and the flight turns one-way.
	/// </summary>
	private void CoverWholeFlight()
	{
		if (GetParent() is not StaircaseBuilder stairs || GetNodeOrNull("FlightShape") != null) return;
		float zFront = -stairs.PlinthSteps * stairs.Run + 0.1f;
		float zBack = stairs.BackZ;
		float height = stairs.TotalHeight + FlightHeadroom;
		var box = new CollisionShape3D
		{
			Name = "FlightShape",
			Shape = new BoxShape3D { Size = new Vector3(Mathf.Max(0.3f, stairs.Width - FlightInset * 2f), height, zFront - zBack) },
			Position = new Vector3(0, height * 0.5f - 0.2f, (zFront + zBack) * 0.5f) - Position,
		};
		AddChild(box);
	}

	/// <summary>The first step: the flight is one-way (the camera stays with them now: the owner wants it
	/// kept all game), and the player is lifted
	/// off their feet for the glide to the top (<see cref="FloatUp"/>). The post at the top can be
	/// inspected only once they land there.</summary>
	protected override void Fire(PlayerController player)
	{
		OnTheStairs = true;
		if (GetParent() is StaircaseBuilder stairs)
		{
			OneWay = OneWayFlight.Attach(stairs);
			_ = Cutscene.Run(this, ct => FloatUp(player, stairs, ct), freezeBody: true);
		}
		GD.Print("[story] Act 1: on the stairs; there is no going back down");
	}

	/// <summary>
	/// Straight up to the top landing on its own, at a fixed pace, with no footsteps: the body's
	/// physics is frozen so no key moves it, but <see cref="PlayerInput"/> and the camera rig are
	/// left running, so the mouse (or stick) can still look around for the whole ride. Landing
	/// hands movement back and reveals the broken post's E-point, exactly where an ordinary climb
	/// would leave the player.
	/// </summary>
	private async Task FloatUp(PlayerController player, StaircaseBuilder stairs, CancellationToken ct)
	{
		Floating = true;
		var feet = player.GetNodeOrNull<PlayerFootsteps>("Footsteps");
		feet?.SetPhysicsProcess(false);
		player.Velocity = Vector3.Zero;
		try
		{
			var top = GetNodeOrNull<Node3D>(TopMarkerPath);
			Vector3 dest = top?.GlobalPosition ?? player.GlobalPosition;
			var tween = player.CreateTween();
			tween.TweenProperty(player, "global_position", dest, FloatSeconds)
				.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			await Cutscene.Tween(this, tween, ct);
		}
		finally
		{
			Floating = false;
			if (feet != null && IsInstanceValid(feet)) feet.SetPhysicsProcess(true);
		}
		GD.Print("[story] Act 2: the climb set them down at the top landing");
		if (GodotObject.IsInstanceValid(player) && player.IsInsideTree()) AttachInspectPost(stairs);
	}

	/// <summary>The E-point on the broken newel post, built once the glide actually sets the player down
	/// at the top.</summary>
	private void AttachInspectPost(StaircaseBuilder stairs)
	{
		if (!stairs.HasNewel || InspectPost != null) return;
		InspectPost = new Interactable
		{
			Name = "InspectPost",
			Prompt = InspectPrompt,
			PickRadius = 0.6f,
			MaxDistance = 2.6f,
			Position = stairs.NewelSeatLocal,
		};
		InspectPost.Interacted += OnInspected;
		stairs.AddChild(InspectPost);
	}

	private bool _inspected;

	/// <summary>E on the broken post: the collapse.</summary>
	private void OnInspected(PlayerController player)
	{
		if (_inspected || StoryManager.Instance is not { Current: < Checkpoint.Act2StairsClimbed }) return;
		_inspected = true;
		if (InspectPost != null) InspectPost.Enabled = false;
		var feet = player.GetNodeOrNull<PlayerFootsteps>("Footsteps");
		var postMat = StoryBeat.PostMaterial(this);
		float baseVignette = postMat != null ? (float)postMat.GetShaderParameter("vignette") : 0f;
		var fader = StoryBeat.Fader(this);
		_ = Cutscene.Run(this, async ct =>
		{
			Collapsing = true;
			feet?.SetPhysicsProcess(false);
			bool travelling = false;
			try
			{
				await Collapse(player, postMat, baseVignette, fader, ct);
				// Black, and gone: the checkpoint is saved here and the player comes to in the hollow
				// (GameFlow plays the wake-up there). The screen stays black through the level change.
				StoryBeat.ReachCheckpoint(player, Checkpoint.Act2StairsClimbed);
				travelling = true;
				StoryManager.Instance?.TravelToCheckpointLevel();
			}
			finally
			{
				Collapsing = false;
				if (!travelling)
				{
					postMat?.SetShaderParameter("vignette", baseVignette);
					if (fader != null && IsInstanceValid(fader)) fader.BlackAlpha = 0f;
				}
				if (feet != null && IsInstanceValid(feet)) feet.SetPhysicsProcess(true);
			}
		}, lockInput: true, freezeBody: true);
	}

	/// <summary>
	/// The hand on the stump, one thought, and then vision falls shut and drifts open again, over and
	/// over, each blink deeper than the last, until it is all black. The player passes out on the
	/// landing. Their eyes are locked on the post the whole way down.
	/// </summary>
	private async Task Collapse(PlayerController player, ShaderMaterial postMat, float baseVignette, ScreenFader fader, CancellationToken ct)
	{
		player.Velocity = Vector3.Zero;
		await Cutscene.Wait(this, 1.2, ct);   // the hand on the stump, no thought (self-talk removed, Dan 2026-09-22)
		double t = 0;
		while (t < CollapseSeconds)
		{
			t += GetProcessDeltaTime();
			float u = Mathf.Min(1f, (float)(t / CollapseSeconds));
			float phase = (float)(t / Mathf.Max(BlinkSeconds, 0.1f)) * Mathf.Tau;
			float closed = Mathf.Sin(phase - Mathf.Pi / 2f) * 0.5f + 0.5f;   // 0 = open .. 1 = closed
			// Each blink closes further than the last; the last ones never open.
			float floor = Mathf.SmoothStep(0.3f, 1f, u);
			float shut = Mathf.Max(closed, floor);
			postMat?.SetShaderParameter("vignette", Mathf.Lerp(baseVignette, BlinkVignette, shut));
			if (fader != null) fader.BlackAlpha = shut * Mathf.SmoothStep(0.15f, 0.9f, u);
			await Cutscene.Frame(this, ct);
		}
		if (fader != null) fader.BlackAlpha = 1f;
		// behind the black, put the vignette back: the post material is shared by every level, and a raised
		// value carried into the next one was read there as the resting look (the owner's "eye" view)
		postMat?.SetShaderParameter("vignette", baseVignette);
		await Cutscene.Wait(this, BlackHoldSeconds, ct);
		GD.Print("[story] Act 2: blacked out at the top of the stairs");
	}
}
