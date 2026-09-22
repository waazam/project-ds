using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 7: once the clearing's business is done, the cabin the player left
/// behind is fully engulfed when they finally get back. The checkpoint fires
/// the moment the player comes within <see cref="Radius"/>, close enough to see it.
///
/// This is the beat's logic only: the flames, smoke, embers and glow are the
/// cabin's own (<c>Cabin.SetBurning</c>). This adds the fire's sound (a crackle
/// bed and the odd structural groan, on the Events bus), warms the screen's
/// shadow tint the closer the player stands, and says the one line the sight
/// gets (<see cref="StillInThereLine"/>, <see cref="LineDelay"/> after the
/// checkpoint; never on Continue).
///
/// The per-frame work (groans, heat tint) only runs while the player is within
/// <see cref="HeatReach"/> x <see cref="Radius"/> of the cabin, where the tint
/// is non-zero: a wide trigger wakes it and leaving it puts it to sleep, so the
/// bunker and the ending pay nothing for a fire 3 km away.
///
/// Restore: from Act 7 on, the cabin is simply burning when the level loads
/// (the story never shows it burning out).
/// </summary>
public partial class CabinFireEvent : Node
{
	[Export] public NodePath CabinPath = "..";
	[Export] public float Radius = 30f;
	/// <summary>Multiple of <see cref="Radius"/> beyond which the heat tint is zero and processing stops.</summary>
	[Export] public float HeatReach = 3f;
	[Export] public string StillInThereLine = "He's still in there.";
	[Export] public float LineDelay = 2f;

	private Cabin _cabin;
	private Area3D _zone;
	private Area3D _heatZone;
	private PlayerController _player;
	private bool _burning;
	private ShaderMaterial _postMat;
	private Color _postBaseTint;
	private double _clock;
	private double _nextGroan;
	private readonly RandomNumberGenerator _rng = new();

	/// <summary>For tests: whether the cabin is on fire.</summary>
	public bool Burning => _burning;

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
		// Deferred: the cabin (our parent) is still readying its children during _Ready.
		_zone = StoryBeat.MakeTrigger(_cabin, new CylinderShape3D { Radius = Radius, Height = 80f }, Vector3.Zero, OnZoneEntered, "FireSightZone");
		if (StoryManager.Instance is { Current: >= Checkpoint.Act7CabinBurning }) Ignite();
	}

	private static bool CanFire(StoryManager s) => s is { ClearingVoiceHeard: true } && s.Current < Checkpoint.Act7CabinBurning;

	// The voice can be heard while already standing in range only in theory; re-check anyway.
	private void OnFlag(string _)
	{
		if (StoryBeat.PlayerInside(_zone) is { } p) OnZoneEntered(p);
	}

	private void OnZoneEntered(PlayerController player)
	{
		if (_burning || !CanFire(StoryManager.Instance)) return;
		Ignite();
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act7CabinBurning);
		GD.Print("[story] Act 7: the cabin is burning");
		// P13: a beat after the sight has landed, the one thing there is to say. Only here, never on a restore.
		Cutscene.Run(this, async ct =>
		{
			await Cutscene.Wait(this, LineDelay, ct);
			await StoryBeat.Caption(this, StillInThereLine, 0.8f, 2.8f, 1.0f);
		});
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

		_postMat = StoryBeat.PostMaterial(this);
		if (_postMat != null) _postBaseTint = (Color)_postMat.GetShaderParameter("shadow_tint");
		_nextGroan = _rng.RandfRange(2f, 6f);
		// Wake-up zone: inside it the groans and the heat tint run every frame; outside, nothing does.
		_heatZone = StoryBeat.MakeTrigger(_cabin, new CylinderShape3D { Radius = Radius * HeatReach, Height = 120f }, Vector3.Zero, _ => SetProcess(true), "FireHeatZone");
		_heatZone.BodyExited += b => { if (b is PlayerController) Sleep(); };
		SetProcess(true);   // the first frame measures for itself: the player may already stand inside, or 3 km away
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
