using System;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// A road flare burning green (2026-10-10, the owner: "guide the way in the snow tunnel in act 24 with flares spread out
/// far from each other but lit, the play can pick up the flare for a light, they could also throw the flare to distract
/// the wendigo towards it. Let do green flares. Also when the player interacts with the flare, it is on a timer until it
/// goes out. This should take like 80% of the time it takes to get the next flare").
/// <list type="bullet">
/// <item>On the ground (<see cref="Kind.Planted"/>): stuck in the snow at a lean, burning end up, lit and burning for as
/// long as it's left there; taking it (E) hands it to <see cref="HeldFlare"/> with its time to burn.</item>
/// <item>Thrown (<see cref="Kind.Thrown"/>): flies, tumbling, bounces off the walls, lands in the snow and burns out
/// there; where it lands is where the wendigo goes to look (<see cref="Landed"/>), and it stays to sniff at it while it
/// burns.</item>
/// </list>
/// Its light is a soft, steady green, wavering a little (never a flicker you'd call flashing); it fizzes; it smokes a
/// little; sparks drift up off it.
/// </summary>
public partial class Flare : Node3D
{
	public enum Kind { Planted, Thrown }
	public static readonly Color Green = new(0.38f, 1f, 0.45f);

	public Kind Mode = Kind.Planted;
	/// <summary>Seconds left to burn (planted: forever, until it's taken).</summary>
	public float BurnLeft = -1f;
	/// <summary>Seconds of burn a planted one has once taken.</summary>
	public float BurnWhenTaken = 12f;
	public Vector3 Velocity;
	/// <summary>A thrown one has come down here (world).</summary>
	public static event Action<Vector3, float> Landed;
	public bool Out { get; private set; }
	public bool Down { get; private set; }

	private Node3D _model;
	private OmniLight3D _light;
	private GpuParticles3D _sparks;
	private AudioStreamPlayer3D _fizz;
	private Interactable _take;
	private float _t, _spin;
	private FastNoiseLite _waver;

	public override void _Ready()
	{
		_model = new Node3D { Name = "Model" };
		AddChild(_model);
		BuildModel(_model, out _);
		_light = new OmniLight3D { Name = "Light", LightColor = Green, LightEnergy = 0.85f, OmniRange = 10f, OmniAttenuation = 1.1f, ShadowEnabled = false, Position = new Vector3(0, 0.5f, 0) };
		AddChild(_light);
		_sparks = Sparks();
		_sparks.Position = new Vector3(0, 0.26f, 0);
		_model.AddChild(_sparks);
		_waver = new FastNoiseLite { Seed = (int)(GetInstanceId() % 9999), Frequency = 2.2f };
		if (ResourceLoader.Exists("res://assets/audio/ambient/flare_burn_loop.wav"))
		{
			var wav = (AudioStreamWav)GD.Load<AudioStreamWav>("res://assets/audio/ambient/flare_burn_loop.wav").Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			_fizz = new AudioStreamPlayer3D { Name = "Fizz", Stream = wav, Bus = "Events", VolumeDb = -12f, UnitSize = 2.5f, MaxDistance = 30f, Autoplay = true };
			AddChild(_fizz);
		}
		if (Mode == Kind.Planted)
		{
			// stuck in the snow at a lean, the burning end up
			_model.Rotation = new Vector3(Mathf.DegToRad(GD.RandRange(14, 26)), (float)GD.RandRange(0, Mathf.Tau), 0);
			_model.Position = Vector3.Down * 0.07f;
			_take = new Interactable { Name = "Take", Prompt = "Take the flare", PickRadius = 0.5f, MaxDistance = 2.4f, Position = new Vector3(0, 0.15f, 0) };
			_take.Interacted += Take;
			AddChild(_take);
		}
	}

	private void Take(PlayerController player)
	{
		if (Out) return;
		var held = HeldFlare.For(player);
		if (held == null) return;
		held.Take(BurnWhenTaken);
		Out = true;
		QueueFree();
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		if (Mode != Kind.Thrown || Down) return;
		Velocity += Vector3.Down * 9.8f * dt;
		Vector3 from = GlobalPosition, to = from + Velocity * dt;
		var q = PhysicsRayQueryParameters3D.Create(from, to + Velocity.Normalized() * 0.05f, 1);
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
		_spin += dt * 14f;
		_model.Rotation = new Vector3(_spin, _spin * 0.3f, 0);
		if (hit.Count == 0) { GlobalPosition = to; return; }
		Vector3 at = (Vector3)hit["position"], n = (Vector3)hit["normal"];
		if (n.Y > 0.55f && Velocity.Length() < 7f || Velocity.Length() < 2.5f)
		{
			// down in the snow: it settles on its side, the burning end lifted
			Down = true;
			GlobalPosition = at + n * 0.03f;
			_model.Rotation = new Vector3(Mathf.DegToRad(80), (float)GD.RandRange(0, Mathf.Tau), 0);
			AudioDirector.OneShot(this, "snow_whump", 3, at, -10f, "Events", 3f, 0.08f);
			Landed?.Invoke(at, Mathf.Max(BurnLeft, 0f));
			return;
		}
		// off a wall: a soft knock into the snow, and it drops
		GlobalPosition = at + n * 0.05f;
		Velocity = Velocity.Bounce(n) * 0.3f;
		AudioDirector.OneShot(this, "snow_whump", 3, at, -16f, "Events", 2f, 0.1f);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_t += dt;
		if (Out) return;
		float k = 1f;
		if (BurnLeft >= 0f)
		{
			BurnLeft -= dt;
			// the last few seconds: it gutters, dimming and wavering, and goes out
			if (BurnLeft < 3f) k = Mathf.Clamp(BurnLeft / 3f, 0f, 1f) * (0.75f + 0.25f * _waver.GetNoise1D(_t * 3f));
			if (BurnLeft <= 0f) { GoOut(); return; }
		}
		_light.LightEnergy = 0.85f * k * (0.9f + 0.1f * _waver.GetNoise1D(_t * 1.3f));
		if (_fizz != null) _fizz.VolumeDb = -12f + Mathf.LinearToDb(Mathf.Max(k, 0.01f));
	}

