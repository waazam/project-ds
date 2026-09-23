using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// RETIRED (see <see cref="Active"/>): formerly Act 7's aftermath, a voice calling
/// "come up and see" out of the forest at random intervals on the walk to the
/// bunker. The voice now belongs to the stairs alone (Act 6's loop; the last
/// staircase). Kept, inert, so the scene node and its references still load.
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

	// Retired (Dan, 2026-09-22): "come up and see" is only heard where staircases stand (the Act 6
	// clearing's loop, once or twice on the last staircase), never on the walk to the bunker. The
	// node stays in the scene so saves, previews and the autotest keep their references; it never fires.
	private static bool Active(StoryManager s) => false;

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
		// No caption: the voice is heard, never read (Dan, 2026-09-22: only the radio's lines go on screen).
		if (!echo) return;
		// A second, overlapping voice a beat behind the first: the "multiplicity" the outline asks for.
		await Cutscene.Wait(this, _rng.RandfRange(0.3f, 0.7f), ct);
		PlaySting();
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
			"distant" => (-14f, 9f, 100f, false),
			"light" => (-9f, 5f, 60f, false),
			"medium" => (-5f, 4f, 55f, false),
			_ => (-1f, 3f, 50f, true),   // "loud" (the take itself is loud; it must sit inside the mix, not explode out of it)
		};
		bool distorted = recorded && harsh && _rng.Randf() < 0.35f;
		float ang = _rng.RandfRange(0f, Mathf.Tau);
		float dist = _rng.RandfRange(9f, 24f);
		Vector3 pos = _player.GlobalPosition + new Vector3(Mathf.Cos(ang), 0.5f, Mathf.Sin(ang)) * dist;
		var voice = new AudioStreamPlayer3D
		{
			Stream = GD.Load<AudioStream>(path), Bus = distorted ? "VoiceHarsh" : "Voice",
			UnitSize = unitSize, MaxDistance = maxDist,
			VolumeDb = baseDb + _rng.RandfRange(-2f, 2f),
			PitchScale = _rng.RandfRange(0.8f, 0.97f),   // never above the take: every voice stays low (the Voice bus deepens them further)
		};
		// Must be parented before GlobalPosition is set, or Godot can't resolve the transform.
		Cutscene.SceneRoot(this).AddChild(voice);
		voice.GlobalPosition = pos;
		voice.Finished += voice.QueueFree;
		voice.Play();
		StoryBeat.FadeIn(voice, voice.VolumeDb, 0.5f);   // it comes up out of the trees, never bursts in
	}
}
