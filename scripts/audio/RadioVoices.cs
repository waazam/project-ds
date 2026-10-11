using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.Audio;

/// <summary>
/// The radio picks things up (2026-10-10b). Carrying the walkie, once it has woken (after the bunker), now and then when
/// something is near and unseen (the same nearness as <see cref="DreadDrone"/>), it clicks on by itself: a burst of
/// static, and through it a voice. In the winter it's the wendigo's stolen voices; anywhere else, a whisper too broken to
/// make out. Then static, and it's off. Rare (two minutes apart at the least, and only a chance each time), never in a
/// cutscene, and never twice the same voice in a row.
/// </summary>
public partial class RadioVoices : Node
{
	public int Heard { get; private set; }
	private PlayerController _player;
	private double _nextAt = 60, _clock, _poll;
	private int _lastVoice = -1;
	private bool _busy;

	public override void _Ready() => _player = GetParent() as PlayerController;

	public override void _Process(double delta)
	{
		_clock += delta;
		_poll -= delta;
		if (_poll > 0 || _busy || _player == null) return;
		_poll = 2.0;
		if (_clock < _nextAt) return;
		var inv = _player.GetNodeOrNull<PlayerInventory>("Inventory");
		var s = StoryManager.Instance;
		if (inv is not { HasRadio: true } || s == null || s.Current < Checkpoint.Act11GiantEncounter) return;
		if (!_player.PlayerInput.Enabled || PlayerDeath.Dying) return;
		float dread = _player.GetNodeOrNull<DreadDrone>("Dread")?.Target ?? 0f;
		bool force = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--radio-voices") >= 0;
		if (!force && (dread < 0.25f || GD.Randf() > 0.3f)) return;
		_nextAt = _clock + GD.RandRange(120.0, 240.0);
		_ = Transmit(s.Current >= Checkpoint.Act21Finished);
	}

	private async System.Threading.Tasks.Task Transmit(bool winter)
	{
		_busy = true;
		Heard++;
		GD.Print($"[radio] it picks something up ({(winter ? "a stolen voice" : "a whisper")})");
		Play("radio_tick", 4, -10f, 1f);
		await ToSignal(GetTree().CreateTimer(0.25), SceneTreeTimer.SignalName.Timeout);
		Play("radio_burst", 2, -8f, 1f);
		await ToSignal(GetTree().CreateTimer(0.5), SceneTreeTimer.SignalName.Timeout);
		// (2026-10-11) one time in four, no voice: the lullaby, faint, as if from a far station (the lodge's music box's tune)
		bool lullaby = GD.Randf() < 0.25f && _lastVoice != -1;
		if (lullaby)
		{
			_lastVoice = -1;
			Play("radio_lullaby", 0, -10f, 1f);
			await ToSignal(GetTree().CreateTimer(9.0), SceneTreeTimer.SignalName.Timeout);
		}
		else
		{
			int n = winter ? 14 : 3, v;
			do v = (int)(GD.Randi() % n) + 1; while (v == _lastVoice && n > 1);
			_lastVoice = v;
			Play(winter ? $"wendigo_mimic_{v:00}" : $"whisper_voice_{v:00}", 0, winter ? -6f : -4f, winter ? 1f : 0.92f);
			await ToSignal(GetTree().CreateTimer(2.6), SceneTreeTimer.SignalName.Timeout);
		}
		Play("radio_burst", 2, -12f, 1.1f);
		Play("radio_tick", 4, -12f, 0.9f);
		PlayerBreathing.Startle(0.5f);
		_busy = false;
	}

	private void Play(string name, int variants, float db, float pitch)
	{
		string path = variants > 0 ? $"res://assets/audio/sfx/{name}_{GD.RandRange(1, variants):00}.wav" : $"res://assets/audio/sfx/{name}.wav";
		if (!ResourceLoader.Exists(path)) return;
		var p = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(path), Bus = "Radio", VolumeDb = db, PitchScale = pitch };
		AddChild(p);
		p.Finished += p.QueueFree;
		p.Play();
	}
}
