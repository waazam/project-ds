using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.Audio;

/// <summary>
/// Act 3's storm: once the player leaves the cabin's safe zone with the
/// lantern and compass in hand, the forest goes dead silent (the same signal
/// as the stairs), rain fades in, and lightning flashes with a rolling thunder
/// crack at random intervals. It runs until <see cref="Deactivate"/> is called
/// (Act 5's dawn breaks it), which is otherwise never automatic.
///
/// Restores itself on Continue: raging if the story has
/// <see cref="StoryManager.Flag.StormStarted"/> but not yet
/// <see cref="StoryManager.Flag.DawnBroke"/>.
///
/// Visuals: the rain and the lightning light come from <see cref="World.RainVfx"/>
/// (Intensity 0..1 is tweened with the rain; Flash(strength) on each strike);
/// this keeps only a faint full-screen overlay on top, which Reduce Flashing turns off.
/// Sounds: rain and thunder on the Weather bus.
/// </summary>
public partial class StormController : Node
{
	public static StormController Instance { get; private set; }

	[Export] public NodePath RainPath = "../Rain";
	[Export] public NodePath ThunderPath = "../Thunder";
	[Export] public float RainFadeSeconds = 5f;
	[Export] public Vector2 LightningInterval = new(6f, 17f);
	/// <summary>Peak alpha of the full-screen overlay flash (the scene itself is lit by RainVfx).</summary>
	[Export] public Vector2 OverlayAlpha = new(0.04f, 0.08f);
	/// <summary>Silence priority: the storm's hush yields to scripted set pieces (Act 11).</summary>
	[Export] public int SilencePriority = 10;

	public bool Active { get; private set; }

	private AmbienceLoop _rainLoop;
	private AudioStreamPlayer _thunderVoice;
	private readonly List<AudioStream> _thunder = new();
	private ColorRect _flash;
	private readonly RandomNumberGenerator _rng = new();
	private double _clock;
	private double _nextStrike;
	private Tween _rainTween;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
		ForestAmbienceManager.Instance?.ReleaseSilence(this);
	}

	public override void _Ready()
	{
		var rain = GetNodeOrNull<AudioStreamPlayer>(RainPath);
		if (rain != null) rain.Bus = "Weather";
		_rainLoop = rain?.GetNodeOrNull<AmbienceLoop>("Loop");
		_thunderVoice = GetNodeOrNull<AudioStreamPlayer>(ThunderPath);
		if (_thunderVoice != null) _thunderVoice.Bus = "Weather";
		for (int i = 1; i <= 3; i++)
		{
			string p = $"res://assets/audio/sfx/thunder_{i:00}.wav";
			if (ResourceLoader.Exists(p)) _thunder.Add(GD.Load<AudioStream>(p));
		}
		var layer = new CanvasLayer { Layer = 25 };
		AddChild(layer);
		_flash = new ColorRect { Color = new Color(1f, 1f, 0.95f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
		_flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(_flash);
		_nextStrike = _rng.RandfRange(LightningInterval.X, LightningInterval.Y);
		SetProcess(false);
		Callable.From(Restore).CallDeferred();
	}

	private void Restore()
	{
		if (StoryBeat.StormShouldRage(StoryManager.Instance)) Activate(0.1f);
	}

	public void Activate() => Activate(RainFadeSeconds);

	public void Activate(float fadeSeconds)
	{
		if (Active) return;
		Active = true;
		SetProcess(true);
		ForestAmbienceManager.Instance?.RequestSilence(this, 1f, SilencePriority);
		FadeRain(1f, fadeSeconds);
	}

	/// <summary>Act 5's dawn: the storm breaks. Rain fades out, the forced silence lifts, no more lightning.</summary>
	public void Deactivate(float fadeSeconds = 6f)
	{
		if (!Active) return;
		Active = false;
		SetProcess(false);
		ForestAmbienceManager.Instance?.ReleaseSilence(this);
		FadeRain(0f, fadeSeconds);
		var c = _flash.Color; c.A = 0f; _flash.Color = c;
	}

	private void FadeRain(float to, float seconds)
	{
		_rainTween?.Kill();
		_rainTween = CreateTween().SetParallel();
		if (_rainLoop != null) _rainTween.TweenProperty(_rainLoop, "Gain", to, seconds);
		if (World.RainVfx.Instance is { } vfx)
			_rainTween.TweenProperty(vfx, "Intensity", to, seconds);
	}

	public override void _Process(double delta)
	{
		_clock += delta;
		if (_flash.Color.A > 0f)
		{
			var c = _flash.Color;
			c.A = Mathf.MoveToward(c.A, 0f, (float)delta * 2.2f);
			_flash.Color = c;
		}
		if (_clock >= _nextStrike) Strike();
	}

	private void Strike()
	{
		_nextStrike = _clock + _rng.RandfRange(LightningInterval.X, LightningInterval.Y);
		// "Distant but bright": the flash reads clearly even though the strike sounds far off.
		float strength = _rng.RandfRange(0.6f, 1.1f);
		var vfx = World.RainVfx.Instance;
		bool litByVfx = vfx != null;
		vfx?.Flash(strength);   // lights the scene; honours ReduceFlashing itself
		bool reduce = GameSettings.Instance?.ReduceFlashing ?? false;
		// Without the scene lighting the overlay is the whole flash, so it keeps its old strength;
		// Reduce Flashing leaves only a faint, slow glow (or nothing, when the scene is lit anyway).
		Vector2 range = litByVfx ? OverlayAlpha : new Vector2(0.18f, 0.4f);
		float alpha = Mathf.Lerp(range.X, range.Y, Mathf.Clamp((strength - 0.6f) / 0.5f, 0f, 1f));
		if (reduce) alpha = litByVfx ? 0f : 0.04f;
		if (alpha > 0f) _flash.Color = new Color(1f, 1f, 0.95f, alpha);
		if (_thunder.Count == 0 || _thunderVoice == null) return;
		_thunderVoice.Stream = _thunder[_rng.RandiRange(0, _thunder.Count - 1)];
		_thunderVoice.VolumeDb = _rng.RandfRange(-9f, -1f);
		_thunderVoice.PitchScale = _rng.RandfRange(0.88f, 1.06f);
		_thunderVoice.Play();
	}
}
