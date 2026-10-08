using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The flamethrower's fire (Act 24; the owner: "the flame needs to be really well done ... a napalm stream of fire just
/// like ww2"; "you may have to build a new fire system for the game"). A stream of burning fuel:
/// <list type="bullet">
/// <item>globs thrown from the nozzle twenty metres a second, a little spread, arcing down under their weight and slowed
/// by the air, each a lick of flame that swells and reddens as it flies (napalm_flame.gdshader): close together at the
/// nozzle they read as one bright rope, opening out into a rolling, billowing tongue at its end;</item>
/// <item>where a glob strikes the snow or the ice it clings and burns there for a couple of seconds, spreading a little:
/// the fire splashes and pools where the stream lands;</item>
/// <item>the burnt-out globs roll up into thick black smoke that climbs and spreads under the roof;</item>
/// <item>a warm light rides the middle of the stream, its strength from how much is burning (smoothed: it swells and
/// dies with the fire, it never strobes).</item>
/// </list>
/// All of it is two draws (the flames and the smoke, each one MultiMesh) and a handful of ray tests a frame, whatever the
/// stream's length. <see cref="Heat"/> lets anything ask how much fire is on it.
/// </summary>
public partial class NapalmStream : Node3D
{
	public const int MaxGlobs = 340, MaxSmoke = 240;
	// (dense enough that the globs overlap into one rope; heavy enough to arc down; lasting long enough to reach a wall
	// twenty metres off and splash there)
	[Export] public float Speed = 21f, Rate = 125f, Gravity = 8.5f, Drag = 0.42f, Life = 1.35f;

	private struct Glob { public Vector3 P, V; public float Age, Life, Size, Seed; public bool Alive, Stuck; }
	private readonly Glob[] _g = new Glob[MaxGlobs];
	private readonly Glob[] _s = new Glob[MaxSmoke];
	private MultiMesh _flameMM, _smokeMM;
	private MultiMeshInstance3D _flameMI, _smokeMI;
	private OmniLight3D _light, _splashLight;
	private float _splashLevel;
	private float _emitAcc, _lightLevel, _smokeAcc;
	private int _rayCursor;
	private readonly RandomNumberGenerator _rng = new();
	private static ShaderMaterial _mat;

	/// <summary>The number of globs alight (tests, and the light).</summary>
	public int Alight { get; private set; }
	/// <summary>Raised where a glob strikes something (a point, in the world).</summary>
	public event System.Action<Vector3> Struck;

