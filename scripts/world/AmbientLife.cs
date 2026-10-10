using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// The woods reacting (2026-10-10):
/// <list type="bullet">
/// <item><b>Birds flushing</b>: when the thing that follows you takes a new tree near you, now and then a few birds go up
/// out of the canopy over it all at once, wings clattering, and are gone. Rare (a minute at least between, and only a
/// third of its moves), and never anywhere else: when the birds go up, it's there.</item>
/// <item><b>Frogs</b> (<see cref="FrogChorus"/>): along the lake's shore at night they croak, and go quiet as you come near,
/// the way frogs do; and all of them at once when something in the water moves.</item>
/// <item><b>Moths</b> (<see cref="LanternMoths"/>): on summer nights in the woods, two or three moths find the lantern and
/// circle it, batting at the glass.</item>
/// </list>
/// </summary>
public static class AmbientLife
{
	private static double _lastBirds = -999;

	/// <summary>Maybe a few birds go up from the trees over <paramref name="at"/> (rate-limited, a chance each call).</summary>
	public static bool MaybeBirds(Node owner, Vector3 at, float chance = 0.33f)
	{
		double now = Time.GetTicksMsec() / 1000.0;
		if (now - _lastBirds < 60.0 || GD.Randf() > chance) return false;
		_lastBirds = now;
		Birds(owner, at);
		return true;
	}

	public static void Birds(Node owner, Vector3 at)
	{
		var root = owner.GetTree().CurrentScene ?? owner.GetTree().Root;
		var from = at + Vector3.Up * (float)GD.RandRange(6.0, 9.0);
		var pm = new ParticleProcessMaterial
		{
			Direction = new Vector3((float)GD.RandRange(-0.4, 0.4), 1f, (float)GD.RandRange(-0.4, 0.4)), Spread = 35f,
			InitialVelocityMin = 4f, InitialVelocityMax = 7f, Gravity = new Vector3(0, 0.8f, 0), DampingMin = 0.3f, DampingMax = 0.6f,
			EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 1.5f, ScaleMin = 0.7f, ScaleMax = 1.1f,
			TurbulenceEnabled = true, TurbulenceNoiseStrength = 1.4f, TurbulenceNoiseScale = 3f,
		};
		var p = new GpuParticles3D
		{
			Name = "Birds", Amount = (int)GD.RandRange(5, 9), Lifetime = 3.2, OneShot = true, Explosiveness = 0.75f, ProcessMaterial = pm,
			DrawPass1 = new QuadMesh
			{
				Size = new Vector2(0.32f, 0.12f),
				Material = new StandardMaterial3D
				{
					AlbedoTexture = BirdTexture(), AlbedoColor = new Color(0.05f, 0.05f, 0.05f), Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
					BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				},
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityAabb = new Aabb(new Vector3(-20, -5, -20), new Vector3(40, 40, 40)),
		};
		root.AddChild(p);
		p.GlobalPosition = from;
		p.Emitting = true;
		AudioDirector.OneShot(owner, "wing_flutter", 3, from, 0f, "Birds", 8f, 0.08f);
		AudioDirector.OneShot(owner, "wing_flutter", 3, from + Vector3.Right * 2f, -4f, "Birds", 8f, 0.12f);
		owner.GetTree().CreateTimer(4.0).Timeout += p.QueueFree;
		GD.Print("[life] birds flushed from the trees");
	}

	private static ImageTexture _bird;

	/// <summary>A small black bird in flight, wings up in a shallow V (drawn once).</summary>
	private static ImageTexture BirdTexture()
	{
		if (_bird != null) return _bird;
		const int w = 32, h = 12;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int x = 0; x < w; x++)
		{
			float u = (x - w * 0.5f) / (w * 0.5f);
			float wingY = h * 0.35f + Mathf.Abs(u) * -h * 0.25f + u * u * h * 0.4f;
			float thick = 1.6f * (1f - Mathf.Abs(u) * 0.7f) + (Mathf.Abs(u) < 0.15f ? 1.5f : 0f);
			for (int y = 0; y < h; y++)
				if (Mathf.Abs(y - wingY) < thick) img.SetPixel(x, y, Colors.White);
		}
		return _bird = ImageTexture.CreateFromImage(img);
	}
}

/// <summary>Frogs along a shore at night: they hush as you come near (and come back once you've gone a while), and all
/// go quiet together when something moves in the water (<see cref="Hush"/>).</summary>
public partial class FrogChorus : Node3D
{
	public float NearQuiet = 10f;
	public float Db = -9f;
	private AudioStreamPlayer3D _p;
	private double _quietUntil;
	private static double _hushUntil;
	public bool Quiet => _p == null || _p.VolumeDb < Db - 20f;

	/// <summary>Something moved in the water: every frog goes still for a while.</summary>
	public static void Hush(float seconds) => _hushUntil = Time.GetTicksMsec() / 1000.0 + seconds;

	public override void _Ready()
	{
		if (!ResourceLoader.Exists("res://assets/audio/ambient/frogs_loop.wav")) return;
		var wav = (AudioStreamWav)GD.Load<AudioStreamWav>("res://assets/audio/ambient/frogs_loop.wav").Duplicate();
		wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
		wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
		_p = new AudioStreamPlayer3D { Name = "Frogs", Stream = wav, Bus = "Nature", VolumeDb = -80f, UnitSize = 6f, MaxDistance = 60f };
		AddChild(_p);
		_p.Play((float)GD.RandRange(0.0, wav.GetLength()));
	}

