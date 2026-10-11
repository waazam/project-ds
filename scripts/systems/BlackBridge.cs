using Godot;

namespace ProjectDS.Systems;

/// <summary>
/// The black between two levels, filled (2026-10-11, the owner's list: "Hide the Act 1 to 2 load inside the story ... the
/// climb up the stairs could keep going in the dark"). Put under the tree's root (not the level), so it outlives the change:
/// a loop plays on through the black while the next level reads and builds (it must play on by itself: while a level builds
/// nothing else runs), then, once the new level has handed the player control, it fades away over a few seconds. The
/// stairs: the climb in the dark (tools/audio/make_climb_loop.py). Elsewhere: the wind.
/// </summary>
public partial class BlackBridge : Node
{
	private AudioStreamPlayer _p;
	private Node _from;
	private double _clock;
	private bool _fading;

	public static void Begin(SceneTree tree, string loopPath, float db = -6f)
	{
		if (tree?.Root == null || !ResourceLoader.Exists(loopPath)) return;
		foreach (var c in tree.Root.GetChildren()) if (c is BlackBridge old) old.QueueFree();
		var b = new BlackBridge { Name = "BlackBridge", _from = tree.CurrentScene };
		tree.Root.AddChild(b);
		var st = GD.Load<AudioStream>(loopPath);
		if (st is AudioStreamWav wav) { wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward; wav.LoopBegin = 0; wav.LoopEnd = (int)(wav.GetLength() * wav.MixRate); }
		b._p = new AudioStreamPlayer { Stream = st, Bus = "Master", VolumeDb = -40f };
		b.AddChild(b._p);
		b._p.Play();
		b.CreateTween().TweenProperty(b._p, "volume_db", db, 1.2f);
		GD.Print($"[bridge] through the black: {System.IO.Path.GetFileName(loopPath)}");
	}

	public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

	public override void _Process(double delta)
	{
		_clock += delta;
		var scene = GetTree().CurrentScene;
		bool arrived = scene != null && scene != _from && IsInstanceValid(scene) && GameFlow.Instance is { Started: true } && GameFlow.Instance.IsInsideTree();
		if ((arrived || _clock > 90) && !_fading)
		{
			_fading = true;
			var tw = CreateTween();
			tw.TweenInterval(1.5);
			tw.TweenProperty(_p, "volume_db", -60f, 4.0f).SetTrans(Tween.TransitionType.Sine);
			tw.TweenCallback(Callable.From(QueueFree));
		}
	}
}
