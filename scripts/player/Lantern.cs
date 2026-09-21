using Godot;

namespace ProjectDS.Player;

/// <summary>
/// A hand lantern the player equips from the cabin porch in Act 3: a radial
/// light (STORY.md Act 3) that rides the camera, with a living flame flicker.
/// Holding Focus (right mouse — the same button that zooms the camera) gathers
/// the light into a forward beam that throws further, like shuttering the glass.
/// [F] (PlayerInput.LightPressed) toggles it on and off.
/// </summary>
public partial class Lantern : Node3D
{
	[ExportGroup("Radial glow")]
	[Export] public float GlowRange = 7.5f;
	[Export] public float GlowEnergy = 1.1f;
	[Export] public float GlowAttenuation = 1.4f;
	/// <summary>How much of the glow remains while the beam is focused.</summary>
	[Export] public float GlowWhileFocused = 0.55f;
	[Export] public Color LightColor = new(1.0f, 0.72f, 0.42f);

	[ExportGroup("Focused beam")]
	[Export] public float BeamRange = 11f;
	[Export] public float BeamEnergy = 2.2f;
	[Export] public float BeamAngle = 35f;
	[Export] public float Sharpness = 6f;

	[ExportGroup("Flicker")]
	[Export] public float FlickerAmount = 0.06f;
	/// <summary>Chance per second of a brief dip.</summary>
	[Export] public float DipChance = 0.15f;
	[Export] public float DipLevel = 0.85f;

	private OmniLight3D _glow;
	private SpotLight3D _beam;
	private PlayerController _player;
	private PlayerInventory _inv;
	private bool _lit = true;
	private float _focus;
	private double _t;
	private float _dip = 1f;
	private readonly RandomNumberGenerator _rng = new();

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

	public override void _Process(double delta)
	{
		var cam = _player.CameraRig?.Camera;
		if (cam == null) return;
		GlobalTransform = cam.GlobalTransform;
		if (_player.PlayerInput.LightPressed && _inv.HasLantern) _lit = !_lit;

		bool on = _inv.HasLantern && _lit;
		_glow.Visible = on;
		if (!on) { _beam.Visible = false; return; }

		float dt = (float)delta;
		_t += dt;
		// Two incommensurate sines (~6.5 Hz and ~11 Hz) plus a rare short dip; never below 80%.
		float flicker = 1f + FlickerAmount * (0.6f * Mathf.Sin((float)_t * Mathf.Tau * 6.5f) + 0.4f * Mathf.Sin((float)_t * Mathf.Tau * 11f + 1.3f));
		if (_dip >= 0.999f && _rng.Randf() < DipChance * dt) _dip = DipLevel;
		_dip = Mathf.MoveToward(_dip, 1f, dt * 1.5f);
		float k = Mathf.Max(0.8f, flicker * _dip);

		float s = 1f - Mathf.Exp(-Sharpness * dt);
		_focus = Mathf.Lerp(_focus, _player.PlayerInput.Focus ? 1f : 0f, s);
		_glow.LightEnergy = GlowEnergy * Mathf.Lerp(1f, GlowWhileFocused, _focus) * k;
		_beam.Visible = _focus > 0.01f;
		_beam.LightEnergy = BeamEnergy * _focus * k;
	}
}
