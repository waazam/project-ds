using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 7: once the clearing's business is done (its voice heard), the path climbs to a
/// lookout on the ridge across the creek, and from there the player sees the cabin they
/// left burning. Checkpoint 6 fires the moment the player enters the lookout zone
/// (<see cref="LookoutRadius"/> around <see cref="LookoutPath"/>, or the level's
/// "fire_lookout_marker" node when the path is empty). The cabin catches a little earlier,
/// when the player comes within <see cref="PreIgniteRadius"/> of the lookout, so it is
/// already ablaze when it comes into view. If night has not fallen yet (the Act 6 fallback's
/// timer still running), it falls now, quickly: the fire is a night sight.
///
/// Without any lookout in the level the old behaviour stands: the checkpoint fires the moment
/// the player comes within <see cref="Radius"/> of the cabin.
///
/// This is the beat's logic only: the flames, smoke, embers and glow are the cabin's own
/// (<c>Cabin.SetBurning</c>). This adds the fire's sound (a crackle bed and the odd structural
/// groan, on the Events bus), a tall glow over the roof so the fire lights the smoke and the
/// trees around it from afar, warms the screen's shadow tint the closer the player stands, and
/// keeps a beat of quiet after the checkpoint (<see cref="LineDelay"/>; the old line is gone, no self-talk).
/// the checkpoint; never on Continue).
///
/// The per-frame work (groans, heat tint) only runs while the player is within
/// <see cref="HeatReach"/> x <see cref="Radius"/> of the cabin, where the tint is non-zero: a
/// wide trigger wakes it and leaving it puts it to sleep.
///
/// Restore: from Act 7 on, the cabin is simply burning when the level loads
/// (the story never shows it burning out).
/// </summary>
public partial class CabinFireEvent : Node
{
	[Export] public NodePath CabinPath = "..";
	/// <summary>The lookout the fire is seen from. Empty: the level's "fire_lookout_marker" node, if any.</summary>
	[Export] public NodePath LookoutPath = "";
	/// <summary>Entering this radius (horizontal metres) around the lookout fires checkpoint 6.</summary>
	[Export] public float LookoutRadius = 7f;
	/// <summary>The cabin catches when the player is this close to the lookout (and the story allows it).</summary>
	[Export] public float PreIgniteRadius = 45f;
	/// <summary>Fallback (no lookout): the checkpoint fires within this radius of the cabin.</summary>
	[Export] public float Radius = 30f;
	/// <summary>Multiple of <see cref="Radius"/> beyond which the heat tint is zero and processing stops.</summary>
	[Export] public float HeatReach = 3f;
	/// <summary>Unused since 2026-09-22 (no self-talk); kept so scene overrides still load.</summary>
	[Export] public string SightLine = "";
	[Export] public float LineDelay = 2f;
	/// <summary>Seconds for night to fall at the lookout if it has not yet.</summary>
	[Export] public float NightFallSeconds = 6f;
	[ExportGroup("Glow seen from afar")]
	[Export] public Color GlowColor = new(1f, 0.45f, 0.16f);
	[Export] public float GlowEnergy = 9f;
	[Export] public float GlowRange = 60f;   // reaches the far bank: the trail now runs past the fire across the creek
	[Export] public float GlowHeight = 5.5f;

	private Cabin _cabin;
	private Node3D _lookout;
	private Area3D _zone;
	private Area3D _preZone;
	private Area3D _heatZone;
	private PlayerController _player;
	private bool _burning;
	private ShaderMaterial _postMat;
	private Color _postBaseTint;
	private double _clock;
	private double _nextGroan;
	private OmniLight3D _glow;
	private readonly RandomNumberGenerator _rng = new();

	/// <summary>For tests: whether the cabin is on fire.</summary>
	public bool Burning => _burning;
	/// <summary>For tests: the lookout the checkpoint belongs to (null: the cabin-radius fallback).</summary>
	public Node3D Lookout => _lookout;

	public override void _Ready()
	{
		_cabin = GetNode<Cabin>(CabinPath);
		SetProcess(false);
		Callable.From(Restore).CallDeferred();
		if (StoryManager.Instance is { } s) s.FlagSet += OnFlag;
	}

	public override void _ExitTree()
	{
		if (StoryManager.Instance is { } s) s.FlagSet -= OnFlag;
		// The post material is a shared resource: never leave the heat tint baked into it.
		_postMat?.SetShaderParameter("shadow_tint", _postBaseTint);
	}

