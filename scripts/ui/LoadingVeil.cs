using Godot;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The black between two levels, held as one piece (2026-10-15, the owner: "transition between act 1 and 2 needs to be
/// fixed with a loading screen or something"). What it was: after the collapse the old level went and, before the Hollow
/// was up, a frame of the world's flat grey clear colour was drawn, and that grey frame then sat frozen on screen for the
/// whole build (7.5 s warm, 22.5 s with a cold texture cache); the Hollow's rain and wind came in at full level while
/// it was still black.
///
/// Now: a veil under the tree's root (it outlives the change), over every level layer (the fader, the chapter cards, the
/// HUD; under the save indicator, which still turns while it saves), shown before the change: black, with a faint
/// tape's look, a little grain and a soft tracking band drifting down, like a VHS left playing on a dark screen (still,
/// it is a paused tape, not a hang). While it's up the window keeps answering (<see cref="LoadPump"/>). It stays until
/// the new level is built, its shaders warmed and its flow begun, then thins away over the black the level holds for the
/// wake. With <c>mute</c> it also holds every sound silent through the load and lets it back in slowly once the level is
/// ready (hearing comes back before sight), instead of the new level's sounds starting at full under the black.
///
/// The game's title (STORY.md Rev 5, 2026-10-16): the first time the veil carries the player into the Hollow after the
/// first climb, DEAD SILENT comes up on it (<see cref="ChapterCard.TitleLabel"/>) and hides the load. It holds at least
/// long enough to read, however quick the load; once the level is ready it clears first, then the veil thins to the
/// level's black and GameFlow's wake opens the eyes. Once a playthrough (the title's flag, <c>ChapterCard.Flag(0)</c>).
/// </summary>
public partial class LoadingVeil : CanvasLayer
{
	public const int VeilLayer = 60;
	public static LoadingVeil Current { get; private set; }
	/// <summary>For tests: the last veil's timings (ms from shown to released) and how often the window was pumped.</summary>
	public static ulong LastHeldMs { get; private set; }

	public float FadeOutSeconds = 0.9f;
	public float SoundInSeconds = 6f;
	// the title's timing: black first, its fade in, the least it is up, its fade out
	public float TitleLead = 1.2f, TitleIn = 1.6f, TitleMinUp = 4.2f, TitleOut = 1.5f;

	/// <summary>True while this veil carries the game's title.</summary>
	public bool HasTitle => _titleLabel != null;
	private Label _titleLabel;
	private Control _titleRoot;
	private float TitleSeconds => (Time.GetTicksMsec() - _shownAt) / 1000f;

	private Node _from;
	private ColorRect _rect;
	private ShaderMaterial _mat;
	private bool _mute, _released;
	private AudioEffectAmplify _amp;
	private ulong _shownAt;

	public static LoadingVeil Show(SceneTree tree, bool mute)
	{
		if (tree?.Root == null) return null;
		if (Current != null && IsInstanceValid(Current)) Current.Finish();
		var v = new LoadingVeil { Name = "LoadingVeil", Layer = VeilLayer, _from = tree.CurrentScene, _mute = mute };
		tree.Root.AddChild(v);
		return v;
	}

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Current = this;
		_shownAt = Time.GetTicksMsec();
		_mat = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
		_mat.SetShaderParameter("alpha", 1f);
		_rect = new ColorRect { Color = Colors.Black, Material = _mat, MouseFilter = Control.MouseFilterEnum.Ignore };
		_rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_rect);
		// the game's title, the first time into the Hollow from the first climb
		if (StoryManager.Instance is { Current: Checkpoint.Act2StairsClimbed } s && !s.HasFlag(ChapterCard.Flag(0)))
		{
			s.SetFlag(ChapterCard.Flag(0));
			if (GameSettings.Instance is { AutoTest: true } && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--real-timing") < 0)
			{ TitleLead = 0f; TitleIn = 0.2f; TitleMinUp = 0.3f; TitleOut = 0.2f; }
			_titleRoot = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
			_titleRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			AddChild(_titleRoot);
			_titleLabel = ChapterCard.TitleLabel();
			_titleRoot.AddChild(_titleLabel);
			ChapterCard.Shown.Add(0);
			GD.Print("[story] title: DEAD SILENT, over the load into the Hollow");
		}
		if (_mute)
		{
			int master = AudioServer.GetBusIndex("Master");
			if (master >= 0) { _amp = new AudioEffectAmplify { VolumeDb = -80f }; AudioServer.AddBusEffect(master, _amp); }
		}
		LoadPump.Begin();
		GD.Print($"[veil] up{(_mute ? ", sound held" : "")}");
	}

	public override void _Process(double delta)
	{
		if (_titleLabel != null)
		{
			// (never a jump: it fades up out of the black and drifts very slightly larger while it holds)
			float tt = TitleSeconds - TitleLead;
			if (!_released) _titleRoot.Modulate = new Color(1, 1, 1, Mathf.SmoothStep(0f, 1f, Mathf.Clamp(tt / TitleIn, 0f, 1f)));
			float grow = 1f + Mathf.Min(0.05f, 0.004f * Mathf.Max(tt, 0f));
			_titleLabel.PivotOffset = _titleLabel.Size * 0.5f;
			_titleLabel.Scale = new Vector2(grow, grow);
		}
		if (_released) return;
		LoadPump.Frame();
		var scene = GetTree().CurrentScene;
		bool arrived = scene != null && scene != _from && IsInstanceValid(scene) && GameFlow.Instance is { Started: true } gf
			&& gf.IsInsideTree() && ShaderWarmup.Ready;
		if (arrived || Time.GetTicksMsec() - _shownAt > 120_000) Release();
	}

	/// <summary>The level's ready: the veil thins away (the level's own black is under it), the sound comes back in.</summary>
	public void Release()
	{
		if (_released) return;
		// the title stays up at least long enough to read (it asks again each frame)
		if (_titleLabel != null && TitleSeconds < TitleLead + TitleIn + TitleMinUp && Time.GetTicksMsec() - _shownAt < 120_000) return;
		_released = true;
		LoadPump.End();
		LastHeldMs = Time.GetTicksMsec() - _shownAt;
		GD.Print($"[veil] down after {LastHeldMs} ms ({LoadPump.Pumps} window pumps, the window unanswered at most {LoadPump.MaxGapMs} ms)");
		var tw = CreateTween().SetParallel();
		// the title clears first, then the veil thins to the level's own black
		float titleOut = 0f;
		if (_titleRoot != null)
		{
			titleOut = TitleOut;
			tw.TweenProperty(_titleRoot, "modulate:a", 0f, TitleOut).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		}
		tw.TweenMethod(Callable.From<float>(a => _mat.SetShaderParameter("alpha", a)), 1f, 0f, FadeOutSeconds)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In).SetDelay(titleOut);
		if (_amp != null)
			tw.TweenMethod(Callable.From<float>(u => { if (_amp != null) _amp.VolumeDb = u >= 1f ? 0f : Mathf.Max(-80f, Mathf.LinearToDb(u * u)); }),
				0f, 1f, SoundInSeconds).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		tw.Chain().TweenCallback(Callable.From(Finish));
	}

	private void Finish()
	{
		LoadPump.Active = false;
		if (_amp != null)
		{
			int master = AudioServer.GetBusIndex("Master");
			for (int i = AudioServer.GetBusEffectCount(master) - 1; master >= 0 && i >= 0; i--)
				if (AudioServer.GetBusEffect(master, i) == _amp) { AudioServer.RemoveBusEffect(master, i); break; }
			_amp = null;
		}
		if (Current == this) Current = null;
		QueueFree();
	}

	public override void _ExitTree()
	{
		// (never leave the sound held or the pump on)
		if (_amp != null) Finish();
		if (Current == this) { Current = null; LoadPump.Active = false; }
	}

	private const string ShaderCode = @"
shader_type canvas_item;
uniform float alpha = 1.0;
float h(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }
void fragment() {
	vec2 uv = SCREEN_UV;
	float t = TIME;
	// grain, a little: the tape's own noise on black
	float g = h(floor(uv * vec2(480.0, 270.0)) + floor(t * 24.0));
	// a soft tracking band drifting down the screen, broken into short streaks
	float by = fract(t * 0.045);
	float d = abs(uv.y - by);
	float band = smoothstep(0.03, 0.0, d);
	float streak = step(0.55, h(vec2(floor(uv.x * 90.0 + h(vec2(floor(uv.y * 200.0), floor(t * 20.0))) * 7.0), floor(uv.y * 200.0))));
	// the screen's edge falling off, and a slow breath in the dark
	vec2 c = uv - 0.5;
	float vig = 1.0 - smoothstep(0.25, 0.75, length(c * vec2(1.6, 1.0)));
	float breath = 0.75 + 0.25 * sin(t * 0.9);
	float v = (g * 0.016 + band * streak * 0.05 * breath) * vig;
	COLOR = vec4(vec3(v * 0.95, v, v * 1.05), alpha);
}";
}
