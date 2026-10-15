using Godot;
using ProjectDS.Entities;

namespace ProjectDS.World.HallwayParts;

/// <summary>
/// Act 15's thing in the hall (2026-10-15, the owner: "the stalker should replace the shadowman completely"): the stalker
/// itself (<see cref="StalkerBody"/>: the rigged model, its black skin, its rags and its torn jaw) where the code-built
/// shadow man stood. It plays the red light, green light game as he did (see <see cref="Act15Hallway"/>): it never
/// walks; while the lights are green it is frozen where it stands, and in the red it is simply somewhere else, caught
/// each time in a different held pose (looming over, its arms hung, leaning out round the wall's edge the way it leans
/// out round a trunk), its head turning to follow the eye, its jaw working when it's looked at. Its eyes are two embers;
/// they flare when it takes someone, and the melting turn runs them through colours and down its face.
///
/// It keeps the old figure's contract, so the hall's turns didn't change: it faces -Z in its own space (the body inside
/// it is turned round: the stalker's model faces +Z), <see cref="StandAt"/>, <see cref="Glare"/>, <see cref="EyeColor"/>,
/// <see cref="EyeDrips"/>, and <see cref="Height"/> for the turns that lay it down.
/// </summary>
public partial class HallStalker : Node3D
{
	/// <summary>Its standing height at <see cref="Size"/> 1.08 (the bunker's and Act 11's size).</summary>
	public const float Height = 2.36f;
	/// <summary>Its eyes' own colour (the bunker's and Act 11's ember red).</summary>
	public static readonly Color EyeDefault = new(1f, 0.16f, 0.05f);

	[Export] public float Size = 1.08f;
	public StalkerBody Body { get; private set; }
	/// <summary>Where its eyes are right now (its head's pose: where a look is turned to meet it).</summary>
	public Vector3 EyesAt => Body != null && Body.IsInsideTree() ? Body.EyesWorld : GlobalPosition + Vector3.Up * 1.9f * Scale.Y;

	private float _glare = 0.3f;
	/// <summary>0..1: how hard the eyes burn (they flare when it takes someone; past 0.9 it throws its arms at them).</summary>
	public float Glare
	{
		get => _glare;
		set
		{
			_glare = value;
			ApplyEyes();
			if (value >= 0.9f && !_shoved && Body != null) { _shoved = true; Body.Play("shove", 0.08f); }
			else if (value < 0.5f) _shoved = false;
		}
	}

	private bool _shoved;
	private Color _eyeColour = EyeDefault;
	private StandardMaterial3D _eyeMat;
	private OmniLight3D _eyeLight;
	private GpuParticles3D _drip;
	private StandardMaterial3D _dripMat;
	private readonly RandomNumberGenerator _rng = new();
	private int _lastPose = -1;
	// the poses it's caught in, each time it's somewhere new (held clips: frozen mid-move)
	private static readonly string[] Poses = { "loom", "idle" };

	public override void _Ready()
	{
		_rng.Randomize();
		PhotoSubject.Attach(this, "shadowman", new Vector3(0, 1.6f, 0), 1f, 45f, 12f, true, new Vector3(0, 1.0f, 0));
		Body = new StalkerBody { Name = "Body", Seed = 2077, Size = Size, Idle = true };
		Body.Rotation = new Vector3(0, Mathf.Pi, 0);   // (its model faces +Z; this figure faces -Z)
		AddChild(Body);
		Body.GlowEyes(EyeDefault, 6f);
		// its eyes' beads and light, taken over so their colour and burn can be set
		if (Body.FindChild("EyeGlow", true, false) is Node glow)
		{
			_eyeMat = new StandardMaterial3D { AlbedoColor = EyeDefault, EmissionEnabled = true, Emission = EyeDefault, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
			foreach (var n in glow.FindChildren("*", "MeshInstance3D", true, false)) ((MeshInstance3D)n).MaterialOverride = _eyeMat;
			_eyeLight = glow.FindChild("Light", true, false) as OmniLight3D;
		}
		ApplyEyes();
		// it is there: you can't walk through it
		var body = new StaticBody3D { Name = "Solid", CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, 1.1f, 0), Shape = new CapsuleShape3D { Radius = 0.3f, Height = 2.2f } });
		AddChild(body);
	}

	public override void _Process(double delta)
	{
		if (Body == null || !IsVisibleInTree()) return;
		// its head follows the eye, and when it's looked at its jaw works (the stalker's own horror pass)
		if (GetViewport()?.GetCamera3D() is { } cam)
		{
			Body.TrackTarget = cam.GlobalPosition;
			var to = (Body.EyesWorld - cam.GlobalPosition).Normalized();
			Body.Watched = (-cam.GlobalBasis.Z).Dot(to) > 0.8f;
		}
		if (_drip != null && _drip.Emitting) _drip.GlobalPosition = Body.EyesWorld + Vector3.Down * 0.02f;
	}

	private void ApplyEyes()
	{
		float g = Mathf.Clamp(_glare, 0f, 1f);
		if (_eyeMat != null)
		{
			_eyeMat.AlbedoColor = _eyeColour;
			_eyeMat.Emission = _eyeColour;
			_eyeMat.EmissionEnergyMultiplier = Mathf.Lerp(0.2f, 12f, g * g);
		}
		if (_eyeLight != null)
		{
			_eyeLight.LightColor = _eyeColour;
			_eyeLight.LightEnergy = Mathf.Lerp(0.05f, 1.4f, g);
		}
	}

	/// <summary>Its eyes' light (their own ember red; the melting red-light turn changes it, slowly).</summary>
	public void EyeColor(Color c) { _eyeColour = c; ApplyEyes(); }

	/// <summary>Its eyes running: a slow drip of their own light down its face (the melting red-light turn).</summary>
	public void EyeDrips(bool on, Color c)
	{
		if (_drip == null)
		{
			if (!on) return;
			_dripMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, AlbedoColor = c };
			_drip = new GpuParticles3D
			{
				Name = "EyeDrip", Amount = 16, Lifetime = 1.4f, LocalCoords = false, TopLevel = true,
				ProcessMaterial = new ParticleProcessMaterial
				{
					Gravity = new Vector3(0, -0.12f, 0), InitialVelocityMin = 0f, InitialVelocityMax = 0.01f, ScaleMin = 0.6f, ScaleMax = 1f,
					EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.035f, 0.004f, 0.004f),
				},
				DrawPass1 = new QuadMesh { Size = new Vector2(0.006f, 0.014f), Material = _dripMat },
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			AddChild(_drip);
		}
		_dripMat.AlbedoColor = c;
		_drip.Emitting = on;
	}

	/// <summary>Puts it at <paramref name="at"/> (feet), facing <paramref name="toward"/>, caught in a new held pose.</summary>
	public void StandAt(Vector3 at, Vector3 toward)
	{
		GlobalPosition = at;
		Vector3 d = toward - at;
		d.Y = 0;
		if (d.LengthSquared() > 0.001f) GlobalBasis = Basis.LookingAt(d.Normalized(), Vector3.Up);
		Pose();
	}

	/// <summary>A new held pose (never the one just seen), frozen at its end: it was moving, and stopped when the light did.</summary>
	public void Pose(string clip = null)
	{
		if (Body == null) return;
		if (clip == null)
		{
			int i;
			do i = _rng.RandiRange(0, Poses.Length - 1); while (i == _lastPose && Poses.Length > 1);
			_lastPose = i;
			clip = Poses[i];
		}
		_shoved = false;
		Body.Snap(clip);
	}
}
