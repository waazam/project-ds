using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World.BossParts;

/// <summary>
/// One of Act 18's sixteen valves, on the pipe wall behind the catwalk: a riser out of the manifold,
/// a flanged valve body, a big yellow handwheel facing the catwalk, a gauge above it, and a stencilled
/// number. Only the valve the spotlight is on can be turned: held, a little at a time (progress is
/// kept if you have to let go and run). Faces -Z in its own space (toward the catwalk).
/// </summary>
public partial class Valve : Node3D
{
	public const float TurnSeconds = 2.6f;
	public int Number { get; set; }
	public bool Active { get; private set; }
	public bool Done { get; private set; }
	public float Progress { get; set; }
	public Interactable Use { get; private set; }
	/// <summary>Where someone turning it stands (world).</summary>
	public Vector3 StandWorld => ToGlobal(new Vector3(0, -1.2f, -1.0f));
	public Vector3 WheelWorld => ToGlobal(new Vector3(0, 0, -0.42f));

	private Node3D _wheel, _needle;

	public override void _Ready()
	{
		var steel = BossTextures.Painted(new Color(0.18f, 0.2f, 0.2f), 0.45f, 0.5f);
		var k = new MeshKit();
		k.Mat(steel);
		k.Color = Colors.White;
		// the riser from the manifold below, the valve body, and the stem out to the wheel
		k.Cylinder(new Vector3(0, -1.25f, 0), new Vector3(0, 0.5f, 0), 0.12f, 0.12f, 10, true);
		k.Cylinder(new Vector3(0, -0.25f, 0), new Vector3(0, -0.2f, 0), 0.2f, 0.2f, 12, true);    // flange
		k.Cylinder(new Vector3(0, 0.2f, 0), new Vector3(0, 0.25f, 0), 0.2f, 0.2f, 12, true);      // flange
		k.Blob(new Vector3(0, 0, -0.05f), new Vector3(0.24f, 0.22f, 0.26f), Number + 3, 0.04f, false);
		for (int b = 0; b < 6; b++)
		{
			float a = b / 6f * Mathf.Tau;
			foreach (float y in new[] { -0.23f, 0.23f })
				k.Cylinder(new Vector3(Mathf.Cos(a) * 0.17f, y - 0.03f, Mathf.Sin(a) * 0.17f), new Vector3(Mathf.Cos(a) * 0.17f, y + 0.03f, Mathf.Sin(a) * 0.17f), 0.02f, 0.02f, 6, true);
		}
		k.Cylinder(new Vector3(0, 0, -0.2f), new Vector3(0, 0, -0.44f), 0.035f, 0.035f, 8, true);  // stem
		k.CommitTo(this, "Body", true);

		// the handwheel: rim, five spokes, a hub with a red cap
		_wheel = new Node3D { Name = "Wheel", Position = new Vector3(0, 0, -0.42f) };
		AddChild(_wheel);
		var yellow = BossTextures.Painted(new Color(0.85f, 0.68f, 0.08f), 0.4f, 0.2f);
		const float R = 0.36f, tube = 0.034f;
		// the rim: a true torus, smooth all the way round (it lies in XZ; turn it to face the catwalk)
		_wheel.AddChild(new MeshInstance3D
		{
			Name = "Rim", Mesh = new TorusMesh { InnerRadius = R - tube, OuterRadius = R + tube, Rings = 64, RingSegments = 16 },
			MaterialOverride = yellow, Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0),
		});
		// five round spokes and a hub
		var spoke = new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.026f, Height = R - 0.06f, RadialSegments = 16, Rings = 1 };
		for (int s = 0; s < 5; s++)
		{
			float a = s / 5f * Mathf.Tau + 0.3f;
			Vector3 dir = new(Mathf.Cos(a), Mathf.Sin(a), 0);
			_wheel.AddChild(new MeshInstance3D
			{
				Name = $"Spoke{s}", Mesh = spoke, MaterialOverride = yellow, Position = dir * (0.06f + (R - 0.06f) * 0.5f),
				Basis = new Basis(new Vector3(0, 0, 1), a - Mathf.Pi * 0.5f),
			});
		}
		_wheel.AddChild(new MeshInstance3D
		{
			Name = "Hub", Mesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 0.1f, RadialSegments = 32 },
			MaterialOverride = yellow, Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0),
		});
		_wheel.AddChild(new MeshInstance3D
		{
			Name = "Cap", Mesh = new CylinderMesh { TopRadius = 0.036f, BottomRadius = 0.046f, Height = 0.04f, RadialSegments = 32 },
			MaterialOverride = BossTextures.Painted(new Color(0.6f, 0.06f, 0.05f), 0.3f, 0.1f), Position = new Vector3(0, 0, -0.07f), Rotation = new Vector3(-Mathf.Pi * 0.5f, 0, 0),
		});

		// the gauge above
		var g = new MeshKit();
		g.Mat(BossTextures.Painted(new Color(0.6f, 0.5f, 0.25f), 0.35f, 0.7f));
		g.Color = Colors.White;
		g.Cylinder(new Vector3(0, 0.5f, 0), new Vector3(0, 0.62f, 0), 0.02f, 0.02f, 6, true);
		g.Cylinder(new Vector3(0, 0.78f, 0.02f), new Vector3(0, 0.78f, -0.05f), 0.13f, 0.13f, 16, true);
		g.CommitTo(this, "Gauge", false);
		AddChild(new MeshInstance3D
		{
			Name = "Face", Mesh = new QuadMesh { Size = new Vector2(0.22f, 0.22f) }, Position = new Vector3(0, 0.78f, -0.052f), Rotation = new Vector3(0, Mathf.Pi, 0),
			MaterialOverride = new StandardMaterial3D { AlbedoTexture = BossTextures.GaugeFace, Roughness = 0.2f, MetallicSpecular = 0.8f },
		});
		_needle = new Node3D { Name = "Needle", Position = new Vector3(0, 0.78f, -0.056f) };
		AddChild(_needle);
		_needle.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.006f, 0.085f, 0.004f) }, Position = new Vector3(0, 0.035f, 0),
			MaterialOverride = BossTextures.Painted(new Color(0.8f, 0.08f, 0.06f), 0.4f, 0.1f),
		});
		// its number
		AddChild(new Label3D
		{
			Text = $"V-{Number:00}", FontSize = 48, PixelSize = 0.004f, Modulate = new Color(0.9f, 0.88f, 0.8f), OutlineSize = 0, Shaded = true,
			Position = new Vector3(0, -0.5f, -0.14f), Rotation = new Vector3(0, Mathf.Pi, 0),
		});

		Use = new Interactable { Name = "Turn", Prompt = "Turn the valve (hold)", PickRadius = 0.45f, MaxDistance = 2.3f, Position = new Vector3(0, 0, -0.45f), Enabled = false };
		AddChild(Use);
		Apply();
	}

	public void SetActive(bool on)
	{
		Active = on && !Done;
		if (Use != null) Use.Enabled = Active;
	}

	public void Finish()
	{
		Done = true;
		Progress = 1f;
		SetActive(false);
		Apply();
	}

	/// <summary>Wheel and needle follow the progress: three full turns, the needle falling from the red.</summary>
	public void Apply()
	{
		if (_wheel == null) return;
		_wheel.Rotation = new Vector3(0, 0, -Progress * Mathf.Tau * 3f);
		_needle.Rotation = new Vector3(0, 0, Mathf.Lerp(-2.0f, 1.6f, 1f - Progress) * -1f);
	}
}
