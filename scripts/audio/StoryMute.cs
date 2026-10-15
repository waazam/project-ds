using Godot;

namespace ProjectDS.Audio;

/// <summary>
/// Every sound in the game cut to nothing, for a stretch of the story (2026-10-15, the owner: "all audio mutes once you
/// reach the staircase, right before the title and chapter title appear one after the other ... muted until the next
/// act"). An amplifier on the Master bus (the settings' master volume stays the settings'), faded down over
/// <see cref="FadeSeconds"/>. It lives as long as the node it was engaged on: when that level goes (the climb's blackout
/// into the Hollow, a load, quit to the menu) the effect comes off and the sound is back. Nothing can leave the game
/// muted.
/// </summary>
public partial class StoryMute : Node
{
	/// <summary>Muted now (or fading to it).</summary>
	public static bool On => _live != null && IsInstanceValid(_live) && _live.IsInsideTree();
	/// <summary>How far down it is (0 = untouched .. 1 = silent).</summary>
	public static float Depth => On ? _live._depth : 0f;

	public float FadeSeconds = 0.35f;
	private static StoryMute _live;
	private AudioEffectAmplify _fx;
	private float _depth;

	/// <summary>Mutes everything until <paramref name="owner"/> leaves the tree (once; a second call is the same mute).</summary>
	public static StoryMute Engage(Node owner, float fadeSeconds = 0.35f, string why = "")
	{
		if (On) return _live;
		var m = new StoryMute { Name = "StoryMute", FadeSeconds = fadeSeconds };
		owner.AddChild(m);
		GD.Print($"[audio] all sound cut{(why.Length > 0 ? $": {why}" : "")}");
		return m;
	}

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_live = this;
		int master = AudioServer.GetBusIndex("Master");
		if (master < 0) return;
		_fx = new AudioEffectAmplify { VolumeDb = 0f };
		AudioServer.AddBusEffect(master, _fx);
	}

	public override void _Process(double delta)
	{
		_depth = Mathf.MoveToward(_depth, 1f, (float)delta / Mathf.Max(FadeSeconds, 0.01f));
		if (_fx != null) _fx.VolumeDb = _depth >= 1f ? -80f : Mathf.Max(-80f, Mathf.LinearToDb(1f - _depth));
		if (_depth >= 1f) SetProcess(false);
	}

	public override void _ExitTree()
	{
		if (_live == this) _live = null;
		if (_fx == null) return;
		int master = AudioServer.GetBusIndex("Master");
		for (int i = AudioServer.GetBusEffectCount(master) - 1; master >= 0 && i >= 0; i--)
			if (AudioServer.GetBusEffect(master, i) == _fx) { AudioServer.RemoveBusEffect(master, i); break; }
		_fx = null;
		GD.Print("[audio] sound back");
	}
}
