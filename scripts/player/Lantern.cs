using Godot;

namespace ProjectDS.Player;

/// <summary>
/// A hand lantern the player equips from the cabin porch in Act 3: a radial
/// light (STORY.md Act 3) that rides the camera, with a living flame flicker.
/// Holding Focus (right mouse — the same button that zooms the camera) gathers
/// the light into a forward beam that throws further, like shuttering the glass.
/// [F] (PlayerInput.LightPressed) toggles it on and off.
///
/// [B] (PlayerInput.LanternModePressed) switches it to its second light, a blacklight (the owner's
/// mechanic): a steady violet cone that helps little to see by, but shows what was written in the ink
/// only it lights up (<see cref="World.UvInk"/>). R.H.'s code numbers on the trees, marks and prints
/// that aren't his, footprints to the bunker. While it's on, the beam's place and direction are
/// published as the shader globals uv_light_pos/dir/on/range (and as <see cref="Uv"/> for code that
/// needs to know what it is lighting).
/// </summary>
public partial class Lantern : Node3D
{
	[ExportGroup("Radial glow")]
	[Export] public float GlowRange = 14f;   // the owner: further, so more can be seen (was 9.5; the energy is eased to keep the near light as it was)
	[Export] public float GlowEnergy = 1.35f;   // was 1.5 at the shorter range
	[Export] public float GlowAttenuation = 1.3f;
	/// <summary>How much of the glow remains while the beam is focused.</summary>
	[Export] public float GlowWhileFocused = 0.55f;
	[Export] public Color LightColor = new(1.0f, 0.72f, 0.42f);

	[ExportGroup("Focused beam")]
	[Export] public float BeamRange = 24f;   // was 13 (the owner: throw it further)
	[Export] public float BeamEnergy = 2.5f;   // was 2.2, 2.6, 2.9
	[Export] public float BeamAngle = 35f;
	[Export] public float Sharpness = 6f;

	[ExportGroup("Flicker")]
	[Export] public float FlickerAmount = 0.06f;
	/// <summary>Chance per second of a brief dip.</summary>
	[Export] public float DipChance = 0.15f;
	[Export] public float DipLevel = 0.85f;

	/// <summary>0..1 how far the beam is gathered right now (right mouse held). Survey stakes read only above ~0.5.</summary>
	public float FocusAmount => _focus;
	/// <summary>Carried and switched on.</summary>
	public bool IsOn => _inv != null && _inv.HasLantern && _lit;

	[ExportGroup("Blacklight")]
	[Export] public Color UvColor = new(0.38f, 0.12f, 1.0f);
	[Export] public float UvRange = 9f;
	[Export] public float UvEnergy = 3.2f;   // enough violet on the bark and ground to see where it points (the polish pass: 2.6 left the stairwell black)
	/// <summary>Half-angle of the violet cone (degrees).</summary>
	[Export] public float UvAngle = 24f;
	[Export] public float UvGlowEnergy = 0.55f;   // a little violet round you: with the flame dead (Acts 14-20) it's all there is: enough to see the steps (the polish pass)

	/// <summary>The blacklight is the lantern's mode (on or off, it keeps the mode).</summary>
	public bool Blacklight { get; private set; }
	/// <summary>The blacklight, as it is right now: on, where from, which way, how far, and its cone's cosine.</summary>
	public static (bool on, Vector3 pos, Vector3 dir, float range, float cosOuter) Uv { get; private set; }

	/// <summary>For tests and restores: set the mode directly.</summary>
	public void SetBlacklight(bool on) { Blacklight = on; }

	/// <summary>The flame is dead (the owner: it gutters out a quarter of the way down Act 14's stairwell and
	/// doesn't work again until after Act 20). F only clicks; the blacklight still works.</summary>
	public static bool FlameDead => Systems.StoryManager.Instance is { } s
		&& (s.HasFlag(Systems.StoryManager.Flag.LanternFlameDead) || s.Current >= Systems.Checkpoint.Act14Finished)
		&& s.Current < Systems.Checkpoint.Act20Finished;

	private double _dyingUntil = -1;
	/// <summary>For tests: any light is coming from it right now (flame or blacklight).</summary>
	public bool Shining => _glow != null && _glow.Visible && _glow.LightEnergy > 0.01f;
	/// <summary>For tests: the flame is guttering out right now.</summary>
	public bool Dying => _t < _dyingUntil;

