using System.Collections.Generic;
using Godot;

namespace ProjectDS.Audio;

/// <summary>
/// Act 3's storm: once the player leaves the cabin's safe zone with the
/// lantern and compass in hand, the forest goes dead silent (the same signal
/// as the stairs), rain fades in, and lightning flashes with a rolling thunder
/// crack at random intervals. It runs until <see cref="Deactivate"/> is called
/// (Act 6's dawn breaks it), which is otherwise never automatic.
/// </summary>
public partial class StormController : Node
{
	public static StormController Instance { get; private set; }

	[Export] public NodePath RainPath = "../Rain";
	[Export] public NodePath ThunderPath = "../Thunder";
	[Export] public float RainFadeSeconds = 5f;
	[Export] public Vector2 LightningInterval = new(6f, 17f);

	public bool Active { get; private set; }

	private AmbienceLoop _rainLoop;
	private AudioStreamPlayer _thunderVoice;
	private readonly List<AudioStream> _thunder = new();
	private ColorRect _flash;
	private readonly RandomNumberGenerator _rng = new();
	private double _clock;
	private double _nextStrike;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		_rainLoop = GetNodeOrNull<AudioStreamPlayer>(RainPath)?.GetNodeOrNull<AmbienceLoop>("Loop");
		_thunderVoice = GetNodeOrNull<AudioStreamPlayer>(ThunderPath);
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
	}

	public void Activate()
	{
		if (Active) return;
		Active = true;
		ForestAmbienceManager.Instance.SilenceOverride = 1f;
		if (_rainLoop != null)
			CreateTween().TweenProperty(_rainLoop, "Gain", 1f, RainFadeSeconds);
	}

	/// <summary>Act 6's dawn: the storm breaks. Rain fades out, the forced silence lifts, no more lightning.</summary>
	public void Deactivate(float fadeSeconds = 6f)
	{
		if (!Active) return;
		Active = false;
		ForestAmbienceManager.Instance.SilenceOverride = -1f;
		if (_rainLoop != null)
			CreateTween().TweenProperty(_rainLoop, "Gain", 0f, fadeSeconds);
		var c = _flash.Color; c.A = 0f; _flash.Color = c;
	}

	public override void _Process(double delta)
	{
		if (!Active) return;
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
		_flash.Color = new Color(1f, 1f, 0.95f, _rng.RandfRange(0.18f, 0.4f));
		if (_thunder.Count == 0 || _thunderVoice == null) return;
		_thunderVoice.Stream = _thunder[_rng.RandiRange(0, _thunder.Count - 1)];
		_thunderVoice.VolumeDb = _rng.RandfRange(-9f, -1f);
		_thunderVoice.PitchScale = _rng.RandfRange(0.88f, 1.06f);
		_thunderVoice.Play();
	}
}
