using Godot;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// A tiny pulsing red receive LED (and the faint red it throws on the floor)
/// so the dropped walkie-talkie can be found in the dark by sight as well as
/// by its static. Parented to the walkie, so it goes when the walkie is taken.
/// </summary>
public partial class WalkieBeacon : Node3D
{
	[Export] public float PulseHz = 1.1f;

	private StandardMaterial3D _mat;
	private OmniLight3D _light;
	private double _t;

	public override void _Ready()
	{
		_mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(1f, 0.08f, 0.05f),
			EmissionEnabled = true,
			Emission = new Color(1f, 0.08f, 0.05f),
			EmissionEnergyMultiplier = 3f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		var k = new MeshKit();
		k.Mat(_mat);
		k.Blob(Vector3.Zero, new Vector3(0.008f, 0.008f, 0.008f), 5, 0f, false);
		k.CommitTo(this, "Led", false);
		_light = new OmniLight3D
		{
			Name = "LedGlow", LightColor = new Color(1f, 0.12f, 0.06f), LightEnergy = 0.5f, OmniRange = 1.4f,
			OmniAttenuation = 1.5f, Position = new Vector3(0, 0.03f, 0.03f), ShadowEnabled = false,
		};
		AddChild(_light);
	}

	public override void _Process(double delta)
	{
		_t += delta;
		// A short bright blink then a slow fade, like a receive light catching a carrier.
		float ph = (float)(_t * PulseHz % 1.0);
		float k = ph < 0.12f ? 1f : Mathf.Exp(-(ph - 0.12f) * 5f);
		_mat.EmissionEnergyMultiplier = 0.6f + 3.4f * k;
		_light.LightEnergy = 0.08f + 0.6f * k;
	}
}
