using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 6's clearing. Once the player has crossed the bridge with the newel
/// post in hand, the woods around the original staircase turn menacing, the
/// clearing fills with fog and fifteen small stair variations appear around
/// it: ten whole flights, all much the same size, and five broken fragments.
/// One whole flight stands right in the mouth of the trail as it enters. The
/// clearing has one way in (the trail); a ring of deadfall closes the rest, so
/// the only way on is up the stairs. Getting close enough plays the clearing's
/// voice line ("Come up and see"), the red spotlight finds the player and the
/// clearing answers (hum surge, chorus). Nothing is taken: the cap stays in the
/// player's hands until the last staircase (Act 11) and this clearing's own
/// staircase stays broken.
///
/// The loop (Dan, 2026-09-22): "stuck walking up different staircases before
/// eventually falling off", continuously up, never down. The voice calls out of
/// the trees from the moment they are inside the ring. Nothing is lit until the
/// player chooses: stepping onto ANY whole flight starts it. They climb to its
/// top; the fog closes in; without a cut they are part way up a DIFFERENT long
/// flight, facing up it, still walking, and climb again; every leg one-way
/// (<see cref="OneWayFlight"/>: no backing down), the voice calling at every
/// hand-off, blood raining harder with every flight. Four to six flights in all;
/// at the top of the last one the landing gives way, they fall off the back, black
/// out, and wake past the clearing (outside the ring) the same night in thick fog,
/// with nothing said. Act 7 begins.
///
/// Restore: the reveal is derived from the checkpoint + newel post flag, the
/// voice from <see cref="StoryManager.Flag.ClearingVoiceHeard"/>, the loop from
/// <see cref="StoryManager.Flag.ClearingLoopDone"/> (a loop quit halfway starts
/// over from the choice: its progress is never saved) and nightfall from
/// <see cref="StoryManager.Flag.Act6NightFell"/> (set by the fall). The overhead red
/// spotlight belongs to the voice beat only: it follows the player through the
/// clearing and is freed once they leave it (or the loop starts, or Act 7 begins).
///
/// The original flight's length is always <see cref="StairsState.ClearingStepsFor"/>
/// of the saved story, applied here before the staircase's own first build (this
/// node is its child, so it readies first), so a Continue builds the flight once.
///
/// The clearing's dressing (the fifteen stairs, the giant firs, the veiny
/// ground) is deterministic, so it is built at load and kept out of the tree
/// until the bridge checkpoint, when it is simply attached. The stairs are laid
/// out with oriented footprints (no two overlap, none on the trail but the trail
/// one, none over the original, none across the ring).
/// </summary>
public partial class Act6ClearingEvent : Node3D
{
	[Export] public NodePath OriginalStairsPath = "..";
	[Export] public NodePath DressingPath = "../Dressing";
	[Export] public float VoiceRadius = 15f;
	/// <summary>The ring that closes the clearing: just outside the outermost staircase.</summary>
	[Export] public float FenceRadius = 50f;
	/// <summary>Metres of the ring left open where the trail comes in (the one way in and back out).</summary>
	[Export] public float EntranceGap = 10f;
	/// <summary>How often the voice calls out of the trees during the loop (seconds between calls).</summary>
	[Export] public Vector2 LoopVoiceInterval = new(8f, 20f);
	[Export] public Vector2 LoopVoiceIntervalAutoTest = new(2f, 5f);
	/// <summary>How many flights the loop asks for (inclusive range; every leg goes up, the last one ends in the fall).</summary>
	[Export] public Vector2I LoopClimbRange = new(4, 6);
	/// <summary>How far past the clearing (along the trail) the player wakes after the fall: outside the ring.</summary>
	[Export] public float WakeTrailMetres = 72f;   // the trail bends: well past the ring by road as well as by air
	/// <summary>How far short of the clearing's centre, along the trail, the trail staircase stands (Dan,
	/// 2026-09-22: one whole flight right in the middle of the trail as it enters the clearing).</summary>
	[Export] public float TrailStairMetres = 24f;
	/// <summary>The spotlight lets go of the player once they are this far from the clearing.</summary>
	[Export] public float SpotlightReleaseRadius = 60f;
	/// <summary>The clearing's fog band (thickening above the ground) from the reveal on.</summary>
	[Export] public float ClearingFogDensity = 0.07f;
	/// <summary>The fog at the peak of a hand-off: a near white-out around the player, the only "cut" there is.</summary>
	[Export] public float HandOffFogDensity = 0.9f;
	/// <summary>The fog on waking after the fall ("super foggy"), thinning slowly over the walk to the lookout.</summary>
	[Export] public float WakeFogDensity = 0.16f;
	[Export] public float WakeFogClearSeconds = 150f;

	private const float StairsMaxReach = 44f;   // outer edge of the mini-stairs placement band
	private const int WholeSteps = 48, WholeStepsJitter = 0;   // long and all the same (Dan, 2026-09-22): a hand-off can land part way up
	private const float WholeScale = 1.35f, WholeWidth = 1.5f;

	private StaircaseBuilder _original;
	private DeepZoneDressing _dressing;
	private readonly List<Node3D> _minis = new();
	private Node3D _stand;    // the fifteen mini stairs, pre-built, attached on reveal
	private Node3D _lights;   // the clearing's fill lights, pre-built, attached on reveal
	private readonly List<StaircaseBuilder> _loopStairs = new();   // the whole (climbable) ones
	private StaircaseBuilder _trailStair;   // the whole flight standing in the trail's mouth
	private bool _revealed;
	private bool _voiceFired;
	private bool _loopArmed;      // the voice has spoken: any whole flight starts the loop
	private bool _loopStarted;    // a flight was chosen: the light and the compass follow the legs
	private bool _loopBusy;       // a blink or the fall is playing
	private bool _loopDone;
	private bool _calling;        // the voice calls out of the trees (from the clearing's edge until the fall)
	private int _loopClimbs;      // planned flights this run (4 to 6)
	private StaircaseBuilder _loopTarget;
	private SpotLight3D _loopLight;
	private Node3D _loopMarker;   // "clearing_loop_marker": where the compass points
	private StaticBody3D _fence;  // the ring that closes the clearing (from the reveal on)
	private Vector2 _entranceDir = Vector2.Down;   // clearing-local xz direction of the trail's mouth
	private OneWayFlight _oneWay;
	private BloodRain _blood;
	private readonly RandomNumberGenerator _rng = new();

	/// <summary>For tests: the ring stands (the clearing is closed but for the trail's mouth).</summary>
	public bool Fenced => _fence != null && IsInstanceValid(_fence);
	/// <summary>Old name for <see cref="Fenced"/>.</summary>
	public bool LoopFenced => Fenced;
	/// <summary>For tests: the centre of the gap in the ring where the trail comes in (world).</summary>
	public Vector3 EntranceWorld => GlobalPosition + new Vector3(_entranceDir.X, 0, _entranceDir.Y) * FenceRadius;
	/// <summary>For tests: how many times the voice has called out of the trees during the loop.</summary>
	public int LoopVoiceCount { get; private set; }
	/// <summary>For tests: the staircase the light is on (null until the player chooses one, and after the loop).</summary>
	public StaircaseBuilder LoopTarget => _loopTarget;
	/// <summary>For tests: the whole flights that carry the loop (never a ruined one, never the original).</summary>
	public IReadOnlyList<StaircaseBuilder> LoopStairs => _loopStairs;
	/// <summary>For tests: the whole flight standing in the trail where it enters the clearing.</summary>
	public StaircaseBuilder TrailStair => _trailStair;
	/// <summary>For tests: the voice has spoken and the clearing waits for the player to choose a flight.</summary>
	public bool LoopArmed => _loopArmed;
	/// <summary>For tests: a flight was chosen; the legs are running.</summary>
	public bool LoopStarted => _loopStarted;
	/// <summary>For tests: where the current leg ends (the flight's top landing), or the clearing's centre before the choice.</summary>
	public Vector3 LoopTargetPoint => _loopTarget == null ? GlobalPosition : TopOf(_loopTarget);
	/// <summary>For tests: legs completed so far this run.</summary>
	public int LoopStage { get; private set; }
	/// <summary>For tests: legs in all this run (every one goes up; the last ends in the fall).</summary>
	public int LoopLegs => _loopClimbs;
	/// <summary>For tests: the same as <see cref="LoopLegs"/>.</summary>
	public int LoopClimbs => _loopClimbs;
	/// <summary>For tests: the voice is calling out of the trees (from the clearing's edge on).</summary>
	public bool VoiceCalling => _calling;
	/// <summary>For tests: the loop has ended in the fall (or the saved story is past it).</summary>
	public bool LoopDone => _loopDone;
	/// <summary>For tests: a blink or the fall is in progress (the player is being moved).</summary>
	public bool LoopBusy => _loopBusy;
	/// <summary>For tests: the current leg's one-way wall.</summary>
	public OneWayFlight OneWay => _oneWay != null && IsInstanceValid(_oneWay) ? _oneWay : null;
	/// <summary>For tests: how far along its flight the current leg began (0 = its start, 0.5 = half way; the
	/// first leg always 0). Hand-offs land the player part way along sometimes (Dan, 2026-09-22).</summary>
	public float LoopLandFraction { get; private set; }
	/// <summary>For tests: blood is falling.</summary>
	public bool BloodRaining => _blood != null && IsInstanceValid(_blood) && _blood.Active;
	/// <summary>For tests: how hard it rains blood (0..1).</summary>
	public float BloodIntensity => BloodRaining ? _blood.Intensity : 0f;
	/// <summary>For tests: world position at the foot of a staircase, where an up-leg starts and a down-leg ends.</summary>
	public static Vector3 FootOf(StaircaseBuilder s) => s.ToGlobal(new Vector3(0, 0.05f, 1.4f));
	/// <summary>For tests: world position on a staircase's top landing.</summary>
	public static Vector3 TopOf(StaircaseBuilder s) => s.ToGlobal(new Vector3(0, s.TotalHeight, (s.TopFrontZ + s.BackZ) * 0.5f));
	/// <summary>
	/// The whole flight whose treads <paramref name="world"/> stands on, or null. This is how the loop knows
	/// a flight has been chosen (Dan, 2026-09-22: every large staircase must start it): not a trigger box
	/// round the flight, which a player brushing past its side also entered, but the player's own feet on
	/// the treads: inside the clear width, past the first riser, and at least a step above the ground.
	/// </summary>
	public StaircaseBuilder FlightUnder(Vector3 world)
	{
		// EVERY staircase in the clearing counts (Dan, 2026-09-22, "the millionth time"): the ten whole flights,
		// the five ruined stubs and the original in the middle. Geometry is the fallback under the physical test.
		foreach (var s in AllFlights)
		{
			if (s == null || !IsInstanceValid(s) || !s.IsInsideTree()) continue;
			Vector3 l = s.ToLocal(world);   // the flight's own units (scale included)
			if (Mathf.Abs(l.X) > s.Width * 0.5f + 0.2f) continue;
			if (l.Z > 0.45f || l.Z < s.BackZ - 0.5f) continue;
			if (l.Y < Mathf.Max(0.06f, 0.5f * s.Rise) || l.Y > s.TotalHeight + 1.6f) continue;
			return s;
		}
		return null;
	}