	private void GoOut()
	{
		Out = true;
		AudioDirector.OneShot(this, "flare_out", 1, GlobalPosition, -10f, "Events", 2.5f, 0.05f);
		_sparks.Emitting = false;
		_fizz?.Stop();
		var tw = CreateTween();
		tw.TweenProperty(_light, "light_energy", 0f, 0.6f);
		foreach (var c in _model.GetChildren())
			if (c is MeshInstance3D mi && mi.Name == "Core") mi.Visible = false;
		// the spent stick stays a while, then is gone
		GetTree().CreateTimer(40).Timeout += QueueFree;
	}

	// ------------------------------------------------------------------ its look

	private static StandardMaterial3D _paper, _cap, _core;

	/// <summary>A road flare: a red paper tube with a black cap, the burning end a hot green core. Along +Y, its burning
	/// end at the top (0.26 m).</summary>
	public static void BuildModel(Node3D parent, out MeshInstance3D core)
	{
		_paper ??= new StandardMaterial3D { ResourceName = "flare_paper", AlbedoColor = new Color(0.62f, 0.08f, 0.06f), Roughness = 0.8f };
		_cap ??= new StandardMaterial3D { ResourceName = "flare_cap", AlbedoColor = new Color(0.08f, 0.08f, 0.08f), Roughness = 0.6f };
		_core ??= new StandardMaterial3D
		{
			ResourceName = "flare_core", AlbedoColor = new Color(0.55f, 1f, 0.6f), EmissionEnabled = true, Emission = new Color(0.4f, 1f, 0.5f), EmissionEnergyMultiplier = 1.2f,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		};
		var k = new MeshKit();
		k.Mat(_paper);
		k.Color = Colors.White;
		k.Cylinder(Vector3.Zero, new Vector3(0, 0.24f, 0), 0.017f, 0.017f, 10, true);
		k.Mat(_cap);
		k.Cylinder(new Vector3(0, -0.035f, 0), new Vector3(0, 0.005f, 0), 0.019f, 0.019f, 10, true);
		// the charred lip round the burning end
		k.Cylinder(new Vector3(0, 0.235f, 0), new Vector3(0, 0.255f, 0), 0.018f, 0.015f, 10, false);
		k.CommitTo(parent, "Stick", true);
		core = new MeshInstance3D
		{
			Name = "Core", Mesh = new SphereMesh { Radius = 0.022f, Height = 0.05f, RadialSegments = 10, Rings = 5 }, MaterialOverride = _core,
			Position = new Vector3(0, 0.262f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		parent.AddChild(core);
	}

	/// <summary>Green sparks and a wisp of smoke, drifting up off the burning end.</summary>
	public static GpuParticles3D Sparks()
	{
		var pm = new ParticleProcessMaterial
		{
			Direction = Vector3.Up, Spread = 30f, InitialVelocityMin = 0.25f, InitialVelocityMax = 0.8f, Gravity = new Vector3(0, 0.4f, 0),
			ScaleMin = 0.4f, ScaleMax = 1f, Color = new Color(0.6f, 1f, 0.6f, 0.9f), DampingMin = 0.5f, DampingMax = 1f,
		};
		var ramp = new Gradient();
		ramp.SetColor(0, new Color(0.75f, 1f, 0.75f, 1f));
		ramp.SetColor(1, new Color(0.3f, 0.45f, 0.35f, 0f));
		pm.ColorRamp = new GradientTexture1D { Gradient = ramp };
		return new GpuParticles3D
		{
			Name = "Sparks", Amount = 18, Lifetime = 0.9, ProcessMaterial = pm, LocalCoords = false,
			DrawPass1 = new QuadMesh
			{
				Size = Vector2.One * 0.03f,
				Material = new StandardMaterial3D
				{
					AlbedoTexture = LakeParts.LakeFx.SoftDot(), VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				},
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityAabb = new Aabb(new Vector3(-1, -0.5f, -1), new Vector3(2, 2.5f, 2)),
		};
	}
}
