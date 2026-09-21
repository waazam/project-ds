using Godot;

namespace ProjectDS.Audio;

/// <summary>
/// The low hum that comes off the stairs (STORY.md Act 2): silent until the
/// player is inside the stairs' silence radius, then about <see cref="EdgeDb"/>
/// at its edge rising to <see cref="CloseDb"/> at the stairs, following the same
/// proximity curve the stairs' SilenceZone gives the Pressure layer. A scripted
/// override (<see cref="SetOverrideDb"/>) lets Act 11 push it far louder during
/// the long climb, capped at <see cref="MaxDb"/>. Plays on the Unnatural bus.
/// </summary>
public partial class StairsHum : Node
{
	public static StairsHum Instance { get; private set; }

	[Export] public string StreamPath = "res://assets/audio/ambient/stairs_hum_loop.wav";
	[Export] public float EdgeDb = -30f;
	[Export] public float CloseDb = -14f;
	[Export] public float MaxDb = 6f;
	/// <summary>Seconds for the level to cover ~63% of a change.</summary>
	[Export] public float SmoothingTime = 0.5f;

	/// <summary>Current level in dB (-80 = silent), for tests.</summary>
	public float LevelDb { get; private set; } = -80f;

	private AmbienceLoop _loop;
	private SilenceZone _zone;
	private Node3D _listener;
	private float? _override;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		var player = new AudioStreamPlayer { Name = "Hum", Bus = "Unnatural" };
		AddChild(player);
		_loop = new AmbienceLoop { Name = "Loop", StreamPath = StreamPath, BaseVolumeDb = 0f, Gain = 0f };
		player.AddChild(_loop);
	}

	/// <summary>Forces the hum to <paramref name="db"/> (clamped to MaxDb) regardless of distance; null hands it back to proximity.</summary>
	public void SetOverrideDb(float? db) => _override = db.HasValue ? Mathf.Min(db.Value, MaxDb) : null;

	public override void _Process(double delta)
	{
		if (_listener == null || !IsInstanceValid(_listener))
			_listener = GetTree().GetFirstNodeInGroup("player") as Node3D;
		if (_zone == null || !IsInstanceValid(_zone))
			_zone = (GetTree().GetFirstNodeInGroup("stairs_top_trigger") as Node)?.GetParent()?.GetNodeOrNull<SilenceZone>("SilenceZone");

		float target = -80f;
		if (_override.HasValue) target = _override.Value;
		else if (_zone != null && _listener != null)
		{
			float prox = _zone.SilenceAt(_listener.GlobalPosition) / Mathf.Max(_zone.Strength, 0.01f);
			if (prox > 0.001f) target = Mathf.Lerp(EdgeDb, CloseDb, prox);
		}
		float k = 1f - Mathf.Exp(-(float)delta / Mathf.Max(SmoothingTime, 0.01f));
		LevelDb = Mathf.Lerp(LevelDb, target, k);
		if (_loop == null) return;
		// Gain carries the fade to silence; ExtraDb the level while audible.
		_loop.Gain = Mathf.Clamp(Mathf.DbToLinear(LevelDb - EdgeDb + 6f), 0f, 1f);
		_loop.ExtraDb = Mathf.Max(LevelDb, EdgeDb - 6f);
	}
}