	/// <summary>Every staircase standing in the clearing: the minis (whole and ruined) and the original flight.</summary>
	public IEnumerable<StaircaseBuilder> AllFlights
	{
		get
		{
			foreach (var n in _minis) if (n is StaircaseBuilder m && IsInstanceValid(m)) yield return m;
			if (_original != null && IsInstanceValid(_original) && _original.Visible) yield return _original;
		}
	}

	/// <summary>
	/// The physical test: the flight whose collider the player is actually standing on (the floor under the
	/// character body, or a ray 1.6 m down from the feet), read off the StairBody's "stair_owner" tag. Null when
	/// the feet are on the ground or on anything else.
	/// </summary>
	public StaircaseBuilder StairUnderFeet(PlayerController player)
	{
		if (player == null || !IsInstanceValid(player)) return null;
		StaircaseBuilder Owner(GodotObject collider)
		{
			for (Node n = collider as Node; n != null; n = n.GetParent())
			{
				if (n is StaircaseBuilder sb) return sb;
				if (n.HasMeta("stair_owner") && n.GetNodeOrNull(n.GetMeta("stair_owner").AsNodePath()) is StaircaseBuilder tagged) return tagged;
			}
			return null;
		}
		StaircaseBuilder found = null;
		if (player.IsOnFloor())
			for (int i = 0; i < player.GetSlideCollisionCount() && found == null; i++)
				found = Owner(player.GetSlideCollision(i).GetCollider());
		if (found == null && player.GetWorld3D()?.DirectSpaceState is { } space)
		{
			Vector3 from = player.GlobalPosition + Vector3.Up * 0.3f;
			var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 1.9f, 1u, new Godot.Collections.Array<Rid> { player.GetRid() }));
			if (hit.Count > 0) found = Owner(hit["collider"].AsGodotObject());
		}
		if (found == null) return null;
		// Only a flight of this clearing, and only once the feet are half a step above its foot.
		bool ours = false;
		foreach (var f in AllFlights) if (f == found) { ours = true; break; }
		if (!ours) return null;
		Vector3 l = found.ToLocal(player.GlobalPosition);
		return l.Y >= 0.5f * found.Rise - 0.02f ? found : null;
	}

	/// <summary>For tests: whether standing two treads up <paramref name="s"/> would start the loop on it (any clearing flight).</summary>
	public bool WouldStartOn(StaircaseBuilder s)
	{
		if (s == null) return false;
		bool ours = false;
		foreach (var f in AllFlights) if (f == s) { ours = true; break; }
		if (!ours) return false;
		Vector3 twoUp = s.ToGlobal(new Vector3(0, 2f * s.Rise + 0.05f, -2f * s.Run));
		return FlightUnder(twoUp) == s;
	}

	/// <summary>Autotest only: puts the loop back to "armed, nothing chosen" so every flight can be tried in turn.</summary>
	public void ResetLoopForTest()
	{
		if (!GameSettings.Instance.AutoTest) return;
		_loopStarted = false;
		_loopBusy = false;
		LoopStage = 0;
		LoopLandFraction = 0f;
		DetachOneWay();
		_loopTarget = null;
		if (_loopLight != null && IsInstanceValid(_loopLight)) _loopLight.LightEnergy = 0f;
		if (_loopMarker != null && IsInstanceValid(_loopMarker)) _loopMarker.GlobalPosition = GlobalPosition + Vector3.Up;
	}
	/// <summary>Old name for <see cref="WouldStartOn"/>.</summary>
	public bool StepTriggerCovers(StaircaseBuilder s) => WouldStartOn(s);

	/// <summary>For tests: the world has been reset for Act 7 (the stairs and the ring gone; the meadow and the trails stay).</summary>
	public bool WorldReset { get; private set; }

	/// <summary>For tests: how many mini stairs currently stand in the world.</summary>
	public int MiniStairCount => _stand != null && _stand.IsInsideTree() ? _minis.Count : 0;
	/// <summary>For tests: whether the voice beat's spotlight is still following the player.</summary>
	public bool SpotlightActive => _playerSpot != null;
	/// <summary>For tests: the original flight this clearing surrounds.</summary>
	public StaircaseBuilder OriginalStairs => _original;
	/// <summary>For tests: whether the pre-built dressing is standing in the world.</summary>
	public bool Revealed => _revealed;
	/// <summary>For tests: the clearing's voice has spoken (or the saved story is past it).</summary>
	public bool VoiceSpoken => _voiceFired;

	/// <summary>Dev previews only (debug builds): shows the clearing now, without the story reaching it.</summary>
	public void DebugReveal()
	{
		if (!OS.IsDebugBuild() || _revealed) return;
		Reveal(restoring: false);
	}

	private PlayerController _player;
	private SpotLight3D _playerSpot;
	private Area3D _voiceZone;

	public override void _Ready()
	{
		_original = GetNodeOrNull<StaircaseBuilder>(OriginalStairsPath);
		_dressing = GetNodeOrNull<DeepZoneDressing>(DressingPath);
		SetProcess(false);   // only while the spotlight rides
		// Before the parent flight builds itself (children ready first): its first build is already the right
		// length. The clearing's staircase stays broken: the cap goes back on the LAST staircase (Act 11).
		ApplyStairsLength();
		if (_original != null) _original.NewelCapped = false;
		Callable.From(() => { PrepareDressing(); Restore(); }).CallDeferred();
		if (StoryManager.Instance is { } s)
		{
			s.CheckpointReached += OnCheckpoint;
			s.FlagSet += OnFlag;
		}
	}

	public override void _ExitTree()
	{
		if (StoryManager.Instance is { } s)
		{
			s.CheckpointReached -= OnCheckpoint;
			s.FlagSet -= OnFlag;
		}
		// Dressing that was never attached would otherwise outlive the level.
		FreeIfDetached(ref _stand);
		FreeIfDetached(ref _lights);
	}

	private static void FreeIfDetached(ref Node3D n)
	{
		if (n != null && IsInstanceValid(n) && n.GetParent() == null) n.Free();
		n = null;
	}

	/// <summary>Sets the original flight's length to what the saved story says (StairsState), rebuilding
	/// only if it already stands at another length.</summary>
	private void ApplyStairsLength()
	{
		if (_original == null) return;
		int want = StairsState.ClearingStepsFor(StoryManager.Instance, _original.BaseSteps);
		if (_original.Steps == want) return;
		_original.Steps = want;
		if (_original.IsNodeReady()) _original.Build();
	}

	private void OnCheckpoint(Checkpoint cp)
	{
		TryReveal();
		// "Come up and see" starts calling as soon as the creek is crossed (Dan, 2026-09-22), long before the ring.
		if (cp == Checkpoint.Act6BridgeCrossed && !_loopDone) StartCalling();
		if (cp >= Checkpoint.Act7CabinBurning) { ReleaseSpotlight(1.5f); TearDownLoop(); }
	}

	/// <summary>The stalker's feet: while the clearing stands, whichever whole flight they climb is the chosen one.</summary>
	private void WatchForChoice()
	{
		if (!_revealed || _loopDone || _loopBusy || _player == null || !IsInstanceValid(_player)) return;
		// Physical first (the collider under the feet), geometry as the fallback (inside a footprint, above its plinth).
		var on = StairUnderFeet(_player) ?? FlightUnder(_player.GlobalPosition);
		if (on == null) return;
		if (!_loopStarted) { OnLoopStepped(on, _player); return; }
		// Stepped off the chosen flight's first treads onto another whole one before the way back closed: that one is the choice.
		if (LoopStage == 0 && on != _loopTarget && _loopStairs.Contains(on) && (_oneWay == null || !IsInstanceValid(_oneWay) || !_oneWay.Armed))
		{
			SetLoopTarget(on, 0.6f);
			AttachOneWay(on);
			GD.Print($"[story] Act 6: the loop moves to {on.Name}");
		}
	}

	private void OnFlag(string _) => TryReveal();

	private void TryReveal()
	{
		if (_revealed || !StoryBeat.Act6Revealed(StoryManager.Instance)) return;
		Reveal(restoring: false);
	}

	/// <summary>Continue: put the clearing back the way the story left it.</summary>
	private void Restore()
	{
		var s = StoryManager.Instance;
		if (!StoryBeat.Act6Revealed(s)) return;
		_voiceFired = s.ClearingVoiceHeard;
		_loopDone = s.HasFlag(StoryManager.Flag.ClearingLoopDone);
		if (_loopDone)
		{
			// Act 7 on: the stairs and the ring are gone (the meadow and the trails stay; the compass leads on).
			ResetWorld(restoring: true);
			return;
		}
		Reveal(restoring: true);
		ApplyStairsLength();   // normally a no-op: the flight was built at the right length
		if (_voiceFired && !_loopDone && !s.HasFlag(StoryManager.Flag.Act6NightFell) && s.Current < Checkpoint.Act7CabinBurning)
			ArmLoop();           // a loop quit halfway starts over from the choice
		// The voice has been calling since the creek: a Continue between the bridge and the fall picks it up again.
		if (!_loopDone && s.Current >= Checkpoint.Act6BridgeCrossed && s.Current < Checkpoint.Act7CabinBurning) StartCalling();
	}

	/// <summary>Builds everything the reveal will need, then keeps it out of the tree (no rendering,
	/// no physics) until the story gets there. Deterministic seeds, so nothing depends on when it runs.</summary>
	private void PrepareDressing()
	{
		if (_stand != null) return;
		_stand = new Node3D { Name = "MiniStairs" };
		AddChild(_stand);
		BuildMiniStairs(_stand);
		RemoveChild(_stand);
		_lights = new Node3D { Name = "ClearingLights" };
		AddChild(_lights);
		BuildClearingLights(_lights);
		RemoveChild(_lights);
		// Giants stay well outside the mini-stairs band (StairsMaxReach + one stair's own footprint)
		// so their thick trunks never clip through a staircase.
		_dressing?.Prepare(GlobalPosition, FenceRadius + 20f);
	}

	private void Reveal(bool restoring)
	{
		_revealed = true;
		_player ??= StoryBeat.Player(this);
		PrepareDressing();
		// Continue applies the saved mood itself (GameFlow); only a live reveal fades to it.
		if (!restoring) StoryBeat.SetMood(this, ForestAtmosphere.Mood.Menacing, 14f);
		_dressing?.Reveal(GlobalPosition, FenceRadius + 20f);
		if (_stand.GetParent() == null) AddChild(_stand);
		if (_lights.GetParent() == null) AddChild(_lights);
		BuildFence();
		// Thick with fog from here on (Dan, 2026-09-22): the far stairs are grey shapes in it.
		if (!_loopDone) StoryBeat.Atmosphere(this)?.SetHeightFog(GlobalPosition.Y + 1.0f, ClearingFogDensity, restoring ? 0.1f : 12f);
		// "Come up and see" starts the moment the stairs load in (Dan, 2026-09-22): the reveal itself calls, no
		// zone in between (the reveal follows the bridge checkpoint, so this is the creek crossing).
		if (!_loopDone) StartCalling();
		// From here the player's feet choose the flight: watched every frame (no trigger boxes).
		SetProcess(true);
		if (!_voiceFired)
		{
			_voiceZone = StoryBeat.MakeTrigger(this, new CylinderShape3D { Radius = VoiceRadius, Height = 80f }, Vector3.Zero, OnVoiceZoneEntered, "VoiceZone");
			_voiceZone.TopLevel = true;
			_voiceZone.GlobalPosition = GlobalPosition;
		}
		if (!restoring) GD.Print("[story] Act 6: the woods around the clearing turn");
	}

	private void OnVoiceZoneEntered(PlayerController player)
	{
		if (_voiceFired) return;
		_voiceFired = true;
		_player = player;
		_voiceZone?.QueueFree();
		_voiceZone = null;
		_ = Cutscene.Run(this, VoiceBeat);
	}

	/// <summary>A ring of cold, even fill light so the whole stand of fifteen stairs actually reads
	/// clearly, rather than being lost in the Menacing mood's low ambient.</summary>
	private static void BuildClearingLights(Node3D parent)
	{
		const int count = 6;
		for (int i = 0; i < count; i++)
		{
			float ang = Mathf.Tau / count * i;
			parent.AddChild(new OmniLight3D
			{
				Name = $"ClearingFill{i}",
				LightColor = new Color(0.75f, 0.78f, 0.85f),
				LightEnergy = 3.2f,
				OmniRange = StairsMaxReach + 8f,
				Position = new Vector3(Mathf.Cos(ang) * StairsMaxReach * 0.5f, 11f, Mathf.Sin(ang) * StairsMaxReach * 0.5f),
			});
		}
	}

	// ------------------------------------------------------------------ the stand of stairs

	/// <summary>An oriented footprint on the ground plane: centre and half extents in metres, yaw in radians.</summary>
	public readonly record struct Footprint(Vector2 Centre, Vector2 Half, float Yaw)
	{
		public float HalfDiagonal => Half.Length();
		/// <summary>Separating-axis test of two boxes, each grown by <paramref name="margin"/> on every side.</summary>
		public static bool Overlap(Footprint a, Footprint b, float margin = 0f)
		{
			Vector2[] axes = { Dir(a.Yaw), Dir(a.Yaw + Mathf.Pi * 0.5f), Dir(b.Yaw), Dir(b.Yaw + Mathf.Pi * 0.5f) };
			foreach (var axis in axes)
			{
				float ca = a.Centre.Dot(axis), cb = b.Centre.Dot(axis);
				float ra = Radius(a, axis) + margin, rb = Radius(b, axis) + margin;
				if (Mathf.Abs(ca - cb) > ra + rb) return false;
			}
			return true;
		}
		private static Vector2 Dir(float yaw) => new(Mathf.Sin(yaw), Mathf.Cos(yaw));   // a flight's local +Z on the ground (x, z)
		private static float Radius(Footprint f, Vector2 axis)
		{
			Vector2 ax = new(Mathf.Cos(f.Yaw), -Mathf.Sin(f.Yaw)), az = Dir(f.Yaw);   // local +X and +Z on the ground
			return Mathf.Abs(ax.Dot(axis)) * f.Half.X + Mathf.Abs(az.Dot(axis)) * f.Half.Y;
		}
	}

	/// <summary>Local (unscaled) footprint of a flight with these settings: [zFront, zBack] along its axis and half width.</summary>
	private static (float zFront, float zBack, float halfX) LocalExtents(int steps, float run, float landing, bool ruined, float width, float wallT, float flare, bool dropZone)
	{
		float topFrontZ = -(steps - 1) * run;
		float backZ = topFrontZ - (ruined ? run : landing);
		float zFront = run + 0.35f;
		float zBack = backZ - (dropZone && !ruined ? 2.5f : 0.3f);   // whole flights keep their landing's drop zone clear behind them
		float halfX = width * 0.5f + wallT + flare + 0.25f;
		return (zFront, zBack, halfX);
	}

	/// <summary>World-ground footprint of a standing flight (for tests: the pairwise overlap check).</summary>
	public static Footprint FootprintOf(StaircaseBuilder s, bool dropZone = false)
	{
		var (zf, zb, hx) = LocalExtents(s.Steps, s.Run, s.LandingDepth, s.Ruined, s.Width, s.WallThickness, s.PlinthFlare, dropZone);
		float yaw = s.GlobalRotation.Y, scale = s.Scale.X;
		Vector2 az = new(Mathf.Sin(yaw), Mathf.Cos(yaw));
		Vector2 centre = new Vector2(s.GlobalPosition.X, s.GlobalPosition.Z) + az * ((zf + zb) * 0.5f * scale);
		return new Footprint(centre, new Vector2(hx, (zf - zb) * 0.5f) * scale, yaw);
	}

	/// <summary>
	/// Lays out the fifteen: the trail flight first (in the trail's mouth, facing the way the player walks
	/// in), then the rest on rings 13-28 m out at random yaws, each candidate rejected if its footprint
	/// (landing drop zone included) overlaps a placed one, the original flight (3 m margin), the trail
	/// corridor or the ring. Ten whole flights are all much the same size; the five fragments vary.
	/// </summary>
	private void BuildMiniStairs(Node3D parent)
	{
		var rng = new RandomNumberGenerator { Seed = 9001 };
		var terrain = GroundSnap.FindTerrain(this);
		const int count = 15;
		var placed = new List<Footprint>();   // clearing-local xz
		Vector2 origin = new(GlobalPosition.X, GlobalPosition.Z);
		if (_original != null)
		{
			var o = FootprintOf(_original, dropZone: true);
			placed.Add(new Footprint(o.Centre - origin, o.Half + Vector2.One * 3f, o.Yaw));
		}
		float sc = 0f;
		if (terrain != null)
		{
			terrain.TrailDistance(GlobalPosition.X, GlobalPosition.Z, out sc);
			Vector3 mouth = terrain.TrailPoint(sc - FenceRadius, out _);
			_entranceDir = new Vector2(mouth.X - GlobalPosition.X, mouth.Z - GlobalPosition.Z).Normalized();
		}
		bool OnPath(Vector2 localXz, float reach)
		{
			if (terrain == null) return false;
			Vector3 w = GlobalPosition + new Vector3(localXz.X, 0, localXz.Y);
			return terrain.TrailDistance(w.X, w.Z, out _) < reach + 2.2f;
		}
		float[] rings = { 17f, 25f, 33f, 41f };
		var layout = new System.Text.StringBuilder("[story] Act 6 layout (name, xz, yaw deg, footprint w x l):");

		for (int i = 0; i < count; i++)
		{
			// Every third one is a broken fragment; the rest are whole and climbable (the loop's stairs), and
			// all the whole ones are much the same size, so whichever the player picks the loop reads the same.
			bool ruined = i % 3 == 2;
			float scale = ruined ? rng.RandfRange(1.15f, 1.7f) : WholeScale;
			int steps = ruined ? rng.RandiRange(6, 10) : WholeSteps + rng.RandiRange(-WholeStepsJitter, WholeStepsJitter);   // the fragments are low broken stubs
			// The whole ones keep the first staircase's real step (0.17 x 0.28 m in the world) whatever their
			// scale, so the player can actually walk up them; they loom by being longer and wider.
			float rise = ruined ? rng.RandfRange(0.16f, 0.2f) : 0.17f / scale;
			float run = ruined ? rng.RandfRange(0.26f, 0.32f) : 0.28f / scale;
			float width = ruined ? rng.RandfRange(1.1f, 1.8f) : WholeWidth;
			const float wallT = 0.22f, flare = 0.32f, landing = 1.25f;
			var (zf, zb, hx) = LocalExtents(steps, run, landing, ruined, width, wallT, flare, dropZone: true);
			Vector2 half = new Vector2(hx, (zf - zb) * 0.5f) * scale;
			float centreZ = (zf + zb) * 0.5f * scale;   // the box centre lies this far along local +Z from the origin (negative: toward the top)
			Footprint At(Vector2 xz, float yaw) => new(xz + new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * centreZ, half, yaw);
			bool Clear(Footprint f, bool trail)
			{
				foreach (var p in placed) if (Footprint.Overlap(f, p, 1.0f)) return false;
				if (!trail && OnPath(f.Centre, f.HalfDiagonal)) return false;
				return f.Centre.Length() + f.HalfDiagonal < FenceRadius - 1.5f;
			}
			// The ground under a candidate: the flight stands on the HIGHEST ground under its footprint (no tread
			// ever below ground; the foundation reaches down to the lowest), but not where the ground in front of
			// its first step would be a lip of more than the collider apron can ramp (0.5 m), nor on a slope the
			// foundation cannot cover (2.5 m across the footprint).
			(float hi, float lo, float foot) GroundUnder(Vector2 o, float yawAt)
			{
				Vector3 az = new(Mathf.Sin(yawAt), 0, Mathf.Cos(yawAt)), ax = new(Mathf.Cos(yawAt), 0, -Mathf.Sin(yawAt));
				Vector3 w0 = GlobalPosition + new Vector3(o.X, 0, o.Y);
				float hi = float.MinValue, lo = float.MaxValue;
				for (int j = 0; j <= 10; j++)
					for (int k = -2; k <= 2; k++)
					{
						Vector3 w = w0 + az * Mathf.Lerp(zf, zb + (ruined ? 0f : 2.5f), j / 10f) * scale + ax * (k * 0.5f * hx * scale);
						float h = terrain.HeightAt(w.X, w.Z);
						hi = Mathf.Max(hi, h); lo = Mathf.Min(lo, h);
					}
				Vector3 wf = w0 + az * (1.4f * scale);
				return (hi, lo, terrain.HeightAt(wf.X, wf.Z));
			}
			bool GroundOk(Vector2 o, float yawAt)
			{
				if (terrain == null) return true;
				var (hi, lo, foot) = GroundUnder(o, yawAt);
				return hi - lo <= 2.5f && hi - foot <= 0.5f;
			}

			Vector2 xz = Vector2.Zero;
			float yaw = 0f;
			bool found = false;
			if (i == 0 && terrain != null)
			{
				// The trail flight: in the trail's mouth, its foot toward the player walking in and its treads
				// running on into the clearing, pushed out a little if the original stands in its way.
				for (float back = TrailStairMetres; back <= FenceRadius - 8f && !found; back += 2f)
				{
					Vector3 mouth = terrain.TrailPoint(sc - back, out var tan);
					xz = new Vector2(mouth.X - GlobalPosition.X, mouth.Z - GlobalPosition.Z);
					yaw = Mathf.Atan2(-tan.X, -tan.Z);   // the flight rises along the trail's direction of travel
					found = Clear(At(xz, yaw), trail: true) && GroundOk(xz, yaw);
				}
			}
			else
			{
				Footprint last = default;
				foreach (float ring in rings)
				{
					for (int attempt = 0; attempt < 60 && !found; attempt++)
					{
						float ang = rng.RandfRange(0f, Mathf.Tau);
						float dist = ring + rng.RandfRange(-2.5f, 2.5f);
						xz = new Vector2(Mathf.Cos(ang) * dist, Mathf.Sin(ang) * dist);
						yaw = rng.RandfRange(0f, Mathf.Tau);
						last = At(xz, yaw);
						found = Clear(last, trail: false) && GroundOk(xz, yaw);
					}
					if (found) break;
				}
				if (!found) GD.PushWarning($"[story] Act 6 layout: no clear spot for MiniStair{i}; it stands where it landed");
			}
			placed.Add(At(xz, yaw));

			// Ground: set on the highest ground under its footprint, so no tread is ever below the ground; the
			// walls and plinth reach down to the lowest (FootingFor) so nothing floats, and the collider apron
			// ramps the small lip that may be left in front of the first step.
			Vector3 local = new(xz.X, 0, xz.Y);
			Vector3 world = GlobalPosition + local;
			float groundY = terrain?.HeightAt(world.X, world.Z) ?? world.Y;
			if (terrain != null) groundY = Mathf.Max(groundY, GroundUnder(xz, yaw).hi);
			var stair = new StaircaseBuilder
			{
				Name = $"MiniStair{i}",
				Steps = steps,
				Rise = rise,
				Run = run,
				Width = width,
				PlinthSteps = 1,
				WallHeight = ruined ? rng.RandfRange(0.35f, 0.55f) : 0.45f,
				Ruined = ruined,
				CrumbleSide = rng.Randf() < 0.5f ? -1 : 1,
				CrumbleAfterStep = rng.RandiRange(1, 3),
				Seed = 100 + i,
				Scale = Vector3.One * scale,
				Position = new Vector3(local.X, groundY - GlobalPosition.Y, local.Z),
				Rotation = new Vector3(0, yaw, 0),
			};
			if (i == 0 && terrain != null) _trailStair = stair;
			// Set on the ground at its foot; on a slope the walls and plinth reach down to the lowest ground
			// under the whole flight, so no edge of it stands in the air.
			stair.FoundationDepth = FootingFor(stair, new Vector3(world.X, groundY, world.Z), terrain);
			parent.AddChild(stair);
			stair.AddToGroup("act6_mini_stairs");
			_minis.Add(stair);
			layout.Append($"\n  {stair.Name,-11} ({xz.X,6:0.0},{xz.Y,6:0.0}) yaw {Mathf.RadToDeg(yaw),4:0} {(ruined ? "ruined" : "whole ")} {half.X * 2f,4:0.0} x {half.Y * 2f,4:0.0} m{(i == 0 && _trailStair == stair ? " (trail)" : "")}");

			if (ruined) continue;
			_loopStairs.Add(stair);
			// The loop's hooks (in the flight's own units: the Area scales with it). Stepping onto the flight is
			// watched by the player's feet on the treads (FlightUnder, every frame): the old trigger box round
			// the flight also fired on a player squeezing PAST it (the trail flight stands in the way in), which
			// silently started the loop there and then no other flight would (Dan's live bug, 2026-09-22).
			// The top landing ends a leg.
			float landingDepth = Mathf.Max(0.3f, stair.LandingDepth * 0.9f);
			StoryBeat.MakeTrigger(stair,
				new BoxShape3D { Size = new Vector3(stair.Width, 1.8f, landingDepth) },
				new Vector3(0, stair.TotalHeight + 0.9f, (stair.TopFrontZ + stair.BackZ) * 0.5f), p => OnLoopTop(stair, p), "LoopTop");
		}
		GD.Print(layout.ToString());
	}

	private static float FootingFor(StaircaseBuilder s, Vector3 origin, ForestTerrain terrain)
	{
		if (terrain == null) return s.FoundationDepth;
		float half = s.Width * 0.5f + s.WallThickness + s.PlinthFlare + 0.15f;
		float z0 = 0.3f, z1 = s.BackZ - 0.5f;
		var basis = Basis.FromEuler(s.Rotation).Scaled(s.Scale);
		float lo = origin.Y;
		for (int j = 0; j <= 12; j++)
			for (int i = 0; i <= 6; i++)
			{
				Vector3 w = origin + basis * new Vector3(Mathf.Lerp(-half, half, i / 6f), 0, Mathf.Lerp(z0, z1, j / 12f));
				lo = Mathf.Min(lo, terrain.HeightAt(w.X, w.Z));
			}
		return Mathf.Max(s.FoundationDepth, (origin.Y - lo) / s.Scale.Y + 0.25f);
	}

	// ------------------------------------------------------------------ the ring

	/// <summary>
	/// Closes the clearing from the reveal on: an invisible ring of wall segments, tall enough that nothing
	/// gets over it, with a heap of deadfall along it so it reads, and one gap where the trail comes in.
	/// The way back out is that gap; the trail onward can only be reached by the stairs (the fall's wake
	/// spot is outside the ring).
	/// </summary>
	private void BuildFence()
	{
		if (Fenced) return;
		var terrain = GroundSnap.FindTerrain(this);
		_fence = new StaticBody3D { Name = "ClearingFence", CollisionLayer = 1, CollisionMask = 0, TopLevel = true };
		AddChild(_fence);
		_fence.GlobalPosition = GlobalPosition;
		const int segs = 36;
		float segLen = Mathf.Tau * FenceRadius / segs;
		float entranceAng = Mathf.Atan2(_entranceDir.Y, _entranceDir.X);
		var logs = new MeshKit();
		logs.Mat(ProcTextures.BarkMat);
		var rng = new RandomNumberGenerator { Seed = 6006 };
		for (int i = 0; i < segs; i++)
		{
			float ang = Mathf.Tau * (i + 0.5f) / segs;
			float away = Mathf.Abs(Mathf.Wrap(ang - entranceAng, -Mathf.Pi, Mathf.Pi)) * FenceRadius;
			if (away < EntranceGap * 0.5f + segLen * 0.5f) continue;   // the trail's mouth stays open
			_fence.AddChild(new CollisionShape3D
			{
				Shape = new BoxShape3D { Size = new Vector3(segLen + 0.5f, 30f, 0.3f) },
				Position = new Vector3(Mathf.Cos(ang) * FenceRadius, 8f, Mathf.Sin(ang) * FenceRadius),
				Rotation = new Vector3(0, -ang - Mathf.Pi * 0.5f, 0),   // tangent to the ring
			});
			// Deadfall: two trunks along the segment, one on the ground and one thrown across it.
			Vector3 a = new(Mathf.Cos(ang - segLen * 0.5f / FenceRadius) * FenceRadius, 0, Mathf.Sin(ang - segLen * 0.5f / FenceRadius) * FenceRadius);
			Vector3 b = new(Mathf.Cos(ang + segLen * 0.5f / FenceRadius) * FenceRadius, 0, Mathf.Sin(ang + segLen * 0.5f / FenceRadius) * FenceRadius);
			float ya = terrain?.HeightAt(GlobalPosition.X + a.X, GlobalPosition.Z + a.Z) - GlobalPosition.Y ?? 0f;
			float yb = terrain?.HeightAt(GlobalPosition.X + b.X, GlobalPosition.Z + b.Z) - GlobalPosition.Y ?? 0f;
			logs.Color = new Color(0.55f + rng.Randf() * 0.2f, 0.5f + rng.Randf() * 0.15f, 0.42f);
			logs.Cylinder(a + new Vector3(0, ya + 0.32f, 0), b + new Vector3(0, yb + 0.32f, 0), 0.34f, 0.28f, 8);
			Vector3 mid = (a + b) * 0.5f;
			Vector3 across = (b - a).Normalized() * rng.RandfRange(-0.8f, 0.8f) + new Vector3(-Mathf.Sin(ang), 0, Mathf.Cos(ang)) * 0f;
			logs.Color = new Color(0.5f, 0.45f, 0.38f);
			logs.Cylinder(a + across + new Vector3(0, ya + 0.85f, 0), b - across + new Vector3(0, yb + 0.85f, 0), 0.26f, 0.2f, 7);
		}
		logs.CommitTo(_fence, "Deadfall");
		GD.Print("[story] Act 6: the clearing is closed but for the trail");
	}

	// ------------------------------------------------------------------ the loop

	private bool LoopActive(StoryManager s) => _voiceFired && !_loopDone && s != null && s.Current < Checkpoint.Act7CabinBurning;

	/// <summary>The voice has spoken: nothing is lit yet; the compass rests on the clearing and any whole flight starts the loop.</summary>
	private void ArmLoop()
	{
		if (_loopArmed || _loopStairs.Count == 0) return;
		_loopArmed = true;
		_loopStarted = false;
		_loopClimbs = _rng.RandiRange(LoopClimbRange.X, Mathf.Max(LoopClimbRange.X, LoopClimbRange.Y));
		LoopStage = 0;
		_loopMarker = new Node3D { Name = "ClearingLoopMarker", TopLevel = true };
		AddChild(_loopMarker);
		_loopMarker.AddToGroup("clearing_loop_marker");
		_loopMarker.GlobalPosition = GlobalPosition + Vector3.Up;
		_loopLight = new SpotLight3D
		{
			Name = "LoopLight", LightColor = new Color(0.86f, 0.9f, 1f), LightEnergy = 0f,
			SpotRange = 46f, SpotAngle = 22f, SpotAngleAttenuation = 1.4f, ShadowEnabled = true, TopLevel = true,
		};
		AddChild(_loopLight);
		StartCalling();
		GD.Print($"[story] Act 6: the clearing waits for a staircase to be chosen ({_loopClimbs} flights)");
	}

	/// <summary>The voice starts calling out of the trees (once; it runs until the fall). The first call comes at once.</summary>
	private void StartCalling()
	{
		if (_calling || _loopDone) return;
		_calling = true;
		StartBlood();   // the blood rain starts with the calling, as soon as the stairs load in (Dan, 2026-09-22)
		_ = Cutscene.Run(this, LoopVoices);
		GD.Print("[story] Act 6: the voice starts calling out of the trees");
	}

	/// <summary>Moves the light and the compass marker onto a staircase (the end of the current leg).</summary>
	private void SetLoopTarget(StaircaseBuilder stair, float fadeSeconds)
	{
		_loopTarget = stair;
		if (stair == null) return;
		Vector3 mid = stair.ToGlobal(new Vector3(0, stair.TotalHeight * 0.5f, stair.TopFrontZ * 0.5f));
		if (_loopMarker != null) _loopMarker.GlobalPosition = LoopTargetPoint;
		if (_loopLight != null)
		{
			_loopLight.GlobalPosition = mid + Vector3.Up * 16f;
			_loopLight.LookAt(mid, Vector3.Forward);
			_loopLight.LightEnergy = 0f;
			_loopLight.CreateTween().TweenProperty(_loopLight, "light_energy", 9f, fadeSeconds).SetTrans(Tween.TransitionType.Sine);
		}
	}

	/// <summary>Stepping onto any whole flight is the choice: the loop starts on it, going up.</summary>
	private void OnLoopStepped(StaircaseBuilder stair, PlayerController player)
	{
		// ANY whole flight starts it (Dan, 2026-09-22), whichever way it is approached and whether or not the
		// voice beat / the ring trigger got there first: stepping on is the choice, so arm on the spot.
		if (_loopBusy || _loopStarted || _loopDone) return;
		if (StoryManager.Instance is not { } sm || sm.Current >= Checkpoint.Act7CabinBurning) return;
		if (!_voiceFired) { _voiceFired = true; sm.MarkClearingVoiceHeard(); PlayVoice(); }   // stepped on before the beat: it still speaks
		if (!_loopArmed) ArmLoop();
		if (!_loopArmed) return;
		_loopStarted = true;
		_player = player;
		LoopLandFraction = 0f;
		ReleaseSpotlight(1.5f);
		SetLoopTarget(stair, 0.6f);
		StartBlood();
		StartDaze();
		if (_loopStairs.Contains(stair))
		{
			AttachOneWay(stair);
			GD.Print($"[story] Act 6: the loop starts on {stair.Name}");
			return;
		}
		// A ruined stub or the original flight: it still starts the loop; the fog hands them straight on to a
		// whole flight (no one-way wall on a stub: the first hand-off is the way the loop takes them).
		GD.Print($"[story] Act 6: the loop starts on {stair.Name} (a stub: the fog takes them on at once)");
		_loopBusy = true;
		_ = Cutscene.Run(this, ct => Blink(stair, ct));
	}

	private void OnLoopTop(StaircaseBuilder stair, PlayerController player)
	{
		if (!LoopActive(StoryManager.Instance) || !_loopStarted || _loopBusy || stair != _loopTarget) return;
		_loopBusy = true;
		_player = player;
		LoopStage++;
		GD.Print($"[story] Act 6: the top of {stair.Name} reached (flight {LoopStage} of {_loopClimbs})");
		if (LoopStage >= _loopClimbs) _ = Cutscene.Run(this, ct => Fall(stair, ct), lockInput: true, freezeBody: true);
		else _ = Cutscene.Run(this, ct => Blink(stair, ct));   // control is never taken: the hand-off is fog, not a cut
	}

	/// <summary>
	/// The top of one flight is part way up the next, and it must feel fluid and trippy, never a beat
	/// skipped and never a cut (Dan, 2026-09-22): the player keeps walking the whole way through and
	/// nothing on the screen flashes or fades. Over 0.35 s the fog closes in around them to a near
	/// white-out (the height fog band, thickening above the ground) while the view drifts (the field
	/// of view breathes out, the hum rises); at the peak they are stood on the next flight, facing up
	/// it, still walking; over the next 0.6 s the fog thins and the view settles, revealing the new
	/// flight, and the voice calls again. under a second in all (Dan: no pause between), no lock, footsteps and hum continuous.
	/// </summary>
	private async Task Blink(StaircaseBuilder from, CancellationToken ct)
	{
		try
		{
			var hum = Audio.StairsHum.Instance;
			var atmo = StoryBeat.Atmosphere(this);
			var rig = _player?.CameraRig;
			hum?.SetOverrideDb(hum.CloseDb);
			var swim = CreateTween().SetParallel();
			// The breathe-out rides on the daze's own slow swim (the daze loop sums them onto the rig).
			if (rig != null) swim.TweenMethod(Callable.From<float>(v => _blinkSwim = v), 0f, 7f, 0.35f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
			atmo?.SetHeightFog(GlobalPosition.Y - 1.0f, HandOffFogDensity, 0.35f);
			await Cutscene.Wait(this, 0.35f, ct);
			ct.ThrowIfCancellationRequested();

			// The peak of the fog: the next flight is under their feet, already walking.
			DetachOneWay();
			var next = PickLoopStair(from);
			SetLoopTarget(next, 1.2f);
			if (next != null && IsInstanceValid(_player))
			{
				// Always part way up it, never near its foot (Dan, 2026-09-22): the loop should feel like being
				// stuck on stairs, not like reaching bottoms and tops.
				LoopLandFraction = _rng.RandfRange(0.3f, 0.75f);
				PlaceOnFlight(next, LoopLandFraction, from);
				AttachOneWay(next);
				if (GameSettings.Instance.AutoTest) _ = Cutscene.Run(this, ct2 => TraceLanding(next, ct2));
			}
			if (_blood != null && IsInstanceValid(_blood)) _blood.Intensity = Mathf.Lerp(0.12f, 1f, LoopStage / (float)Mathf.Max(1, LoopLegs - 1));
			PlaySting();   // it calls again as the next flight comes out of the fog
			GD.Print($"[story] Act 6: the loop, flight {LoopStage + 1} of {LoopLegs} ({next?.Name}, {LoopLandFraction:0.00} up it)");

			// The fog thins to the clearing's own, the view settles, the hum lets go.
			var settle = CreateTween().SetParallel();
			if (rig != null) settle.TweenMethod(Callable.From<float>(v => _blinkSwim = v), 7f, 0f, 0.6f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			atmo?.SetHeightFog(GlobalPosition.Y + 1.0f, ClearingFogDensity, 0.6f);
			await Cutscene.Wait(this, 0.15f, ct);
			hum?.SetOverrideDb(null);
		}
		finally { _loopBusy = false; }
	}

	/// <summary>Autotest diagnostics: where the player goes in the second after a hand-off (in the flight's own frame).</summary>
	private async Task TraceLanding(StaircaseBuilder on, CancellationToken ct)
	{
		for (int i = 0; i < 4 && IsInstanceValid(_player) && IsInstanceValid(on); i++)
		{
			Vector3 l = on.ToLocal(_player.GlobalPosition);
			GD.Print($"[story]   after hand-off {i * 0.3f:0.0}s: on {on.Name} local ({l.X:0.00}, {l.Y:0.00}, {l.Z:0.00}) floor {_player.IsOnFloor()} vel {_player.Velocity.Length():0.00} move {_player.PlayerInput?.ScriptedMove} yaw {Mathf.RadToDeg(_player.CameraRig?.Yaw ?? 0f):0} target {_loopTarget?.Name} stage {LoopStage} t {Time.GetTicksMsec()}");
			await Cutscene.Wait(this, 0.3f, ct);
		}
	}

	/// <summary>
	/// Stands the player on a flight facing up it, <paramref name="fraction"/> of the way already behind
	/// them (0 = its foot; 0.5 = half way up the treads).
	/// </summary>
	private void PlaceOnFlight(StaircaseBuilder stair, float fraction, StaircaseBuilder from = null)
	{
		float yaw = UpYaw(stair);
		Vector3 at;
		if (fraction <= 0.01f) at = FootOf(stair);
		else
		{
			// Along the treads: the flight runs local -Z from the first step, rising Rise per Run.
			float treads = stair.Steps * fraction;
			at = stair.ToGlobal(new Vector3(0, treads * stair.Rise + 0.02f, -treads * stair.Run));
		}
		// No snap (Dan, 2026-09-22): the view keeps the SAME angle to the new flight that it had to the old one
		// (looking a little left of the treads stays a little left), the pitch is untouched, and the stride
		// carries over: the velocity is turned with the flight so the walk never stops.
		var rig = _player.CameraRig;
		float oldYaw = rig?.Yaw ?? yaw;
		float offset = from != null ? Mathf.Clamp(Mathf.Wrap(oldYaw - UpYaw(from), -Mathf.Pi, Mathf.Pi), -0.9f, 0.9f) : 0f;
		float newYaw = Mathf.Wrap(yaw + offset, -Mathf.Pi, Mathf.Pi);
		Vector3 vel = _player.Velocity;
		_player.Teleport(at + Vector3.Up * 0.15f, newYaw);
		float turned = Mathf.Wrap(newYaw - oldYaw, -Mathf.Pi, Mathf.Pi);
		_player.Velocity = new Basis(Vector3.Up, turned) * new Vector3(vel.X, 0f, vel.Z);
	}

	/// <summary>The yaw that faces straight up a flight.</summary>
	private static float UpYaw(StaircaseBuilder stair)
	{
		Vector3 up = -stair.GlobalBasis.Z;
		return Mathf.Atan2(-up.X, -up.Z);
	}

	private void AttachOneWay(StaircaseBuilder stair)
	{
		DetachOneWay();
		_oneWay = OneWayFlight.Attach(stair);
	}

	private void DetachOneWay()
	{
		if (_oneWay != null && IsInstanceValid(_oneWay)) _oneWay.Detach();
		_oneWay = null;
	}

	private void StartBlood()
	{
		if (_blood != null && IsInstanceValid(_blood)) return;
		_player ??= StoryBeat.Player(this);
		_blood = new BloodRain { Name = "BloodRain", Follow = _player };
		AddChild(_blood);
		_blood.Intensity = 0.12f;
		_blood.Start();
	}

	private void StopBlood(float fade)
	{
		if (_blood == null || !IsInstanceValid(_blood)) { _blood = null; return; }
		_blood.Stop(fade);
		_blood = null;
	}

	// ------------------------------------------------------------------ the voice in the clearing

	/// <summary>From the clearing's edge until the fall, the voice calls out of the trees now and then (and once at every hand-off).</summary>
	private async Task LoopVoices(CancellationToken ct)
	{
		var interval = GameSettings.Instance.AutoTest ? LoopVoiceIntervalAutoTest : LoopVoiceInterval;
		bool first = true;
		while (_calling && !_loopDone)
		{
			await Cutscene.Wait(this, first ? 1.2f : _rng.RandfRange(interval.X, interval.Y), ct);   // the first call at once
			first = false;
			if (!_calling || _loopDone || _loopBusy) continue;
			PlaySting();
		}
	}

	// The four recorded takes read "close/quiet" to "far/harsh"; each keeps its own presence and
	// pitch/level/placement are re-rolled so no two calls land the same way. Voices stay deep.
	private static readonly string[] StingVariants = { "distant", "light", "medium", "loud" };

	/// <summary>One "come up and see" from a random spot in the trees around the player. No caption.
	/// In the loop's daze it is now and then doubled: a second call a beat behind the first, from another side.</summary>
	private void PlaySting(bool echo = true)
	{
		_player ??= StoryBeat.Player(this);
		if (_player == null || !IsInstanceValid(_player)) return;
		if (echo && _dazeOn && _rng.Randf() < 0.35f)
			_ = Cutscene.Run(this, async ct => { await Cutscene.Wait(this, _rng.RandfRange(0.3f, 0.7f), ct); PlaySting(echo: false); });
		string variant = StingVariants[_rng.RandiRange(0, StingVariants.Length - 1)];
		string path = $"res://assets/audio/voice/come_and_see_{variant}.mp3";
		if (!ResourceLoader.Exists(path)) return;
		(float baseDb, float unitSize, float maxDist, bool harsh) = variant switch
		{
			"distant" => (-14f, 9f, 100f, false),
			"light" => (-9f, 5f, 60f, false),
			"medium" => (-5f, 4f, 55f, false),
			_ => (-1f, 3f, 50f, true),
		};
		bool distorted = harsh && _rng.Randf() < 0.35f;
		// Outside the ring (the walk up from the creek) it calls from the trees AHEAD, toward the clearing.
		float ang = _rng.RandfRange(0f, Mathf.Tau);
		Vector3 toClearing = GlobalPosition - _player.GlobalPosition; toClearing.Y = 0;
		if (toClearing.Length() > FenceRadius) ang = Mathf.Atan2(toClearing.Z, toClearing.X) + _rng.RandfRange(-0.7f, 0.7f);
		float dist = _rng.RandfRange(9f, 24f);
		var voice = new AudioStreamPlayer3D
		{
			Stream = GD.Load<AudioStream>(path), Bus = distorted ? "VoiceHarsh" : "Voice",
			UnitSize = unitSize, MaxDistance = maxDist,
			VolumeDb = baseDb + _rng.RandfRange(-2f, 2f),
			PitchScale = _rng.RandfRange(0.8f, 0.97f),
		};
		Cutscene.SceneRoot(this).AddChild(voice);
		voice.GlobalPosition = _player.GlobalPosition + new Vector3(Mathf.Cos(ang), 0.5f, Mathf.Sin(ang)) * dist;
		voice.Finished += voice.QueueFree;
		voice.Play();
		StoryBeat.FadeIn(voice, voice.VolumeDb, 0.5f);
		LoopVoiceCount++;
	}

	private StaircaseBuilder PickLoopStair(StaircaseBuilder not)
	{
		var pool = _loopStairs.FindAll(m => m != not && IsInstanceValid(m));
		return pool.Count == 0 ? not : pool[_rng.RandiRange(0, pool.Count - 1)];
	}

	// ------------------------------------------------------------------ the daze

	/// <summary>The hand-off's own breathe-out (Blink tweens this); the daze sums it onto the rig's FovSwim.</summary>
	private float _blinkSwim;
	private bool _dazeOn;
	/// <summary>For tests: 0..1 how far the daze has come in.</summary>
	public float DazeStrength { get; private set; }
	/// <summary>How wide the view breathes at full daze (degrees) and how far it rolls (degrees), not too much.</summary>
	[Export] public float DazeFovDegrees = 10f;
	[Export] public float DazeRollDegrees = 1.5f;
	/// <summary>Seconds the daze takes to come fully in after the loop starts (about the first flight).</summary>
	[Export] public float DazeInSeconds = 14f;

	/// <summary>
	/// The loop's daze (Dan, 2026-09-22: trippy, a daze, but not too much): while the loop runs the view
	/// breathes slowly in and out (the field of view, on a 7 s cycle), rolls a little on another period,
	/// the fog's red pulses with the hum's beat, the hum itself wobbles slightly in pitch, and the voice
	/// is now and then doubled. Eases in over the first flight and off again at the fall.
	/// </summary>
	private void StartDaze()
	{
		if (_dazeOn) return;
		_dazeOn = true;
		_ = Cutscene.Run(this, DazeLoop);
	}

	private async Task DazeLoop(CancellationToken ct)
	{
		double t = 0;
		float strength = 0f;
		var humNode = Audio.StairsHum.Instance?.GetNodeOrNull<AudioStreamPlayer>("Hum");
		try
		{
			while (_dazeOn && !_loopDone)
			{
				float dt = (float)GetProcessDeltaTime();
				t += dt;
				strength = Mathf.MoveToward(strength, 1f, dt / Mathf.Max(1f, DazeInSeconds));
				DazeStrength = strength;
				var rig = _player?.CameraRig;
				if (rig != null)
				{
					rig.FovSwim = _blinkSwim + strength * DazeFovDegrees * 0.5f * (1f + Mathf.Sin((float)(t * Mathf.Tau / 7.0)));
					rig.RollSwim = strength * Mathf.DegToRad(DazeRollDegrees) * Mathf.Sin((float)(t * Mathf.Tau / 11.0));
				}
				// The red in the fog pulses with a slow beat; the hum wobbles under it (never above its own pitch).
				float pulse = 1f + strength * 0.35f * Mathf.Sin((float)(t * Mathf.Tau / 3.2));
				if (_blood != null && IsInstanceValid(_blood)) _blood.TintPulse = pulse;
				if (humNode != null && IsInstanceValid(humNode)) humNode.PitchScale = 1f - strength * 0.04f * (0.5f + 0.5f * Mathf.Sin((float)(t * Mathf.Tau / 5.3)));
				await Cutscene.Frame(this, ct);
			}
		}
		finally
		{
			// Off again: the view settles, the hum comes back to pitch, the red stops pulsing.
			var rig = _player?.CameraRig;
			if (rig != null)
			{
				var tw = CreateTween().SetParallel();
				tw.TweenProperty(rig, "FovSwim", 0f, 1.2f).SetTrans(Tween.TransitionType.Sine);
				tw.TweenProperty(rig, "RollSwim", 0f, 1.2f).SetTrans(Tween.TransitionType.Sine);
			}
			if (humNode != null && IsInstanceValid(humNode)) humNode.PitchScale = 1f;
			if (_blood != null && IsInstanceValid(_blood)) _blood.TintPulse = 1f;
			_dazeOn = false;
			DazeStrength = 0f;
		}
	}

	private void StopDaze() => _dazeOn = false;

	/// <summary>
	/// The last landing: the player is pulled off its back edge and drops to the ground behind the
	/// flight, everything goes black at the impact, and night falls while they are out. They wake on
	/// the trail past the clearing, outside the ring, in fog so thick the trees are grey; nothing is
	/// said. The compass now points at the ridge.
	/// </summary>
	private async Task Fall(StaircaseBuilder from, CancellationToken ct)
	{
		var s = StoryManager.Instance;
		var fader = StoryBeat.Fader(this);
		var terrain = GroundSnap.FindTerrain(this);
		try
		{
			GD.Print("[story] Act 6: the fall");
			_loopLight?.CreateTween().TweenProperty(_loopLight, "light_energy", 0f, 0.5f);
			DetachOneWay();
			var hum = Audio.StairsHum.Instance;
			hum?.SetOverrideDb(hum.MaxDb);
			// Smooth, not snappy (Dan, 2026-09-22): no lurch. A slow forward stumble off the back edge along a gentle
			// arc over 1.4-1.8 s, the view pitching down slowly, and the black coming in over the last 0.6 s of the
			// drop rather than at the impact; a soft body thump as it lands in the dark.
			StoryBeat.PlayAt(this, "res://assets/audio/sfx/breath_in_02.wav", "Events", Vector3.Zero, -8f, pitch: 0.9f);
			StopDaze();
			Vector3 start = _player.GlobalPosition;
			// Two phases, so the body never passes through the stone (Dan, 2026-09-22: it looked like falling THROUGH the
			// stairs): first a stumble straight off the landing's back edge at landing height, well clear of the flight's
			// back wall (1.8 m beyond BackZ, in the flight's own -Z, away from the treads), then the drop from there. If the
			// ground behind is higher than the landing (a slope), the edge is taken off the SIDE of the landing instead.
			float landingY = from.ToGlobal(new Vector3(0, from.TotalHeight, from.BackZ)).Y;
			Vector3 edge = from.ToGlobal(new Vector3(0, from.TotalHeight, from.BackZ - 1.8f));
			float groundBehind = terrain?.HeightAt(edge.X, edge.Z) ?? (landingY - from.TotalHeight * from.Scale.Y);
			if (groundBehind > landingY - 1.5f)
			{
				float side = _rng.Randf() < 0.5f ? -1f : 1f;
				edge = from.ToGlobal(new Vector3(side * (from.Width * 0.5f + 2.2f), from.TotalHeight, (from.TopFrontZ + from.BackZ) * 0.5f));
				groundBehind = terrain?.HeightAt(edge.X, edge.Z) ?? groundBehind;
			}
			edge.Y = start.Y;   // the stumble stays at the landing's height until the edge
			Vector3 land = new(edge.X, groundBehind + 0.1f, edge.Z);
			float drop = Mathf.Max(1f, start.Y - land.Y);
			float stumble = 0.55f;
			float seconds = Mathf.Clamp(0.9f + 0.18f * drop, 1.4f, 1.8f);
			FallClearOfFlight = from.ToLocal(land).Z < from.BackZ - 1.0f || Mathf.Abs(from.ToLocal(land).X) > from.Width * 0.5f + 1.0f;
			var t = CreateTween();
			t.SetParallel();
			t.TweenProperty(_player, "global_position:x", edge.X, stumble).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			t.TweenProperty(_player, "global_position:z", edge.Z, stumble).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
			t.Chain();
			t.SetParallel();
			// Off the edge: the drop, drifting a last half metre outward as it goes.
			Vector3 drift = (land - start); drift.Y = 0;
			drift = drift.Length() > 0.01f ? drift.Normalized() * 0.5f : Vector3.Zero;
			t.TweenProperty(_player, "global_position:x", land.X + drift.X, seconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
			t.TweenProperty(_player, "global_position:z", land.Z + drift.Z, seconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
			t.TweenProperty(_player, "global_position:y", land.Y, seconds).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			seconds += stumble;   // the timers below cover the whole thing
			_ = Cutscene.Run(this, async ct2 =>
			{
				// The view tips forward and down about 18 degrees over the whole drop, on a sine, no yaw, no roll.
				double elapsed = 0, total = seconds;
				float applied = 0f;
				while (elapsed < total && IsInstanceValid(_player))
				{
					float dt = (float)GetProcessDeltaTime();
					elapsed += dt;
					float want = -Mathf.DegToRad(18f) * Mathf.Sin(0.5f * Mathf.Pi * Mathf.Clamp((float)(elapsed / total), 0f, 1f));
					_player.PlayerInput?.AddCutsceneLook(new Vector2(0f, want - applied));
					applied = want;
					await Cutscene.Frame(this, ct2);
				}
			});
			_ = Cutscene.Run(this, async ct2 =>
			{
				// The black comes in over the last 0.6 s of the drop.
				await Cutscene.Wait(this, Mathf.Max(0f, seconds - 0.6f), ct2);
				if (fader != null) await fader.Fade(1f, 0.6f, ct2);
			});
			await Cutscene.Tween(this, t, ct);
			if (fader != null && fader.BlackAlpha < 0.99f) await fader.Fade(1f, 0.1f, ct);
			// Landing in the dark: a proper body thump, flat at the ear, and the breath going out of them.
			PlayFlat($"res://assets/audio/sfx/body_thump_{_rng.RandiRange(1, 2):00}.wav", 8f);
			StoryBeat.PlayAt(this, "res://assets/audio/sfx/breath_out_04.wav", "Events", Vector3.Zero, -4f, pitch: 0.85f);
			hum?.SetOverrideDb(null);
			StopBlood(0.5f);
			await Cutscene.Wait(this, 3.0f, ct);

			// Out cold. It is the same night (Dan, 2026-09-22: nothing in the Hollow happens by day; the
			// Act6NightFell flag stays for old saves and the compass, the mood does not change).
			_loopDone = true;
			StoryBeat.SetMood(this, ForestAtmosphere.Mood.Night, 0.5f);   // the clearing's menace lets go; it is the same night
			s?.SetFlag(StoryManager.Flag.Act6NightFell);
			s?.SetFlag(StoryManager.Flag.ClearingLoopDone);
			TearDownLoop();
			// Wake on the trail past the clearing (outside the ring), facing onward (toward the ridge), in thick fog.
			Vector3 wake = _player.GlobalPosition;
			if (terrain != null && IsInstanceValid(_player))
			{
				terrain.TrailDistance(GlobalPosition.X, GlobalPosition.Z, out float sc);
				wake = terrain.TrailPoint(sc + WakeTrailMetres, out var tan);
				wake.Y = terrain.HeightAt(wake.X, wake.Z) + 0.2f;
				_player.Teleport(wake, Mathf.Atan2(-tan.X, -tan.Z));
			}
			// Behind the black: the stairs were never there. Forest over the clearing and the way back; a light far off.
			ResetWorld(restoring: false);
			var atmo = StoryBeat.Atmosphere(this);
			atmo?.SetHeightFog(wake.Y - 4f, WakeFogDensity, 0.3f);
			await Cutscene.Wait(this, 1.2f, ct);
			if (fader != null) await fader.Fade(0f, 3.0f, ct);
			// Nothing is said on waking (Dan, 2026-09-22): the night, the fog and the compass say it. The fog
			// only thins slowly, over the walk up to the lookout.
			atmo?.SetHeightFog(wake.Y - 4f, 0.03f, WakeFogClearSeconds);
			GD.Print("[story] Act 6: woke on the trail; night, fog");
		}
		finally { _loopBusy = false; }
	}

	/// <summary>For tests: the fall's landing point lay clear of the flight (behind its back wall or off its side).</summary>
	public bool FallClearOfFlight { get; private set; }

	/// <summary>A one-shot flat at the ear (no 3D falloff) on the Events bus, freed when it ends.</summary>
	private void PlayFlat(string path, float db)
	{
		if (!ResourceLoader.Exists(path)) return;
		var p = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = db };
		Cutscene.SceneRoot(this).AddChild(p);
		p.Finished += p.QueueFree;
		p.Play();
	}

	private void TearDownLoop()
	{
		_loopArmed = false;
		_loopStarted = false;
		_calling = false;
		_loopTarget = null;
		StopDaze();
		DetachOneWay();
		StopBlood(1.5f);
		if (_loopMarker != null && IsInstanceValid(_loopMarker)) _loopMarker.QueueFree();
		_loopMarker = null;
		if (_loopLight != null && IsInstanceValid(_loopLight)) _loopLight.QueueFree();
		_loopLight = null;
	}

	// ------------------------------------------------------------------ Act 7: as if it were never there

	/// <summary>
	/// Act 7's world (Dan, 2026-09-22): the fifteen stairs, the ring and the clearing's own flight are gone
	/// and the clearing's silence lifts. The meadow and every trail stay as they were (Dan: no forest over
	/// the way back, no light in the distance; the compass leads to the bunker). Live at the wake (behind
	/// the black) and on any Continue from the fall on.
	/// </summary>
	private void ResetWorld(bool restoring)
	{
		if (WorldReset) return;
		WorldReset = true;
		_loopDone = true;
		SetProcess(false);
		ReleaseSpotlight(0.5f);
		if (_voiceZone != null && IsInstanceValid(_voiceZone)) _voiceZone.QueueFree();
		_voiceZone = null;
		// The stand, the lights, the ring: gone.
		if (_stand != null && IsInstanceValid(_stand)) { if (_stand.GetParent() != null) _stand.GetParent().RemoveChild(_stand); _stand.QueueFree(); }
		_stand = null;
		_minis.Clear();
		_loopStairs.Clear();
		_trailStair = null;
		if (_lights != null && IsInstanceValid(_lights)) { if (_lights.GetParent() != null) _lights.GetParent().RemoveChild(_lights); _lights.QueueFree(); }
		_lights = null;
		if (_fence != null && IsInstanceValid(_fence)) _fence.QueueFree();
		_fence = null;
		if (_dressing != null && IsInstanceValid(_dressing)) { _dressing.Visible = false; _dressing.ProcessMode = ProcessModeEnum.Disabled; }
		// The clearing's own flight: unseen and unwalkable (this node is its child, so it stays; its body does not).
		if (_original != null && IsInstanceValid(_original))
		{
			_original.Visible = false;
			foreach (var body in _original.FindChildren("*", "StaticBody3D", true, false).OfType<StaticBody3D>()) body.CollisionLayer = 0;
			foreach (var zone in _original.FindChildren("*", "SilenceZone", true, false).OfType<Audio.SilenceZone>()) zone.Active = false;
			foreach (var area in _original.FindChildren("*", "Area3D", true, false).OfType<Area3D>()) area.Monitoring = false;
		}
		foreach (var zone in Audio.SilenceZone.All)
			if (zone != null && IsInstanceValid(zone) && new Vector2(zone.GlobalPosition.X - GlobalPosition.X, zone.GlobalPosition.Z - GlobalPosition.Z).Length() < FenceRadius + 20f) zone.Active = false;
		if (!restoring) GD.Print("[story] Act 7: the stairs and the ring are gone");
	}

	/// <summary>
	/// The clearing's beat: the spotlight finds the player, the voice speaks (saved at once as
	/// ClearingVoiceHeard: the compass moves on to the ridge, night starts to fall), and the clearing
	/// answers: the stairs' hum swells and the voice comes from everywhere at once. Nothing is taken:
	/// the cap stays in the player's hands until the last staircase (Act 11). The clearing's own
	/// staircase stays broken.
	/// </summary>
	private async Task VoiceBeat(CancellationToken ct)
	{
		BuildPlayerSpotlight();
		PlayVoice();
		StoryManager.Instance?.MarkClearingVoiceHeard();
		await Cutscene.Wait(this, 4.0f, ct);   // the voice is heard, never read (only the radio's lines go on screen)
		await Payoff(ct);
		if (LoopActive(StoryManager.Instance)) ArmLoop();   // and the clearing waits: come up and see
	}

	/// <summary>The hum surges for a few seconds and the chorus closes in from the trees.</summary>
	private async Task Payoff(CancellationToken ct)
	{
		var hum = Audio.StairsHum.Instance;
		Tween surge = null;
		if (hum != null)
		{
			surge = CreateTween();
			surge.TweenMethod(Callable.From<float>(db => hum.SetOverrideDb(db)), hum.EdgeDb, 2f, 1.5f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
		}
		try
		{
			await Cutscene.Wait(this, 0.8f, ct);
			// The voice, three ways at once, from the trees around the player.
			_ = Chorus(ct);
			await Cutscene.Wait(this, 5.5f, ct);   // the chorus plays out; nothing is printed
		}
		finally
		{
			surge?.Kill();
			hum?.SetOverrideDb(null);   // back to proximity: the surge is over
		}
		GD.Print("[story] Act 6: the clearing has spoken");
	}

	/// <summary>Three recorded takes from three directions, staggered, each coming up out of the trees rather than bursting in.</summary>
	private async Task Chorus(CancellationToken ct)
	{
		if (_player == null) return;
		string[] takes = { "distant", "light", "medium" };
		float[] levels = { -10f, -7f, -4f };
		float baseAng = GD.Randf() * Mathf.Tau;
		for (int i = 0; i < takes.Length; i++)
		{
			string path = $"res://assets/audio/voice/come_and_see_{takes[i]}.mp3";
			if (ResourceLoader.Exists(path) && IsInstanceValid(_player))
			{
				float ang = baseAng + i * Mathf.Tau / 3f + (float)GD.RandRange(-0.3, 0.3);
				float dist = (float)GD.RandRange(10.0, 14.0);
				var voice = new AudioStreamPlayer3D
				{
					Stream = GD.Load<AudioStream>(path), Bus = "Voice",
					UnitSize = 6f, MaxDistance = 70f, VolumeDb = levels[i],
					PitchScale = (float)GD.RandRange(0.85, 0.95),
				};
				// Must be parented before GlobalPosition is set, or Godot can't resolve the transform.
				Cutscene.SceneRoot(this).AddChild(voice);
				voice.GlobalPosition = _player.GlobalPosition + new Vector3(Mathf.Cos(ang), 0.6f, Mathf.Sin(ang)) * dist;
				voice.Finished += voice.QueueFree;
				voice.Play();
				StoryBeat.FadeIn(voice, levels[i], 0.5f);
			}
			await Cutscene.Wait(this, 0.5f, ct);
		}
	}

	/// <summary>The clearing's line, heard once: clear but dreamlike-distant, so it plays
	/// unpositioned (inside the head rather than from a spot in the trees), slightly slowed.</summary>
	private void PlayVoice()
	{
		const string path = "res://assets/audio/voice/come_and_see_distant.mp3";
		if (!ResourceLoader.Exists(path)) return;
		var voice = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(path), Bus = "Voice", VolumeDb = -8f, PitchScale = 0.92f };
		AddChild(voice);
		voice.Finished += voice.QueueFree;
		voice.Play();
		StoryBeat.FadeIn(voice, -8f, 0.7f);
	}

	/// <summary>A giant spotlight snaps on directly overhead the instant the voice speaks, catching
	/// the player in a harsh red glare — the clearing's payoff beat. It rides with them while they
	/// remain in the clearing.</summary>
	private void BuildPlayerSpotlight()
	{
		if (_playerSpot != null || _player == null) return;
		_playerSpot = new SpotLight3D
		{
			Name = "Act6PlayerSpot",
			LightColor = new Color(1f, 0.05f, 0.02f),
			LightEnergy = 12f,
			SpotRange = 30f,
			SpotAngle = 12f,
			SpotAngleAttenuation = 2f,
			ShadowEnabled = true,
		};
		Cutscene.SceneRoot(this).AddChild(_playerSpot);
		FollowPlayer();
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		_player ??= StoryBeat.Player(this);
		WatchForChoice();
		if (_playerSpot == null || _player == null || !IsInstanceValid(_player)) return;
		FollowPlayer();
		float d = new Vector2(_player.GlobalPosition.X - GlobalPosition.X, _player.GlobalPosition.Z - GlobalPosition.Z).Length();
		if (d > SpotlightReleaseRadius) ReleaseSpotlight(2.5f);
	}

	private void FollowPlayer()
	{
		// A giant spotlight rides directly overhead, always aimed straight down at the player.
		_playerSpot.GlobalPosition = _player.GlobalPosition + Vector3.Up * 22f;
		_playerSpot.LookAt(_player.GlobalPosition, Vector3.Forward);
	}

	/// <summary>The voice beat is over: fade the spotlight out and free it.</summary>
	private void ReleaseSpotlight(float fadeSeconds)
	{
		if (_playerSpot == null) return;
		var spot = _playerSpot;
		_playerSpot = null;   // processing stays on: the clearing keeps watching the player's feet for the choice
		if (!IsInstanceValid(spot)) return;
		var t = spot.CreateTween();
		t.TweenProperty(spot, "light_energy", 0f, fadeSeconds);
		t.TweenCallback(Callable.From(spot.QueueFree));
	}
}
