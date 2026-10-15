using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-14h, the old-and-abandoned pass for Acts 15-18) One old incandescent bulb going bad: a dirty glass bulb in a
/// wire cage (or bare, or under a shade), and its light, with a life of its own:
/// <list type="bullet">
/// <item><b>Steady</b>: on, wandering a few percent (old filament, a brownout somewhere);</item>
/// <item><b>Flicker</b>: on, but now and then it drops out in a short burst and comes back (a loose filament);</item>
/// <item><b>Buzz</b>: a fast shallow shimmer, and now and then a sag (a bad ballast, a bad contact);</item>
/// <item><b>Dead</b>: the glass dark and grey, no light.</item>
/// </list>
/// <see cref="Level"/> scales it from outside (a whole room browning out). Its own dice (never anyone else's), and with
/// Reduce Flashing every drop is a soft dip. It only runs while it is near the camera.
/// </summary>
public partial class FailingBulb : Node3D
{
	public enum Mode { Steady, Flicker, Buzz, Dead }
	public enum Housing { Cage, Bare, None }

	public Mode State = Mode.Steady;
	public Color LightColor = new(1f, 0.72f, 0.42f);
	public float Energy = 0.6f, Range = 7f, Attenuation = 1.3f;
	public bool Shadows;
	public Housing Kind = Housing.Cage;
	/// <summary>The bulb's glass radius (m).</summary>
	public float Radius = 0.045f;
	/// <summary>How far off it stops working (m from the camera).</summary>
	public float ActiveRange = 60f;
	public int Seed = 1;
	/// <summary>Scales it from outside (0..1): a room browning out, a bulb dying.</summary>
	public float Level { get; set; } = 1f;
	public OmniLight3D Light => _light;

	private OmniLight3D _light;
	private StandardMaterial3D _glass;
	private readonly RandomNumberGenerator _rng = new();
	private double _t, _next, _burstEnd;
	private int _burstLeft;
	private float _drop = 1f, _dropTarget = 1f, _wander = 1f, _wanderTarget = 1f, _sag = 1f;
	private double _dropUntil;

