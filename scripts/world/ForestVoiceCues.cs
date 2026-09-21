using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Once the real horror has started (the stairs found), a "turn around" voice
/// line is sprinkled in at rare, random moments while the player is still out
/// walking the forest — gone again once they're indoors in the bunker. Each
/// play is re-pitched, re-placed and given its own volume so it never reads
/// as a looped sting.
/// </summary>
public partial class ForestVoiceCues : Node
{
	[Export] public Vector2 IntervalSeconds = new(50f, 130f);
	[Export] public Vector2 IntervalSecondsAutoTest = new(4f, 9f);

	/// <summary>For the autotest: how many "turn around" cues have fired.</summary>
	public int TurnAroundCount { get; private set; }

	private PlayerController _player;
	private readonly RandomNumberGenerator _rng = new();

	public override void _Ready() => _ = Loop();

	private bool Active(StoryManager s) => s != null && s.Current >= Checkpoint.Act2StairsClimbed && s.Current < Checkpoint.Act8BunkerEntered;

	private async Task Loop()
	{
		while (true)
		{
			if (!Active(StoryManager.Instance)) { await Wait(1.0); continue; }
			var interval = GameSettings.Instance.AutoTest ? IntervalSecondsAutoTest : IntervalSeconds;
			await Wait(_rng.RandfRange(interval.X, interval.Y));
			if (!Active(StoryManager.Instance)) continue;
			PlayCue();
		}
	}

	private void PlayCue()
	{
		_player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
		if (_player == null) return;
		string path = "res://assets/audio/voice/turn_around_distant.mp3";
		if (!ResourceLoader.Exists(path)) return;
		TurnAroundCount++;

		// Behind the player, roughly — where a whispered "turn around" actually lands.
		float behindYaw = _player.CameraRig.Yaw + Mathf.Pi + _rng.RandfRange(-0.6f, 0.6f);
		float dist = _rng.RandfRange(7f, 16f);
		Vector3 pos = _player.GlobalPosition + new Vector3(Mathf.Sin(-behindYaw), 0.4f, -Mathf.Cos(-behindYaw)) * dist;
		var voice = new AudioStreamPlayer3D
		{
			Stream = GD.Load<AudioStream>(path), Bus = "Voice",
			UnitSize = _rng.RandfRange(5f, 9f), MaxDistance = 70f,
			VolumeDb = _rng.RandfRange(-3f, 5f),
			PitchScale = _rng.RandfRange(0.8f, 1.2f),
		};
		// Must be parented before GlobalPosition is set, or Godot can't resolve the transform.
		GetTree().Root.AddChild(voice);
		voice.GlobalPosition = pos;
		voice.Finished += voice.QueueFree;
		voice.Play();
	}

	private async Task Wait(double seconds) => await ToSignal(GetTree().CreateTimer(seconds, true, true), SceneTreeTimer.SignalName.Timeout);
}
