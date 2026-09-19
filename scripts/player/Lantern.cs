using Godot;

namespace ProjectDS.Player;

/// <summary>
/// A hand lantern the player equips from the cabin porch in Act 3. Rides the
/// camera. Holding Focus (right mouse — the same button that zooms the
/// camera) narrows the beam and throws it further, like a real hand tightening
/// around the glass.
/// </summary>
public partial class Lantern : Node3D
{
	[Export] public float BaseRange = 7f;
	[Export] public float FocusRange = 13f;
	[Export] public float BaseAngle = 50f;
	[Export] public float FocusAngle = 20f;
	[Export] public float BaseEnergy = 1.6f;
	[Export] public float FocusEnergy = 3f;
	[Export] public float Sharpness = 6f;

	private SpotLight3D _light;
	private PlayerController _player;
	private PlayerInventory _inv;

	public override void _Ready()
	{
		TopLevel = true;
		_player = GetParent<PlayerController>();
		_inv = _player.GetNode<PlayerInventory>("Inventory");
		_light = new SpotLight3D
		{
			LightColor = new Color(1f, 0.8f, 0.52f),
			SpotRange = BaseRange,
			SpotAngle = BaseAngle,
			SpotAngleAttenuation = 0.6f,
			LightEnergy = BaseEnergy,
			ShadowEnabled = true,
			Visible = false,
		};
		AddChild(_light);
	}

	public override void _Process(double delta)
	{
		var cam = _player.CameraRig?.Camera;
		if (cam == null) return;
		GlobalTransform = cam.GlobalTransform;
		bool on = _inv.HasLantern;
		_light.Visible = on;
		if (!on) return;

		bool focus = _player.PlayerInput.Focus;
		float k = 1f - Mathf.Exp(-Sharpness * (float)delta);
		_light.SpotRange = Mathf.Lerp(_light.SpotRange, focus ? FocusRange : BaseRange, k);
		_light.SpotAngle = Mathf.Lerp(_light.SpotAngle, focus ? FocusAngle : BaseAngle, k);
		_light.LightEnergy = Mathf.Lerp(_light.LightEnergy, focus ? FocusEnergy : BaseEnergy, k);
	}
}