	private void Restore()
	{
		// Deferred: the cabin (our parent) is still readying its children during _Ready, and the lookout may sit later in the tree.
		_lookout = !LookoutPath.IsEmpty ? GetNodeOrNull<Node3D>(LookoutPath) : null;
		_lookout ??= GetTree().GetFirstNodeInGroup("fire_lookout_marker") as Node3D;
		if (_lookout != null)
		{
			_zone = MakeWorldTrigger(_lookout.GlobalPosition, LookoutRadius, OnZoneEntered, "FireLookoutZone");
			_preZone = MakeWorldTrigger(_lookout.GlobalPosition, PreIgniteRadius, _ => TryPreIgnite(), "FirePreIgniteZone");
		}
		else _zone = StoryBeat.MakeTrigger(_cabin, new CylinderShape3D { Radius = Radius, Height = 80f }, Vector3.Zero, OnZoneEntered, "FireSightZone");
		if (StoryManager.Instance is { Current: >= Checkpoint.Act7CabinBurning }) { Ignite(); Struck = true; GD.Print($"[story] Act 7: fire restored as already seen (checkpoint {StoryManager.Instance.Current})"); }
		// A Continue after the fall but before the strike: armed from the start.
		else if (StoryManager.Instance is { } sm && sm.HasFlag(StoryManager.Flag.ClearingLoopDone)) Callable.From(ArmAtWake).CallDeferred();
	}

	/// <summary>A tall player trigger at a world point, parented to this beat (top level, so the cabin's transform doesn't matter).</summary>
	private Area3D MakeWorldTrigger(Vector3 at, float radius, System.Action<PlayerController> onEnter, string name)
	{
		var area = StoryBeat.MakeTrigger(_cabin, new CylinderShape3D { Radius = radius, Height = 120f }, Vector3.Zero, onEnter, name);
		area.TopLevel = true;
		area.GlobalPosition = at;
		return area;
	}

	/// <summary>Only after the clearing's loop (the Act 7 wake): the cabin stands dark until lightning takes it.</summary>
	// Past the footbridge and not yet struck. (The loop's end arms the strike; it is not a gate any more: in live play
	// nothing must be able to leave the cabin dark for good.)
	private static bool CanFire(StoryManager s) => s != null && s.Current >= Checkpoint.Act6BridgeCrossed && s.Current < Checkpoint.Act7CabinBurning;

	/// <summary>Seconds after the wake (the loop's end) by which the strike happens whatever the player looks at.</summary>
	[Export] public float FallbackSeconds = 40f;
	[Export] public float FallbackSecondsAutoTest = 6f;
	private double _fallbackAt = -1;

	private void OnFlag(string flag)
	{
		// The wake after the fall arms the strike outright (Dan, 2026-09-22: it was never firing in live play when the
		// player walked the ridge without looking across, and the 7 m lookout zone could be passed by).
		if (flag == StoryManager.Flag.ClearingLoopDone) ArmAtWake();
		if (StoryBeat.PlayerInside(_preZone) != null) TryPreIgnite();
		if (StoryBeat.PlayerInside(_zone) is { } p) OnZoneEntered(p);
	}

	private void ArmAtWake()
	{
		if (_burning || Struck || !CanFire(StoryManager.Instance)) return;
		_player = StoryBeat.Player(this);
		_armed = true;
		_fallbackAt = _clock + (GameSettings.Instance.AutoTest ? FallbackSecondsAutoTest : FallbackSeconds);
		// No strike any more: the cabin is already burning when they wake (Dan, 2026-09-22); seeing it is checkpoint 6.
		EnsureNight();
		Ignite(instant: true);
		SetProcess(true);
		GD.Print($"[story] Act 7: the cabin burns from the wake (checkpoint on sight, fallback in {_fallbackAt - _clock:0} s)");
	}

	/// <summary>For tests: lightning has struck the cabin (the fire's start; checkpoint 6 goes with it).</summary>
	public bool Struck { get; private set; }
	/// <summary>For tests: the cabin has come into view on the walk and the strike is counting down.</summary>
	public bool Sighted { get; private set; }
	/// <summary>How far the player may be for the cabin to count as "in view" (the sight lane from the ridge).</summary>
	[Export] public float SightRange = 160f;
	/// <summary>Seconds between the cabin coming into view and the strike (random in this range).</summary>
	[Export] public Vector2 StrikeDelay = new(2f, 6f);
	private double _strikeAt = -1;
	private bool _armed;

