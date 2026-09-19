using Godot;

namespace ProjectDS.World;

public enum BirdColor { Red, Blue, Purple, BlackOmen }

/// <summary>
/// Act 1's optional photo-minigame subject: a small cardinal-shaped bird that
/// idles in place until the camera catches it. Three ordinary colours are
/// harmless flavour; the fourth (black, glowing red eye) is not meant to be
/// found comfortably, and photographing it ends the minigame for good.
/// </summary>
[Tool]
[GlobalClass]
public partial class Bird : Node3D
{
	[Export] public BirdColor Color = BirdColor.Red;
	[Export] public float BobHeight = 0.02f;
	[Export] public float BobSeconds = 1.6f;

	public bool Photographed { get; private set; }
	public bool IsOmen => Color == BirdColor.BlackOmen;

	private Node3D _model;
	private double _clock;

	public override void _Ready()
	{
		AddToGroup("photo_birds");
		var old = GetNodeOrNull("Model");
		if (old != null) { RemoveChild(old); old.QueueFree(); }
		_model = new Node3D { Name = "Model" };
		AddChild(_model);
		Build();
		_clock = (Seed() % 700) / 100.0;
	}

	private int Seed() => (int)Color * 191 + (int)(GlobalPosition.X * 13f) + (int)(GlobalPosition.Z * 7f);

	private void Build()
	{
		var k = new MeshKit();
		Color bodyColor = Color switch
		{
			BirdColor.Red => new Color(0.72f, 0.08f, 0.09f),
			BirdColor.Blue => new Color(0.15f, 0.28f, 0.75f),
			BirdColor.Purple => new Color(0.42f, 0.16f, 0.55f),
			_ => new Color(0.035f, 0.035f, 0.04f),
		};
		var mat = ProcTextures.Flat($"bird_{Color}", bodyColor, 0.85f);
		k.Color = Colors.White;
		k.Mat(mat);
		k.Blob(new Vector3(0, 0.09f, 0), new Vector3(0.05f, 0.055f, 0.08f), Seed() + 3, 0.1f, false, 0.9f);
		k.Blob(new Vector3(0, 0.135f, 0.055f), new Vector3(0.032f, 0.032f, 0.032f), Seed() + 9, 0.08f, false, 0.9f);
		k.Color = new Color(bodyColor.R * 0.7f, bodyColor.G * 0.7f, bodyColor.B * 0.7f);
		k.Box(new Vector3(0, 0.1f, -0.085f), new Vector3(0.018f, 0.045f, 0.08f), 1f, Basis.FromEuler(new Vector3(Mathf.DegToRad(20f), 0, 0)));
		k.Color = new Color(0.85f, 0.68f, 0.15f);
		k.Cylinder(new Vector3(0, 0.13f, 0.085f), new Vector3(0, 0.122f, 0.11f), 0.011f, 0.001f, 5);
		k.Color = Colors.White;
		k.CommitTo(_model, "BirdMesh", false);

		if (IsOmen)
		{
			var eyeMat = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.6f, 0.02f, 0.02f),
				EmissionEnabled = true,
				Emission = new Color(1f, 0.08f, 0.04f),
				EmissionEnergyMultiplier = 3.5f,
			};
			var eyeMesh = new SphereMesh { Radius = 0.006f, Height = 0.012f, Material = eyeMat };
			_model.AddChild(new MeshInstance3D
			{
				Mesh = eyeMesh,
				Position = new Vector3(0.024f, 0.14f, 0.075f),
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});
		}
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint() || Photographed || _model == null) return;
		_clock += delta;
		float bob = Mathf.Sin((float)(_clock / BobSeconds) * Mathf.Tau) * BobHeight;
		_model.Position = new Vector3(0, bob, 0);
		_model.Rotation = new Vector3(0, Mathf.Sin((float)(_clock * 0.31)) * 0.15f, 0);
	}

	/// <summary>Startles up and away, then gone, rather than just vanishing in place.</summary>
	public void Capture()
	{
		if (Photographed) return;
		Photographed = true;
		var tween = CreateTween();
		tween.TweenProperty(this, "position", Position + Basis.Z * 2.2f + Vector3.Up * 1.6f, 0.55f)
			.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tween.TweenCallback(Callable.From(QueueFree));
	}
}