	public override void _Ready()
	{
		TopLevel = true;
		GlobalTransform = Transform3D.Identity;
		_rng.Randomize();
		_mat ??= MakeMaterial();
		_flameMM = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = new QuadMesh { Size = Vector2.One }, InstanceCount = MaxGlobs };
		_smokeMM = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = new QuadMesh { Size = Vector2.One }, InstanceCount = MaxSmoke };
		// (the smoke drawn first, the flames over it; their bounds follow the fire, each frame: a fixed box about the
		// world's origin culled the whole stream away in the maze, far from it)
		_smokeMI = new MultiMeshInstance3D { Name = "Smoke", Multimesh = _smokeMM, MaterialOverride = _mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, SortingOffset = -1f };
		_flameMI = new MultiMeshInstance3D { Name = "Flames", Multimesh = _flameMM, MaterialOverride = _mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild(_smokeMI);
		AddChild(_flameMI);
		_light = new OmniLight3D { Name = "FireLight", LightColor = new Color(1f, 0.55f, 0.22f), OmniRange = 11f, LightEnergy = 0f, ShadowEnabled = false, Visible = false };
		AddChild(_light);
		// a second light where it has splashed and clings: the ice round it lit orange, the glow wavering slowly with the
		// burning (2026-10-07; eased, never strobing)
		_splashLight = new OmniLight3D { Name = "SplashLight", LightColor = new Color(1f, 0.5f, 0.18f), OmniRange = 7f, LightEnergy = 0f, ShadowEnabled = false, Visible = false };
		AddChild(_splashLight);
		for (int i = 0; i < MaxGlobs; i++) _flameMM.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.Zero), Vector3.Zero));
		for (int i = 0; i < MaxSmoke; i++) _smokeMM.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.Zero), Vector3.Zero));
	}

	private static ShaderMaterial MakeMaterial()
	{
		var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/napalm_flame.gdshader"), ResourceName = "napalm_flame" };
		m.SetShaderParameter("noise_tex", new NoiseTexture2D { Width = 128, Height = 128, Seamless = true, Noise = new FastNoiseLite { Frequency = 0.035f, FractalOctaves = 3, Seed = 9001 } });
		return m;
	}

	/// <summary>Fire this frame (or not): from <paramref name="nozzle"/> along <paramref name="dir"/>, carried on by
	/// <paramref name="carry"/> (the firer's own motion).</summary>
	public void Emit(bool firing, Vector3 nozzle, Vector3 dir, Vector3 carry, float dt)
	{
		if (!firing) { _emitAcc = 0f; return; }
		_emitAcc += Rate * dt;
		while (_emitAcc >= 1f)
		{
			_emitAcc -= 1f;
			int i = Free(_g);
			if (i < 0) break;
			var spread = new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f), _rng.RandfRange(-1f, 1f)) * 0.022f;
			float back = _rng.Randf() * dt;   // (spread along the frame, not bunched at its start)
			var v = (dir + spread).Normalized() * Speed * _rng.RandfRange(0.94f, 1.05f) + carry;
			_g[i] = new Glob { P = nozzle + v * back, V = v, Age = 0f, Life = Life * _rng.RandfRange(0.85f, 1.15f), Size = _rng.RandfRange(0.16f, 0.24f), Seed = _rng.Randf(), Alive = true };
		}
	}

	private static int Free(Glob[] a) { for (int i = 0; i < a.Length; i++) if (!a[i].Alive) return i; return -1; }

	public override void _Process(double delta)
	{
		float dt = Mathf.Min((float)delta, 0.05f);
		var space = GetWorld3D().DirectSpaceState;
		var cam = GetViewport()?.GetCamera3D();
		Vector3 eye = cam?.GlobalPosition ?? Vector3.Zero;
		int rays = 0, alight = 0, stuck = 0;
		Vector3 stuckSum = Vector3.Zero;
		Vector3 sum = Vector3.Zero;
		Vector3 lo = new(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
		for (int i = 0; i < MaxGlobs; i++)
		{
			ref var g = ref _g[i];
			if (!g.Alive) { continue; }
			g.Age += dt;
			float u = g.Age / g.Life;
			if (u >= 1f)
			{
				g.Alive = false;
				_flameMM.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.Zero), Vector3.Zero));
				Smoke(g.P, g.Size * 1.6f);
				continue;
			}
			if (!g.Stuck)
			{
				var prev = g.P;
				g.V += Vector3.Down * Gravity * dt;
				g.V -= g.V * Drag * dt;
				// the hot gas round it rises as it slows
				g.V += Vector3.Up * 1.2f * u * dt;
				g.P += g.V * dt;
				// a ray along its step (a few each frame, in turn: a glob between its tests can't go far)
				if ((i + _rayCursor) % 2 == 0 && rays < 110)
				{
					rays++;
					var q = PhysicsRayQueryParameters3D.Create(prev - g.V * dt, g.P, 1);
					var hit = space.IntersectRay(q);
					if (hit.Count > 0)
					{
						g.P = (Vector3)hit["position"] + (Vector3)hit["normal"] * 0.12f;
						g.Stuck = true;
						g.V = Vector3.Zero;
						// it clings and burns on: a longer life from here
						g.Life = g.Age + _rng.RandfRange(1.4f, 2.6f);
						Struck?.Invoke(g.P);
					}
				}
			}
			u = g.Age / g.Life;
			// swelling as it flies (a rope at the nozzle, billows at the end), a clinging glob spreading and settling
			float size = g.Stuck ? g.Size * (5f + 2f * Mathf.Sin(g.Age * 2.3f + g.Seed * 9f)) * (1f - 0.4f * u) : g.Size * (1f + 9f * Mathf.Pow(Mathf.Min(g.Age / 0.7f, 1f), 1.4f));
			float heat = g.Stuck ? 0.5f * (1f - u) : Mathf.Clamp(1f - g.Age * 1.1f, 0.1f, 1f);
			var at = g.Stuck ? g.P + Vector3.Up * size * 0.35f : g.P;
			// turned to the eye and drawn out along its flight (a streak where it's fast, round where it's slowed or
			// clinging): overlapping, the streaks read as one rope of fire, not a string of balls
			float stretch = g.Stuck ? 1f : 1f + Mathf.Min(g.V.Length() * 0.075f, 1.6f);
			_flameMM.SetInstanceTransform(i, new Transform3D(Facing(eye, at, g.Stuck ? Vector3.Up : g.V, size * stretch, size), at));
			_flameMM.SetInstanceCustomData(i, new Color(u, heat, g.Seed, g.Stuck ? 0.85f : 0.8f));
			alight++;
			sum += at;
			if (g.Stuck) { stuck++; stuckSum += at; }
			lo = lo.Min(at); hi = hi.Max(at);
			// clinging fire smokes as it burns
			if (g.Stuck && _rng.Randf() < dt * 3f) Smoke(g.P + Vector3.Up * 0.4f, size * 0.7f);
			// and the flying fire trails black smoke over it, thickening toward the stream's end
			else if (!g.Stuck && u > 0.35f && _rng.Randf() < dt * 2.2f) Smoke(g.P + Vector3.Up * size * 0.4f, size * 0.8f);
		}
		_rayCursor++;
		Alight = alight;
		for (int i = 0; i < MaxSmoke; i++)
		{
			ref var s = ref _s[i];
			if (!s.Alive) continue;
			s.Age += dt;
			float u = s.Age / s.Life;
			if (u >= 1f) { s.Alive = false; _smokeMM.SetInstanceTransform(i, new Transform3D(Basis.FromScale(Vector3.Zero), Vector3.Zero)); continue; }
			s.V = s.V.Lerp(new Vector3(0, 1.1f, 0), dt * 0.8f);
			s.P += s.V * dt;
			float size = s.Size * (1.3f + 2.8f * u);
			_smokeMM.SetInstanceTransform(i, new Transform3D(Facing(eye, s.P, Vector3.Up, size, size), s.P));
			lo = lo.Min(s.P); hi = hi.Max(s.P);
			_smokeMM.SetInstanceCustomData(i, new Color(u, -1f, s.Seed, 1f));
		}
		if (hi.X >= lo.X)
		{
			var box = new Aabb(lo - Vector3.One * 4f, hi - lo + Vector3.One * 8f);
			_flameMI.CustomAabb = box;
			_smokeMI.CustomAabb = box;
		}
		_splashLevel = Mathf.Lerp(_splashLevel, Mathf.Clamp(stuck / 20f, 0f, 1f), 1f - Mathf.Exp(-dt * 3f));
		_splashLight.Visible = _splashLevel > 0.02f;
		if (stuck > 0) _splashLight.GlobalPosition = _splashLight.GlobalPosition.Lerp(stuckSum / stuck + Vector3.Up * 0.5f, 1f - Mathf.Exp(-dt * 6f));
		float ms = Time.GetTicksMsec() * 0.001f;
		_splashLight.LightEnergy = _splashLevel * (1.8f + 0.3f * Mathf.Sin(ms * 5.3f) * Mathf.Sin(ms * 3.1f + 1f));
		// the light: in the middle of what's burning, as strong as the fire is big (eased, never flickering on and off)
		float want = Mathf.Clamp(alight / 45f, 0f, 1f);
		_lightLevel = Mathf.Lerp(_lightLevel, want, 1f - Mathf.Exp(-dt * 6f));
		_light.Visible = _lightLevel > 0.02f;
		if (alight > 0) _light.GlobalPosition = _light.GlobalPosition.Lerp(sum / alight, 1f - Mathf.Exp(-dt * 10f));
		_light.LightEnergy = _lightLevel * (2.6f + 0.25f * Mathf.Sin(Time.GetTicksMsec() * 0.0071f) * Mathf.Sin(Time.GetTicksMsec() * 0.0043f));
	}

	/// <summary>A quad's basis facing <paramref name="eye"/> from <paramref name="at"/>, its length (x) along
	/// <paramref name="along"/> as seen from there.</summary>
	private static Basis Facing(Vector3 eye, Vector3 at, Vector3 along, float length, float width)
	{
		var f = eye - at;
		if (f.LengthSquared() < 1e-6f) f = Vector3.Back;
		f = f.Normalized();
		var x = along - f * along.Dot(f);
		if (x.LengthSquared() < 1e-6f) x = Mathf.Abs(f.Y) < 0.9f ? Vector3.Up.Cross(f) : Vector3.Right;
		x = x.Normalized();
		var y = f.Cross(x).Normalized();
		return new Basis(x * length, y * width, f * width);
	}

	private void Smoke(Vector3 at, float size)
	{
		int i = Free(_s);
		if (i < 0) return;
		_s[i] = new Glob { P = at, V = new Vector3(_rng.RandfRange(-0.3f, 0.3f), 0.6f, _rng.RandfRange(-0.3f, 0.3f)), Age = 0f, Life = _rng.RandfRange(2.2f, 3.4f), Size = Mathf.Max(size, 0.6f), Seed = _rng.Randf(), Alive = true };
	}

	/// <summary>How much fire is on a sphere: the burning globs inside it, the hotter (younger) counting more.</summary>
	public float Heat(Vector3 centre, float radius)
	{
		float h = 0f, r2 = radius * radius;
		for (int i = 0; i < MaxGlobs; i++)
		{
			ref var g = ref _g[i];
			if (!g.Alive) continue;
			if (g.P.DistanceSquaredTo(centre) < r2) h += g.Stuck ? 0.4f : 1f;
		}
		return h;
	}
}