	/// <summary>The flame gutters and dies: a few seconds of sputtering, weaker and weaker, then out (and saved).</summary>
	public void KillFlame(float seconds = 3.2f)
	{
		if (FlameDead && !Dying) return;
		Systems.StoryManager.Instance?.SetFlag(Systems.StoryManager.Flag.LanternFlameDead);
		_dyingUntil = _t + seconds;
		_dyingLength = seconds;
		Audio.AudioDirector.OneShot(this, "bulb_sputter", 3, null, -10f, "Player");
		GD.Print("[story] Act 14: the lantern's flame gutters out");
	}
	private float _dyingLength = 3.2f;
	private bool _deadHinted;

	/// <summary>The one-time how-to under the picture, the first time the lantern is taken in play (never on a restore).</summary>
	[Export] public string HowToLine = "F: lantern on and off.   B: its blacklight.   Right mouse: gather its beam.";

	private OmniLight3D _glow;
	private SpotLight3D _beam;
	private PlayerController _player;
	private PlayerInventory _inv;
	private bool _lit = true;
	private float _focus;
	private bool _firstFrame = true, _hinted;
	private double _t;
	private float _dip = 1f;
	private readonly RandomNumberGenerator _rng = new();
	private double _joltUntil = -1;

	/// <summary>Makes the flame gutter hard for <paramref name="seconds"/> (a slam, a shock).</summary>
	public void Jolt(float seconds) => _joltUntil = _t + seconds;

	public override void _Ready()
	{
		TopLevel = true;
		_player = GetParent<PlayerController>();
		_inv = _player.GetNode<PlayerInventory>("Inventory");
		_glow = new OmniLight3D
		{
			LightColor = LightColor,
			OmniRange = GlowRange,
			OmniAttenuation = GlowAttenuation,
			LightEnergy = GlowEnergy,
			ShadowEnabled = true,
			Position = new Vector3(0.18f, -0.25f, -0.25f),   // held low and to the right
			Visible = false,
		};
		AddChild(_glow);
		_beam = new SpotLight3D
		{
			LightColor = LightColor,
			SpotRange = BeamRange,
			SpotAngle = BeamAngle,
			SpotAngleAttenuation = 0.6f,
			LightEnergy = 0f,
			ShadowEnabled = true,
			Visible = false,
		};
		AddChild(_beam);
	}

	/// <summary>The blacklight's beam, for the ink's shader (globals) and for code (<see cref="Uv"/>).</summary>
	private void PublishUv(bool on, Camera3D cam)
	{
		Vector3 pos = cam.GlobalPosition, dir = -cam.GlobalBasis.Z;
		float cosOuter = Mathf.Cos(Mathf.DegToRad(UvAngle));
		Uv = (on, pos, dir, UvRange, cosOuter);
		RenderingServer.GlobalShaderParameterSet("uv_light_on", on ? 1f : 0f);
		RenderingServer.GlobalShaderParameterSet("uv_light_pos", pos);
		RenderingServer.GlobalShaderParameterSet("uv_light_dir", dir);
		RenderingServer.GlobalShaderParameterSet("uv_light_range", UvRange);
	}

	public override void _ExitTree()
	{
		Uv = (false, Vector3.Zero, Vector3.Forward, 0f, 1f);
		RenderingServer.GlobalShaderParameterSet("uv_light_on", 0f);
	}

	/// <summary>How strongly the blacklight is on a world point right now (0..1): in its cone, in range,
	/// matching the ink shader's own falloff (line of sight is the caller's).</summary>
	public static float UvOn(Vector3 world)
	{
		var (on, pos, dir, range, cosOuter) = Uv;
		if (!on) return 0f;
		Vector3 to = world - pos;
		float d = to.Length();
		if (d < 0.01f) return 1f;
		float cosInner = Mathf.Lerp(cosOuter, 1f, 0.7f);
		float cone = Mathf.SmoothStep(cosOuter, cosInner, (to / d).Dot(dir));
		float fall = 1f - Mathf.SmoothStep(range * 0.45f, range, d);
		return cone * fall;
	}