	/// <summary>Within 45 m of the lookout: the cabin is in the sight lane; from here on, the moment the player looks at
	/// it the countdown to the strike begins (Dan, 2026-09-22: lightning takes it as you walk up and see it).</summary>
	private void TryPreIgnite()
	{
		if (_burning || _armed || !CanFire(StoryManager.Instance)) return;
		_armed = true;
		_player = StoryBeat.Player(this);
		SetProcess(true);
	}

	/// <summary>The lookout itself: if they never looked at it on the way, it is struck now anyway.</summary>
	private void OnZoneEntered(PlayerController player)
	{
		if (!CanFire(StoryManager.Instance) || Struck) return;
		_player = player;
		_armed = true;
		if (!Sighted) { Sighted = true; _strikeAt = _clock + 1.0; }
		SetProcess(true);
	}

	/// <summary>The cabin is in front of the camera and not too far: the sight that starts the countdown.</summary>
	private bool CabinInView()
	{
		var cam = _player?.CameraRig?.Camera;
		if (cam == null) return false;
		Vector3 to = _cabin.GlobalPosition + Vector3.Up * 2.5f - cam.GlobalPosition;
		float d = to.Length();
		if (d > SightRange) return false;
		return (-cam.GlobalBasis.Z).Dot(to / Mathf.Max(d, 0.01f)) > 0.64f;   // within ~50 degrees of straight ahead (no ray: fog and trees never hide it)
	}

	/// <summary>
	/// The strike (Dan, 2026-09-22): a flash over the cabin, a bolt down onto its roof for two frames, the thunder
	/// right on it (it is close), and the cabin catches: embers, then flames, the glow ramping over three seconds,
	/// then it burns as before (sealed door, the glow across the creek). Checkpoint 6 is set at the strike; the
	/// player keeps control; the giant's crossing follows the checkpoint as before.
	/// </summary>
	private async System.Threading.Tasks.Task Strike(System.Threading.CancellationToken ct)
	{
		// The lightning strike is scrapped (Dan, 2026-09-22: it broke too much). The cabin has been burning since the
		// wake; this beat is only the SIGHT of it: checkpoint 6, and the giant's crossing follows. The name stays for the tests.
		Struck = true;
		GD.Print($"[story] Act 7: sight beat (sighted {Sighted}, player {(_player != null)}, checkpoint {StoryManager.Instance?.Current})");
		var player = _player ?? StoryBeat.Player(this);
		if (!_burning) { EnsureNight(); Ignite(instant: true); }
		if (player != null) StoryBeat.ReachCheckpoint(player, Checkpoint.Act7CabinBurning);
		GD.Print("[story] Act 7: the burning cabin is in view");
		await Cutscene.Frame(this, ct);
	}

	/// <summary>The Hollow is night throughout; this only records the flag older code keyed on (the mood is already Night).</summary>
	private void EnsureNight()
	{
		var s = StoryManager.Instance;
		if (s == null || s.HasFlag(StoryManager.Flag.Act6NightFell)) return;
		StoryBeat.SetMood(this, ForestAtmosphere.Mood.Night, NightFallSeconds);
		s.SetFlag(StoryManager.Flag.Act6NightFell);
	}

