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
/// says the one line the sight gets (<see cref="SightLine"/>, <see cref="LineDelay"/> after
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
	[Export] public string SightLine = "Everything he wrote is in there.";
	[Export] public float LineDelay = 2f;
	/// <summary>Seconds for night to fall at the lookout if it has not yet.</summary>
	[Export] public float NightFallSeconds = 6f;
	[ExportGroup("Glow seen from afar")]
	[Export] public Color GlowColor = new(1f, 0.45f, 0.16f);
	[Export] public float GlowEnergy = 9f;
	[Export] public float GlowRange = 32f;
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
		if (StoryManager.Instance is { Current: >= Checkpoint.Act7CabinBurning }) Ignite();
	}

	/// <summary>A tall player trigger at a world point, parented to this beat (top level, so the cabin's transform doesn't matter).</summary>
	private Area3D MakeWorldTrigger(Vector3 at, float radius, System.Action<PlayerController> onEnter, string name)
	{
		var area = StoryBeat.MakeTrigger(_cabin, new CylinderShape3D { Radius = radius, Height = 120f }, Vector3.Zero, onEnter, name);
		area.TopLevel = true;
		area.GlobalPosition = at;
		return area;
	}

	private static bool CanFire(StoryManager s) => s is { ClearingVoiceHeard: true } && s.Current < Checkpoint.Act7CabinBurning;

	// The voice can be heard while already standing in range only in theory; re-check anyway.
	private void OnFlag(string _)
	{
		if (StoryBeat.PlayerInside(_preZone) != null) TryPreIgnite();
		if (StoryBeat.PlayerInside(_zone) is { } p) OnZoneEntered(p);
	}

	private void TryPreIgnite()
	{
		if (_burning || !CanFire(StoryManager.Instance)) return;
		Ignite();
		EnsureNight();
		GD.Print("[story] Act 7: the cabin catches");
	}

	private void OnZoneEntered(PlayerController player)
	{
		if (!CanFire(StoryManager.Instance)) return;
		Ignite();
		EnsureNight();
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act7CabinBurning);
		GD.Print("[story] Act 7: the cabin is burning");
		// A beat after the sight has landed, the one thing there is to say. Only here, never on a restore.
		Cutscene.Run(this, async ct =>
		{
			await Cutscene.Wait(this, LineDelay, ct);
			await StoryBeat.Caption(this, SightLine, 0.8f, 2.8f, 1.0f);
		});
	}

	/// <summary>The fire is seen at night: if the Act 6 fallback has not brought night yet, it comes now.</summary>
	private void EnsureNight()
	{
		var s = StoryManager.Instance;
		if (s == null || s.HasFlag(StoryManager.Flag.Act6NightFell)) return;
		StoryBeat.SetMood(this, ForestAtmosphere.Mood.Night, NightFallSeconds);
		s.SetFlag(StoryManager.Flag.Act6NightFell);
	}

	private void Ignite()
	{
		if (_burning) return;
		_burning = true;
		_player = StoryBeat.Player(this);
		_cabin.SetBurning(1f);

		var crackleBed = new AudioStreamPlayer3D { Name = "FireCrackle", Bus = "Events", UnitSize = 7f, MaxDistance = 55f, Position = new Vector3(0, 1.4f, 0) };
		_cabin.AddChild(crackleBed);
		crackleBed.AddChild(new Audio.AmbienceLoop { StreamPath = "res://assets/audio/ambient/fire_crackle_loop.wav", BaseVolumeDb = 3f });

		// A big warm glow over the roof: lights the smoke column and the trees around the cabin, so the
		// fire reads as a fire (not a few orange specks) from the lookout across the creek.
		_glow = new OmniLight3D
		{
			Name = "FireGlow",
			LightColor = GlowColor,
			LightEnergy = GlowEnergy,
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

	public override void _Process(double delta)
	{
		float d = -1f;
		if (_player != null && IsInstanceValid(_player))
		{
			d = new Vector2(_player.GlobalPosition.X - _cabin.GlobalPosition.X, _player.GlobalPosition.Z - _cabin.GlobalPosition.Z).Length();
			if (d > Radius * HeatReach + 1f) { Sleep(); return; }
		}

		_clock += delta;
		if (_glow != null && IsInstanceValid(_glow))
			_glow.LightEnergy = GlowEnergy * (0.85f + 0.1f * Mathf.Sin((float)_clock * 2.3f) + 0.05f * Mathf.Sin((float)_clock * 9.1f));
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