	public override void _Process(double delta)
	{
		var cam = _player.CameraRig?.Camera;
		if (cam == null) return;
		GlobalTransform = cam.GlobalTransform;
		bool dead = FlameDead && !Dying;
		if (_player.PlayerInput.LightPressed && _inv.HasLantern)
		{
			if (dead && !Blacklight)
			{
				// the flame won't take: the striker clicks, a spark, nothing
				Audio.AudioDirector.OneShot(this, "lighter_flick", 1, null, -14f, "Player", 3f, 0.08f);
				if (!_deadHinted && UI.Subtitle.Instance != null) { _deadHinted = true; _ = UI.Subtitle.Instance.Show("The flame won't take.   B: the blacklight still works.", 0.6f, 4f, 1f); }
			}
			else
			{
				_lit = !_lit;
				Audio.AudioDirector.OneShot(this, _lit ? "lantern_on" : "lantern_off", 1, null, -12f, "Player");
			}
		}
		if (_player.PlayerInput.LanternModePressed && _inv.HasLantern)
		{
			Blacklight = !Blacklight;
			_lit = true;
			Audio.AudioDirector.OneShot(this, "lantern_off", 1, null, -16f, "Player", 3f, 0.02f);
			Audio.AudioDirector.OneShot(this, Blacklight ? "uv_hum" : "lantern_on", 1, null, Blacklight ? -14f : -12f, "Player");
		}
		// Restored with the lantern already in hand: no how-to. Taken in play: say it once.
		if (_firstFrame) { _firstFrame = false; _hinted = _inv.HasLantern; }
		else if (!_hinted && _inv.HasLantern)
		{
			_hinted = true;
			if (!string.IsNullOrEmpty(HowToLine) && UI.Subtitle.Instance != null) _ = UI.Subtitle.Instance.Show(HowToLine, 1.0f, 5.5f, 1.4f);
		}

		bool on = _inv.HasLantern && _lit && !(dead && !Blacklight);
		_glow.Visible = on;
		bool uv = on && Blacklight;
		PublishUv(uv, cam);
		if (!on) { _beam.Visible = false; return; }
		if (uv)
		{
			// the blacklight: a steady (electric, no flame) violet cone, and hardly any glow round the player
			_glow.LightColor = UvColor;
			_glow.OmniRange = 6f;
			_glow.LightEnergy = UvGlowEnergy;
			_beam.Visible = true;
			_beam.LightColor = UvColor;
			_beam.SpotRange = UvRange;
			_beam.SpotAngle = UvAngle;
			_beam.LightEnergy = UvEnergy;
			_focus = 0f;
			return;
		}
		_glow.LightColor = LightColor;
		_glow.OmniRange = GlowRange;
		_beam.LightColor = LightColor;
		_beam.SpotRange = BeamRange;
		_beam.SpotAngle = BeamAngle;

		float dt = (float)delta;
		_t += dt;
		// Two incommensurate sines (~6.5 Hz and ~11 Hz) plus a rare short dip; never below 80%.
		float flicker = 1f + FlickerAmount * (0.6f * Mathf.Sin((float)_t * Mathf.Tau * 6.5f) + 0.4f * Mathf.Sin((float)_t * Mathf.Tau * 11f + 1.3f));
		if (_dip >= 0.999f && _rng.Randf() < DipChance * dt) _dip = DipLevel;
		_dip = Mathf.MoveToward(_dip, 1f, dt * 1.5f);
		float k = Mathf.Max(0.8f, flicker * _dip);
		// A jolt (the cabin door slamming): the flame gutters hard for a moment.
		if (_t < _joltUntil) k *= _rng.RandfRange(0.15f, 1.0f);
		// dying: sputtering, the dips deeper and longer, fading to nothing
		if (Dying && !Blacklight)
		{
			float u = 1f - (float)((_dyingUntil - _t) / _dyingLength);
			k *= Mathf.Lerp(1f, 0f, u * u) * (_rng.Randf() < 0.25f + 0.5f * u ? _rng.RandfRange(0.05f, 0.4f) : 1f);
		}

		float s = 1f - Mathf.Exp(-Sharpness * dt);
		_focus = Mathf.Lerp(_focus, _player.PlayerInput.Focus ? 1f : 0f, s);
		_glow.LightEnergy = GlowEnergy * Mathf.Lerp(1f, GlowWhileFocused, _focus) * k;
		_beam.Visible = _focus > 0.01f;
		_beam.LightEnergy = BeamEnergy * _focus * k;
	}
}