	public override void _Ready()
	{
		_rng.Seed = (ulong)(Seed * 7919 + 17);
		_next = _rng.RandfRange(0.5f, 4f);
		_glass = new StandardMaterial3D
		{
			ResourceName = "failing_bulb_glass", AlbedoColor = new Color(0.32f, 0.3f, 0.26f), Roughness = 0.35f, MetallicSpecular = 0.6f,
			EmissionEnabled = true, Emission = LightColor, EmissionEnergyMultiplier = 0f,
		};
		AddChild(new MeshInstance3D
		{
			Name = "Glass", Mesh = new SphereMesh { Radius = Radius, Height = Radius * 2.3f, RadialSegments = 10, Rings = 6 },
			MaterialOverride = _glass, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		var k = new MeshKit();
		k.Mat(Materials.Wire);
		k.Color = Colors.White;
		// the brass-and-bakelite holder above it
		k.Cylinder(new Vector3(0, Radius * 0.9f, 0), new Vector3(0, Radius * 2.4f, 0), Radius * 0.42f, Radius * 0.5f, 8, true);
		if (Kind == Housing.Cage)
		{
			// a wire cage: four ribs and two hoops round the glass
			float r = Radius * 1.55f;
			for (int i = 0; i < 4; i++)
			{
				float a = i * Mathf.Pi * 0.5f + 0.4f;
				Vector3 o = new(Mathf.Cos(a), 0, Mathf.Sin(a));
				k.Cylinder(new Vector3(0, Radius * 2.2f, 0) + o * Radius * 0.6f, o * r + Vector3.Up * Radius * 0.6f, 0.0035f, 0.0035f, 3, false);
				k.Cylinder(o * r + Vector3.Up * Radius * 0.6f, o * r * 0.9f + Vector3.Down * Radius * 0.9f, 0.0035f, 0.0035f, 3, false);
				k.Cylinder(o * r * 0.9f + Vector3.Down * Radius * 0.9f, Vector3.Down * Radius * 1.5f, 0.0035f, 0.0035f, 3, false);
			}
			for (int i = 0; i < 10; i++)
			{
				float a0 = i / 10f * Mathf.Tau, a1 = (i + 1) / 10f * Mathf.Tau;
				foreach (var (y, rr) in new[] { (Radius * 0.6f, r), (-Radius * 0.6f, r * 0.94f) })
					k.Cylinder(new Vector3(Mathf.Cos(a0) * rr, y, Mathf.Sin(a0) * rr), new Vector3(Mathf.Cos(a1) * rr, y, Mathf.Sin(a1) * rr), 0.0035f, 0.0035f, 3, false);
			}
		}
		k.CommitTo(this, "Holder", false);
		_light = new OmniLight3D
		{
			Name = "Light", LightColor = LightColor, LightEnergy = 0f, OmniRange = Range, OmniAttenuation = Attenuation, ShadowEnabled = Shadows,
			Position = Vector3.Down * Radius * 1.6f, DistanceFadeEnabled = true, DistanceFadeBegin = ActiveRange * 0.8f, DistanceFadeLength = ActiveRange * 0.2f,
		};
		AddChild(_light);
		Apply(1f);
	}

	/// <summary>The bulb's materials, shared by every bulb (the glass is each bulb's own: it dims).</summary>
	public static class Materials
	{
		public static StandardMaterial3D Wire => _wire ??= new StandardMaterial3D
		{
			ResourceName = "failing_bulb_wire", AlbedoColor = new Color(0.16f, 0.14f, 0.12f), Metallic = 0.5f, Roughness = 0.7f, VertexColorUseAsAlbedo = true,
		};
		private static StandardMaterial3D _wire;
	}

	private void Apply(float f)
	{
		float e = State == Mode.Dead ? 0f : Mathf.Max(0f, f * Level);
		_light.Visible = e > 0.01f;
		_light.LightEnergy = Energy * e;
		_light.LightColor = LightColor;
		_glass.Emission = LightColor;
		// (an old bulb's glass: never brighter than the room wants; dead, a dull grey with a dark filament)
		_glass.EmissionEnergyMultiplier = 2.2f * e;
		_glass.AlbedoColor = State == Mode.Dead ? new Color(0.16f, 0.15f, 0.13f) : new Color(0.32f, 0.3f, 0.26f);
	}

	public override void _Process(double delta)
	{
		if (State == Mode.Dead) { Apply(0f); return; }
		if (GetViewport()?.GetCamera3D() is { } cam && cam.GlobalPosition.DistanceSquaredTo(GlobalPosition) > ActiveRange * ActiveRange) return;
		_t += delta;
		float dt = (float)delta;
		bool soft = GameSettings.Instance?.ReduceFlashing ?? false;
		// the filament's slow wander
		if (_rng.Randf() < dt * 0.8f) _wanderTarget = _rng.RandfRange(0.9f, 1.04f);
		_wander = Mathf.MoveToward(_wander, _wanderTarget, dt * 0.2f);
		float f = _wander;
		switch (State)
		{
			case Mode.Flicker:
				if (_t >= _next && _burstLeft == 0) { _burstLeft = _rng.RandiRange(2, 4); _next = _t + _rng.RandfRange(1.5f, 6.5f); }
				if (_burstLeft > 0 && _t >= _dropUntil)
				{
					// each drop: out (or nearly) for a moment, then back
					bool down = _dropTarget > 0.5f;
					_dropTarget = down ? (soft ? 0.55f : _rng.RandfRange(0.05f, 0.35f)) : 1f;
					_dropUntil = _t + (down ? _rng.RandfRange(0.05f, 0.16f) : _rng.RandfRange(0.04f, 0.12f)) * (soft ? 3f : 1f);
					if (!down) _burstLeft--;
				}
				_drop = soft ? Mathf.MoveToward(_drop, _dropTarget, dt * 3f) : _dropTarget;
				f *= _drop;
				break;
			case Mode.Buzz:
				// a fast shallow shimmer (none with Reduce Flashing), and now and then a slow sag
				if (!soft) f *= 0.9f + 0.1f * Mathf.Sin((float)_t * 46f + Mathf.Sin((float)_t * 3.1f) * 2f);
				if (_t >= _next) { _next = _t + _rng.RandfRange(2f, 7f); _burstEnd = _t + _rng.RandfRange(0.3f, 0.9f); }
				_sag = Mathf.MoveToward(_sag, _t < _burstEnd ? 0.55f : 1f, dt * 2.5f);
				f *= _sag;
				break;
		}
		Apply(f);
	}
}