	public override void _Process(double delta)
	{
		if (_p == null) return;
		var player = StoryBeat.Player(this);
		double now = Time.GetTicksMsec() / 1000.0;
		if (player != null && player.GlobalPosition.DistanceTo(GlobalPosition) < NearQuiet) _quietUntil = now + 12.0;
		bool quiet = now < _quietUntil || now < _hushUntil;
		// they stop at once (a last croak or two), and come back one by one, slowly
		_p.VolumeDb = Mathf.MoveToward(_p.VolumeDb, quiet ? -80f : Db, (float)delta * (quiet ? 40f : 6f));
	}
}

/// <summary>Moths at the lantern on a summer night in the woods: two or three, circling its glass, darting, batting at
/// it. Only while it's lit, outdoors, in the summer woods, and not in the rain.</summary>
public partial class LanternMoths : Node3D
{
	private readonly MeshInstance3D[] _moths = new MeshInstance3D[3];
	private readonly float[] _phase = new float[3], _r = new float[3], _speed = new float[3];
	private float _t, _shown;
	private PlayerController _player;
	private Lantern _lantern;

	public static bool Season => StoryManager.Instance is { } s && s.Current >= Checkpoint.Act2StairsClimbed && s.Current < Checkpoint.Act12LakeCrossed
		&& !(Audio.ForestAmbienceManager.Instance?.IsIndoor ?? false) && (RainVfx.Instance?.Intensity ?? 0f) < 0.2f;

	public override void _Ready()
	{
		_player = GetParent() as PlayerController;
		var mat = new StandardMaterial3D
		{
			ResourceName = "moth", AlbedoColor = new Color(0.62f, 0.56f, 0.46f), Roughness = 1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, AlbedoTexture = MothTexture(),
		};
		for (int i = 0; i < _moths.Length; i++)
		{
			_moths[i] = new MeshInstance3D { Name = $"Moth{i}", Mesh = new QuadMesh { Size = new Vector2(0.035f, 0.025f) }, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
			AddChild(_moths[i]);
			_phase[i] = i * 2.1f;
			_r[i] = 0.16f + 0.07f * i;
			_speed[i] = 3.2f + 0.9f * i;
		}
		TopLevel = true;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_t += dt;
		_lantern ??= _player?.GetNodeOrNull<Lantern>("Lantern");
		bool want = _lantern is { IsOn: true, Blacklight: false } && Season;
		_shown = Mathf.MoveToward(_shown, want ? 1f : 0f, dt * 0.25f);
		int n = Mathf.Clamp(Mathf.CeilToInt(_shown * 3f - 0.01f), 0, 3);
		var centre = _lantern?.HeldFlameAt ?? (_player?.GlobalPosition + Vector3.Up * 1.1f) ?? GlobalPosition;
		for (int i = 0; i < _moths.Length; i++)
		{
			var m = _moths[i];
			m.Visible = i < n;
			if (!m.Visible) continue;
			// round and round the glass, bobbing, now and then darting in to bump it
			float a = _t * _speed[i] + _phase[i] + Mathf.Sin(_t * 1.7f + i) * 0.8f;
			float r = _r[i] * (1f - 0.6f * Mathf.Max(0f, Mathf.Sin(_t * 2.3f + i * 1.3f) - 0.8f) * 5f);
			var p = centre + new Vector3(Mathf.Cos(a) * r, 0.05f + Mathf.Sin(_t * 3.1f + i) * 0.07f, Mathf.Sin(a) * r);
			m.GlobalPosition = p;
			var cam = GetViewport().GetCamera3D();
			if (cam != null && p.DistanceSquaredTo(cam.GlobalPosition) > 0.0001f)
				m.LookAt(cam.GlobalPosition, Vector3.Up);
			// the wings: a fast flutter
			m.Scale = new Vector3(0.4f + 0.6f * Mathf.Abs(Mathf.Sin(_t * 38f + i)), 1f, 1f);
		}
	}

	private static ImageTexture _tex;
	private static ImageTexture MothTexture()
	{
		if (_tex != null) return _tex;
		const int w = 16, h = 12;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int x = 0; x < w; x++)
			for (int y = 0; y < h; y++)
			{
				float u = (x - w * 0.5f + 0.5f) / (w * 0.5f), v = (y - h * 0.5f + 0.5f) / (h * 0.5f);
				bool body = Mathf.Abs(u) < 0.13f && Mathf.Abs(v) < 0.8f;
				bool wing = Mathf.Abs(u) > 0.1f && (u * u * 0.8f + (v + 0.15f * Mathf.Abs(u)) * (v + 0.15f * Mathf.Abs(u)) * 1.4f) < 1f;
				if (body || wing) img.SetPixel(x, y, new Color(body ? 0.6f : 1f, body ? 0.55f : 1f, body ? 0.5f : 1f, 1f));
			}
		return _tex = ImageTexture.CreateFromImage(img);
	}
}
