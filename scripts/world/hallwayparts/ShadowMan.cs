using Godot;

namespace ProjectDS.World.HallwayParts;

/// <summary>
/// Act 15's shadow man: a tall figure in a long coat and a brimmed hat, all of him the same flat black,
/// no face under the brim but two small white points of light where eyes would be, and a few dark
/// shards hanging in the air above him, turning slowly. He never walks: while the lights are green he
/// is frozen where he stands; when they go red he is simply somewhere else (see <see cref="Act15Hallway"/>).
/// He faces -Z in his own space.
/// </summary>
public partial class ShadowMan : Node3D
{
	public const float Height = 2.25f;
	private readonly Node3D[] _shards = new Node3D[7];
	private StandardMaterial3D _eyeMat;
	private float _t;
	private readonly RandomNumberGenerator _rng = new() { Seed = 1515 };

	/// <summary>0..1: how hard the eyes burn (they flare when he takes someone).</summary>
	public float Glare { get; set; } = 0.3f;

	public override void _Ready()
	{
		var black = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.005f, 0.005f, 0.007f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		var k = new MeshKit();
		k.Mat(black);
		k.Color = Colors.White;
		// the coat, flaring a little to the hem, and the shoulders
		k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 1.6f, 0), 0.34f, 0.22f, 10, true);
		k.Cylinder(new Vector3(0, 1.45f, 0), new Vector3(0, 1.72f, 0), 0.27f, 0.2f, 10, true);
		// arms, too long, hanging
		foreach (float s in new[] { -1f, 1f })
			k.Cylinder(new Vector3(s * 0.26f, 1.66f, 0), new Vector3(s * 0.31f, 0.62f, 0.02f), 0.06f, 0.04f, 6, true);
		// the head, and the hat
		k.Cylinder(new Vector3(0, 1.72f, 0), new Vector3(0, 1.8f, 0), 0.07f, 0.08f, 6, false);
		k.Cylinder(new Vector3(0, 1.8f, 0), new Vector3(0, 2.02f, 0), 0.12f, 0.11f, 10, true);
		k.Cylinder(new Vector3(0, 1.99f, 0), new Vector3(0, 2.02f, 0), 0.3f, 0.3f, 14, true);    // brim
		k.Cylinder(new Vector3(0, 2.02f, 0), new Vector3(0, 2.22f, 0), 0.14f, 0.12f, 12, true);  // crown
		k.CommitTo(this, "Body", false).CastShadow = GeometryInstance3D.ShadowCastingSetting.On;

		// two points of light under the brim
		_eyeMat = new StandardMaterial3D
		{
			AlbedoColor = Colors.White, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			EmissionEnabled = true, Emission = Colors.White, EmissionEnergyMultiplier = 3f,
		};
		foreach (float s in new[] { -1f, 1f })
			AddChild(new MeshInstance3D
			{
				Name = "Eye", Mesh = new SphereMesh { Radius = 0.014f, Height = 0.028f }, Position = new Vector3(s * 0.045f, 1.9f, -0.11f),
				MaterialOverride = _eyeMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});

		// shards in the air above him
		for (int i = 0; i < _shards.Length; i++)
		{
			var sh = new MeshInstance3D
			{
				Mesh = new PrismMesh { Size = new Vector3(0.07f, 0.26f, 0.05f) }, MaterialOverride = black,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			var n = new Node3D { Name = $"Shard{i}" };
			n.AddChild(sh);
			sh.Rotation = new Vector3(Mathf.Pi, 0, 0);   // points down, like the ones in the dark over him
			n.Position = new Vector3(_rng.RandfRange(-0.9f, 0.9f), _rng.RandfRange(2.6f, 3.8f), _rng.RandfRange(-0.6f, 0.6f));
			n.SetMeta("home", n.Position);
			n.SetMeta("phase", _rng.RandfRange(0, Mathf.Tau));
			AddChild(n);
			_shards[i] = n;
		}

		// he is there: you can't walk through him
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, 1.1f, 0), Shape = new CapsuleShape3D { Radius = 0.3f, Height = 2.2f } });
		AddChild(body);
	}

	public override void _Process(double delta)
	{
		_t += (float)delta;
		foreach (var n in _shards)
		{
			var home = (Vector3)n.GetMeta("home");
			float ph = (float)n.GetMeta("phase");
			n.Position = home + new Vector3(0, 0.06f * Mathf.Sin(_t * 0.5f + ph), 0);
			n.Rotation = new Vector3(0, _t * 0.2f + ph, 0.1f * Mathf.Sin(_t * 0.3f + ph));
		}
		_eyeMat.EmissionEnergyMultiplier = Mathf.Lerp(1.5f, 12f, Glare);
	}

	/// <summary>Puts him at <paramref name="at"/> (feet), facing <paramref name="toward"/>.</summary>
	public void StandAt(Vector3 at, Vector3 toward)
	{
		GlobalPosition = at;
		Vector3 d = toward - at;
		d.Y = 0;
		if (d.LengthSquared() > 0.001f) GlobalBasis = Basis.LookingAt(d.Normalized(), Vector3.Up);
	}
}