	private void Ignite(bool instant = true)
	{
		if (_burning) return;
		_burning = true;
		// Not Struck here: the fire starts at the wake now, the SIGHT beat (Strike) is what sets Struck and checkpoint 6.
		_player = StoryBeat.Player(this);
		if (instant) _cabin.SetBurning(1f);
		else
		{
			// Embers first, then the flames take over three seconds.
			_cabin.SetBurning(0.12f);
			var ramp = CreateTween();
			ramp.TweenMethod(Callable.From<float>(v => _cabin.SetBurning(v)), 0.12f, 1f, 3f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
		}
		_cabin.SealShut();   // nobody goes into a burning house (Dan, 2026-09-22): the door is shut and solid for good

		var crackleBed = new AudioStreamPlayer3D { Name = "FireCrackle", Bus = "Events", UnitSize = 7f, MaxDistance = 55f, Position = new Vector3(0, 1.4f, 0) };
		_cabin.AddChild(crackleBed);
		crackleBed.AddChild(new Audio.AmbienceLoop { StreamPath = "res://assets/audio/ambient/fire_crackle_loop.wav", BaseVolumeDb = 3f });

		// A big warm glow over the roof: lights the smoke column and the trees around the cabin, so the
		// fire reads as a fire (not a few orange specks) from the lookout across the creek.
		_glow = new OmniLight3D
		{
			Name = "FireGlow",
			LightColor = GlowColor,
			LightEnergy = instant ? GlowEnergy : 0f,
			OmniRange = GlowRange,
			OmniAttenuation = 0.9f,
			ShadowEnabled = false,
			Position = new Vector3(0, GlowHeight, 0),
		};
		_cabin.AddChild(_glow);

		_postMat = StoryBeat.PostMaterial(this);
		if (_postMat != null) _postBaseTint = (Color)_postMat.GetShaderParameter("shadow_tint");
		_nextGroan = _rng.RandfRange(2f, 6f);
		// Wake-up zone: inside it the groans and the heat tint run every frame; outside, nothing does.
		_heatZone = StoryBeat.MakeTrigger(_cabin, new CylinderShape3D { Radius = Radius * HeatReach, Height = 120f }, Vector3.Zero, _ => SetProcess(true), "FireHeatZone");
		_heatZone.BodyExited += b => { if (b is PlayerController) Sleep(); };
		SetProcess(true);   // the first frame measures for itself: the player may already stand inside, or far away
	}

	private void Sleep()
	{
		_postMat?.SetShaderParameter("shadow_tint", _postBaseTint);
		SetProcess(false);
	}

	private double _ignitedAt = -1;

	public override void _Process(double delta)
	{
		_clock += delta;
		// Armed on the walk up: the strike waits for the cabin to be seen, then a short beat.
		if (_armed && !Struck)
		{
			if (_player == null || !IsInstanceValid(_player)) _player = StoryBeat.Player(this);
			if (!Sighted && CabinInView())
			{
				Sighted = true;
				var delay = GameSettings.Instance.AutoTest ? new Vector2(1f, 2f) : StrikeDelay;
				_strikeAt = _clock + _rng.RandfRange(delay.X, delay.Y);
				GD.Print("[story] Act 7: the cabin in view; the sky is about to break");
			}
			else if (!Sighted && _fallbackAt > 0 && _clock >= _fallbackAt)
			{
				// Never looked across: the sky breaks anyway.
				Sighted = true;
				_strikeAt = _clock + 0.5;
				GD.Print("[story] Act 7: the strike's fallback timer ran out");
			}
			if (Sighted && _clock >= _strikeAt) { _ = Cutscene.Run(this, Strike); return; }
			if (!_burning) return;
		}

		float d = -1f;
		if (_player != null && IsInstanceValid(_player))
		{
			d = new Vector2(_player.GlobalPosition.X - _cabin.GlobalPosition.X, _player.GlobalPosition.Z - _cabin.GlobalPosition.Z).Length();
			// Far from the fire the heat effects sleep, but NEVER while the sight beat is still waiting: the cabin burns
			// from the wake now, and sleeping here on the first frame killed the sight check and the fallback, so
			// checkpoint 6 (and with it the giant and the door's dial) never came (Dan's playthrough, 2026-09-22).
			if (d > Radius * HeatReach + 1f) { if (!(_armed && !Struck)) Sleep(); return; }
		}

		if (_ignitedAt < 0 && _burning) _ignitedAt = _clock;
		float catching = _burning ? Mathf.Clamp((float)(_clock - _ignitedAt) / 3f, 0f, 1f) : 0f;   // the glow comes up with the flames
		if (_glow != null && IsInstanceValid(_glow))
			_glow.LightEnergy = GlowEnergy * Mathf.Max(catching, _cabin.Burning >= 0.99f ? 1f : catching) * (0.85f + 0.1f * Mathf.Sin((float)_clock * 2.3f) + 0.05f * Mathf.Sin((float)_clock * 9.1f));
		if (_clock >= _nextGroan)
		{
			_nextGroan = _clock + _rng.RandfRange(5.0f, 11.0f);
			string path = $"res://assets/audio/sfx/trunk_creak_{_rng.RandiRange(1, 3):00}.wav";
			StoryBeat.PlayAt(_cabin, path, "Events",
				new Vector3(_rng.RandfRange(-1.5f, 1.5f), 1.2f, _rng.RandfRange(-1.5f, 1.5f)),
				volumeDb: _rng.RandfRange(2f, 6f), unitSize: 6f, maxDistance: 45f, pitch: _rng.RandfRange(0.55f, 0.7f));
		}

		if (_postMat != null && d >= 0f)
		{
			float near = 1f - Mathf.Clamp((d - Radius * 0.5f) / (Radius * 2.5f), 0f, 1f);
			var heat = new Color(0.16f, 0.03f, 0.0f);
			_postMat.SetShaderParameter("shadow_tint", _postBaseTint.Lerp(heat, near * 0.8f));
		}
	}
}
