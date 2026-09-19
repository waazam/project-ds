using Godot;
using ProjectDS.Entities;

namespace ProjectDS.Audio;

/// <summary>
/// The score: one deep, sustained, unbroken bed from the trailhead to the
/// stairs. There are no notes, bells or attacks. It has three layers:
/// - Pad: a dark low chord (E, B and a sour F) under a slowly moving filter.
/// - Hum: a bowed-metal drone on the same low E.
/// - Shimmer: metallic upper partials, the only part that really grows with
///   intensity.
///
/// Intensity rises with how deep into the woods you are and with the stalker's
/// tension. It adds weight and brings in the shimmer, but the bed is present
/// from the first step. When the silence around a staircase takes hold, the
/// score cuts out almost instantly, and it only creeps back slowly once you
/// leave. Everything runs on the Music bus, set well under the forest.
/// </summary>
public partial class MusicDirector : Node
{
	[Export] public float DepthMeters = 450f;       // distance from the trailhead that counts as "deepest"
	[Export] public float TensionWeight = 0.25f;
	[Export] public float IntensitySmoothing = 6f;  // seconds
	/// <summary>Above this forest silence the score cuts out.</summary>
	[Export] public float CutAtSilence = 0.55f;
	[Export] public float CutSeconds = 0.25f;
	[Export] public float ReturnSeconds = 8f;

	public float Intensity { get; private set; }
	public float Cut { get; private set; } = 1f;

	private AmbienceLoop _pad, _hum, _shimmer;
	private Node3D _player, _spawn;
	private Stalker _stalker;

	public override void _Ready()
	{
		_pad = GetNodeOrNull<AmbienceLoop>("Pad/Loop");
		_hum = GetNodeOrNull<AmbienceLoop>("Hum/Loop");
		_shimmer = GetNodeOrNull<AmbienceLoop>("Shimmer/Loop");
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
		_spawn ??= GetTree().GetFirstNodeInGroup("player_spawn") as Node3D;
		_stalker ??= GetTree().Root.FindChild("Stalker", true, false) as Stalker;
		if (_player == null) return;

		// How deep, how hunted.
		float depth = _spawn == null ? 0f
			: Mathf.Clamp(new Vector2(_player.GlobalPosition.X - _spawn.GlobalPosition.X, _player.GlobalPosition.Z - _spawn.GlobalPosition.Z).Length() / DepthMeters, 0f, 1f);
		float target = Mathf.Clamp(0.1f + 0.65f * depth + TensionWeight * (_stalker?.Tension ?? 0f), 0f, 1f);
		Intensity = Mathf.Lerp(Intensity, target, 1f - Mathf.Exp(-dt / IntensitySmoothing));

		// The silence takes it away at once; it only creeps back.
		float silence = ForestAmbienceManager.Instance?.Silence ?? 0f;
		Cut = silence > CutAtSilence
			? Mathf.Max(0f, Cut - dt / CutSeconds)
			: Mathf.Min(1f, Cut + dt / ReturnSeconds);

		if (_pad != null) _pad.Gain = (0.75f + 0.25f * Intensity) * Cut;
		if (_hum != null) _hum.Gain = (0.7f + 0.3f * Intensity) * Cut;
		if (_shimmer != null) _shimmer.Gain = Mathf.SmoothStep(0.4f, 0.9f, Intensity) * Cut;
	}
}
