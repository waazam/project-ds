using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 7's aftermath: once the cabin is seen burning, a voice starts calling
/// "come up and see" out of the forest at random intervals, in different
/// speeds and sometimes doubled like an overlapping echo, until the player
/// finds the bunker. Purely atmospheric — it never blocks or redirects.
///
/// Stateless beyond the checkpoint, so Continue restores it for free. Waits are
/// pausable; the harsh takes play on the always-distorted "VoiceHarsh" bus
/// (no shared effect is toggled).
/// </summary>
public partial class Act7Whispers : Node
{
	[Export] public Vector2 IntervalSeconds = new(14f, 34f);
	[Export] public Vector2 IntervalSecondsAutoTest = new(2f, 5f);

	/// <summary>For the autotest: how many whisper events have fired.</summary>
	public int WhisperCount { get; private set; }

	private PlayerController _player;
	private readonly RandomNumberGenerator _rng = new();

	public override void _Ready() => _ = Cutscene.Run(this, Loop);

	private static bool Active(StoryManager s) => s != null && s.Current >= Checkpoint.Act7CabinBurning && s.Current < Checkpoint.Act8BunkerEntered;

	private async Task Loop(CancellationToken ct)
	{
		while (true)
		{
			if (!Active(StoryManager.Instance)) { await Cutscene.Wait(this, 1.0, ct); continue; }
			var interval = GameSettings.Instance.AutoTest ? IntervalSecondsAutoTest : IntervalSeconds;
			await Cutscene.Wait(this, _rng.RandfRange(interval.X, interval.Y), ct);
			if (!Active(StoryManager.Instance)) continue;
			await FireWhisper(ct);
		}
	}

	private async Task FireWhisper(CancellationToken ct)
	{
		WhisperCount++;
		if (_player == null || !IsInstanceValid(_player)) _player = StoryBeat.Player(this);
		PlaySting();
		bool echo = _rng.Randf() < 0.3f;
		float fadeIn = _rng.RandfRange(0.8f, 2.2f), hold = _rng.RandfRange(1.6f, 3.4f), fadeOut = _rng.RandfRange(0.8f, 2.0f);
		if (StoryBeat.Fader(this) == null) return;
		if (!echo)
		{
			await StoryBeat.Caption(this, "\"Come up and see.\"", fadeIn, hold, fadeOut, ct);
			return;
		}
		// A second, overlapping voice a beat behind the first: the "multiplicity" the outline asks for.
		// It goes on the fader's echo line, so the two captions stand as two lines and neither cuts
		// the other short. Both come down with this sequence if it is cancelled.
		var first = StoryBeat.Caption(this, "\"Come up and see.\"", fadeIn, hold, fadeOut, ct);
		await Cutscene.Wait(this, _rng.RandfRange(0.3f, 0.7f), ct);
		PlaySting();
		await StoryBeat.Echo(this, "\"...come up and see...\"", fadeIn * 0.6f, hold * 0.7f, fadeOut, ct);
		await first;
	}

	// The four recorded takes read "close/quiet" to "far/harsh"; each gets its own baseline
	// presence, and pitch/volume/placement/distortion are re-rolled on top so no two plays —
	// even of the same take — land quite the same way.
	private static readonly string[] ComeAndSeeVariants = { "distant", "light", "medium", "loud" };

	private void PlaySting()
	{
		if (_player == null) return;
		string variant = ComeAndSeeVariants[_rng.RandiRange(0, ComeAndSeeVariants.Length - 1)];
		string path = $"res://assets/audio/voice/come_and_see_{variant}.mp3";
		bool recorded = ResourceLoader.Exists(path);
		if (!recorded) path = $"res://assets/audio/sfx/whisper_voice_{_rng.RandiRange(1, 3):00}.wav";
		if (!ResourceLoader.Exists(path)) return;

		(float baseDb, float unitSize, float maxDist, bool harsh) = variant switch
		{
			"distant" => (-8f, 9f, 100f, false),
			"light" => (-2f, 5f, 60f, false),
			"medium" => (3f, 4f, 55f, false),
			_ => (8f, 3f, 50f, true),   // "loud"
		};
		bool distorted = recorded && harsh && _rng.Randf() < 0.6f;
		float ang = _rng.RandfRange(0f, Mathf.Tau);
		float dist = _rng.RandfRange(9f, 24f);
		Vector3 pos = _player.GlobalPosition + new Vector3(Mathf.Cos(ang), 0.5f, Mathf.Sin(ang)) * dist;
		var voice = new AudioStreamPlayer3D
		{
			Stream = GD.Load<AudioStream>(path), Bus = distorted ? "VoiceHarsh" : "Voice",
			UnitSize = unitSize, MaxDistance = maxDist,
			VolumeDb = baseDb + _rng.RandfRange(-2f, 2f),
			PitchScale = _rng.RandfRange(0.82f, 1.18f),
		};
		// Must be parented before GlobalPosition is set, or Godot can't resolve the transform.
		Cutscene.SceneRoot(this).AddChild(voice);
		voice.GlobalPosition = pos;
		voice.Finished += voice.QueueFree;
		voice.Play();
	}
}
