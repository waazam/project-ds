using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Blood that lands on things (2026-10-22, the owner: "build a blood system that looks and acts like blood with a rich red
/// color and a viscosity to it"). <see cref="FlowFx"/> has blood that runs (pours, ropes, the pools' moving surface);
/// this is what's splashed and left: on floors, walls, pipes, a valve's wheel, anything, as decals.
/// <list type="bullet">
/// <item>The look (<see cref="Kind"/>, drawn once and cached): metaballs, so it gathers into beads and lobes the way a
/// thick liquid does under its own surface tension, a sharp edge with a raised, rounded lip (the normal map), darker
/// where it lies thick, a thin bright rim of red at its edge drying darker still, glossy (the ORM's roughness).</item>
/// <item>The acting: a splash spreads out from small to its size, quick then slow (<see cref="Splat"/>'s
/// <c>spread</c>); a run on a wall creeps down it for seconds (<see cref="Run"/>); it dulls and darkens as it dries
/// (<see cref="Dry"/>). The drops flying are lit beads, stretched along their flight (<see cref="Burst"/>).</item>
/// </list>
/// Decals only on layer 1 (the world: not the player's own body or the held things).
/// </summary>
public static class Blood
{
	public enum Kind { Pool, Spray, Drip, Caked }

	/// <summary>The rich red, wet (thin); and lying thick.</summary>
	public static readonly Color Rich = new(0.52f, 0.015f, 0.03f), Thick = new(0.17f, 0.0f, 0.01f);

	private const int N = 256;
	private static readonly Dictionary<Kind, (Texture2D alb, Texture2D nor, Texture2D orm)> _tex = new();

	// ------------------------------------------------------------------ the decals

	/// <summary>A splash on the surface at <paramref name="at"/> facing <paramref name="normal"/>; <paramref name="along"/>
	/// is the way its streaks run (a spray: away from the blow; a run: down). <paramref name="spread"/> seconds spreading
	/// out to its size (0: there already).</summary>
	public static Decal Splat(Node parent, Vector3 at, Vector3 normal, float size, Kind kind, float angle = 0f, Vector3? along = null,
		float spread = 0f, float depth = 0.4f, float length = 1f)
	{
		var (alb, nor, orm) = Textures(kind);
		normal = normal.LengthSquared() > 1e-6f ? normal.Normalized() : Vector3.Up;
		Vector3 z = along ?? Vector3.Zero;
		z -= normal * z.Dot(normal);
		if (z.LengthSquared() < 1e-4f) { z = Mathf.Abs(normal.Y) < 0.9f ? Vector3.Up.Cross(normal) : Vector3.Right.Cross(normal); z = z.Normalized().Rotated(normal, angle); }
		else z = z.Normalized();
		Vector3 x = normal.Cross(z).Normalized();
		var full = new Vector3(size, depth, size * length);
		var d = new Decal
		{
			Name = "Blood", TextureAlbedo = alb, TextureNormal = nor, TextureOrm = orm,
			Size = spread > 0f ? full * new Vector3(0.3f, 1f, 0.3f) : full,
			UpperFade = 0.15f, LowerFade = 0.35f, NormalFade = 0.35f, CullMask = 1,
			DistanceFadeEnabled = true, DistanceFadeBegin = 34f, DistanceFadeLength = 10f,
			Modulate = new Color(1.05f, 1f, 1f),
		};
		parent.AddChild(d);
		d.GlobalTransform = new Transform3D(new Basis(x, normal, z), at);
		if (spread > 0f)
			// (quick at first, then slow: a thick liquid stops spreading as it thins)
			d.CreateTween().TweenProperty(d, "size", full, spread).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
		return d;
	}

	/// <summary>A run down a wall: the splash at <paramref name="top"/>, its runs creeping down it over
	/// <paramref name="seconds"/>, slowing as they go, to <paramref name="length"/> metres.</summary>
	public static Decal Run(Node parent, Vector3 top, Vector3 normal, float width, float length, float seconds)
	{
		normal = normal.LengthSquared() > 1e-6f ? normal.Normalized() : Vector3.Back;
		Vector3 down = Vector3.Down - normal * Vector3.Down.Dot(normal);
		if (down.LengthSquared() < 1e-4f) down = Vector3.Forward;
		down = down.Normalized();
		var d = Splat(parent, top + down * length * 0.5f, normal, width, Kind.Drip, along: down, length: length / width);
		if (seconds <= 0f) return d;
		var size = d.Size;
		d.Size = size with { Z = size.Z * 0.3f };
		d.GlobalPosition = top + down * size.Z * 0.15f;
		d.CreateTween().TweenMethod(Callable.From<float>(k =>
		{
			if (!GodotObject.IsInstanceValid(d)) return;
			float len = size.Z * k;
			d.Size = size with { Z = len };
			d.GlobalPosition = top + down * len * 0.5f;
		}), 0.3f, 1f, seconds).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		return d;
	}

	/// <summary>Dulls and darkens it over <paramref name="seconds"/>, the way it dries.</summary>
	public static void Dry(Decal d, float seconds, float to = 0.62f)
	{
		if (d == null || !GodotObject.IsInstanceValid(d)) return;
		d.CreateTween().TweenProperty(d, "modulate", new Color(to, to * 0.92f, to * 0.92f), seconds);
	}

	// ------------------------------------------------------------------ the drops in flight

	private static StandardMaterial3D _bead;
	/// <summary>A drop: dark, glossy, lit (a bead of it catches the light, not a flat red dot).</summary>
	public static StandardMaterial3D BeadMat => _bead ??= new StandardMaterial3D
	{
		ResourceName = "blood_bead", AlbedoColor = new Color(0.34f, 0.006f, 0.02f), Roughness = 0.08f, MetallicSpecular = 0.85f,
		ClearcoatEnabled = true, Clearcoat = 0.6f, ClearcoatRoughness = 0.05f, RimEnabled = true, Rim = 0.25f, RimTint = 0.8f,
	};

	/// <summary>Drops thrown off a blow at <paramref name="at"/>, outward round <paramref name="dir"/>: beads stretched
	/// along their flight, and a few long slow strands (it's thick: it comes off in ropes as well as drops).</summary>
	public static void Burst(Node parent, Vector3 at, Vector3 dir, int amount = 60, float speed = 6f, float spread = 65f)
	{
		Emit(parent, at, dir, amount, speed * 0.5f, speed, spread, new SphereMesh { Radius = 0.022f, Height = 0.075f, RadialSegments = 6, Rings = 3, Material = BeadMat }, 1.4, 0.35f, 1.5f);
		Emit(parent, at, dir, Mathf.Max(4, amount / 6), speed * 0.35f, speed * 0.7f, spread * 0.7f, new CapsuleMesh { Radius = 0.012f, Height = 0.32f, RadialSegments = 5, Rings = 2, Material = BeadMat }, 1.2, 0.7f, 1.2f);
	}

	private static void Emit(Node parent, Vector3 at, Vector3 dir, int amount, float v0, float v1, float spread, Mesh mesh, double life, float s0, float s1)
	{
		var pm = new ParticleProcessMaterial
		{
			Direction = dir.LengthSquared() > 1e-6f ? dir.Normalized() : Vector3.Up, Spread = spread,
			InitialVelocityMin = v0, InitialVelocityMax = v1, Gravity = new Vector3(0, -11f, 0), DampingMin = 0.2f, DampingMax = 0.8f,
			ScaleMin = s0, ScaleMax = s1, ParticleFlagAlignY = true,
		};
		var p = new GpuParticles3D
		{
			Name = "BloodBurst", Amount = amount, Lifetime = life, OneShot = true, Explosiveness = 0.92f, Randomness = 0.6f, ProcessMaterial = pm,
			DrawPass1 = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, LocalCoords = false,
			VisibilityAabb = new Aabb(new Vector3(-8, -8, -8), new Vector3(16, 16, 16)),
		};
		parent.AddChild(p);
		p.GlobalPosition = at;
		p.Emitting = true;
		p.GetTree().CreateTimer(life + 0.6).Timeout += () => { if (GodotObject.IsInstanceValid(p)) p.QueueFree(); };
	}

	// ------------------------------------------------------------------ the look, drawn once

	/// <summary>Draws (or loads) every kind's textures now (a load's black, not the first splash).</summary>
	public static void Prewarm() { foreach (Kind k in System.Enum.GetValues(typeof(Kind))) Textures(k); }

	private static (Texture2D, Texture2D, Texture2D) Textures(Kind kind)
	{
		if (_tex.TryGetValue(kind, out var t)) return t;
		string key = $"Blood_{kind}_v2";
		Image alb = TexCache.Load(key + "_alb"), nor = TexCache.Load(key + "_nor"), orm = TexCache.Load(key + "_orm");
		if (alb == null || nor == null || orm == null)
		{
			ulong t0 = TexGen.Start();
			(alb, nor, orm) = Draw(kind);
			TexGen.Stop(t0);
			TexCache.Save(key + "_alb", alb); TexCache.Save(key + "_nor", nor); TexCache.Save(key + "_orm", orm);
		}
		t = (ImageTexture.CreateFromImage(alb), ImageTexture.CreateFromImage(nor), ImageTexture.CreateFromImage(orm));
		_tex[kind] = t;
		return t;
	}

	/// <summary>The density of the metaballs (a blob's centre 1, 0.5 at its radius); the blood's edge is where it's 0.5.</summary>
	private sealed class Field
	{
		public readonly float[] D = new float[N * N];
		public void Ball(float cx, float cy, float r, float w = 1f)
		{
			if (r <= 0.001f) return;
			int x0 = Mathf.Max(0, (int)((cx - r * 2.6f) * N)), x1 = Mathf.Min(N - 1, (int)((cx + r * 2.6f) * N) + 1);
			int y0 = Mathf.Max(0, (int)((cy - r * 2.6f) * N)), y1 = Mathf.Min(N - 1, (int)((cy + r * 2.6f) * N) + 1);
			float inv = 1f / (r * r);
			for (int y = y0; y <= y1; y++)
			{
				float dy = (y + 0.5f) / N - cy;
				for (int x = x0; x <= x1; x++)
				{
					float dx = (x + 0.5f) / N - cx;
					D[y * N + x] += w * Mathf.Exp(-0.693f * (dx * dx + dy * dy) * inv);
				}
			}
		}
		/// <summary>A line of balls from a to b, its radius r0 to r1, wobbling a little, with a bead on its end.</summary>
		public void Streak(Vector2 a, Vector2 b, float r0, float r1, RandomNumberGenerator rng, float bead = 1.7f)
		{
			float len = a.DistanceTo(b);
			int n = Mathf.Max(2, (int)(len / (Mathf.Min(r0, r1) * 0.7f)));
			Vector2 side = (b - a).Orthogonal().Normalized();
			float ph = rng.Randf() * 6f;
			for (int i = 0; i <= n; i++)
			{
				float k = i / (float)n;
				var p = a.Lerp(b, k) + side * Mathf.Sin(k * 7f + ph) * r0 * 0.4f;
				Ball(p.X, p.Y, Mathf.Lerp(r0, r1, k) * (0.9f + 0.2f * rng.Randf()));
			}
			if (bead > 0f) Ball(b.X, b.Y, r1 * bead);
		}
	}

	private static (Image, Image, Image) Draw(Kind kind)
	{
		var rng = new RandomNumberGenerator { Seed = 7700 + (ulong)kind };
		var f = new Field();
		var c = new Vector2(0.5f, 0.5f);
		switch (kind)
		{
			case Kind.Pool:
			case Kind.Caked:
			{
				bool caked = kind == Kind.Caked;
				f.Ball(0.5f, 0.5f, caked ? 0.2f : 0.17f, 1.3f);
				// lobes round it (it spread unevenly)
				int lobes = caked ? 14 : 7;
				for (int i = 0; i < lobes; i++)
				{
					float a = rng.Randf() * Mathf.Tau, r = rng.RandfRange(0.08f, caked ? 0.3f : 0.2f);
					f.Ball(0.5f + Mathf.Cos(a) * r, 0.5f + Mathf.Sin(a) * r, rng.RandfRange(0.05f, caked ? 0.12f : 0.1f));
				}
				// fingers out of it, ending in beads
				int fingers = caked ? 10 : 8;
				for (int i = 0; i < fingers; i++)
				{
					float a = rng.Randf() * Mathf.Tau, r0 = rng.RandfRange(0.15f, 0.22f), r1 = rng.RandfRange(0.3f, 0.44f);
					var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
					f.Streak(c + dir * r0, c + dir * r1, rng.RandfRange(0.02f, 0.035f), rng.RandfRange(0.008f, 0.016f), rng);
				}
				// flung drops round the outside
				for (int i = 0; i < (caked ? 50 : 26); i++)
				{
					float a = rng.Randf() * Mathf.Tau, r = rng.RandfRange(0.28f, 0.47f);
					f.Ball(0.5f + Mathf.Cos(a) * r, 0.5f + Mathf.Sin(a) * r, rng.RandfRange(0.005f, 0.018f));
				}
				break;
			}
			case Kind.Spray:
			{
				// the blow's end at the top: drops thrown down the texture (along the decal's +Z), long, tapering, beaded
				f.Ball(0.5f, 0.14f, 0.09f, 1.2f);
				for (int i = 0; i < 4; i++) f.Ball(0.5f + rng.RandfRange(-0.12f, 0.12f), 0.14f + rng.RandfRange(-0.05f, 0.08f), rng.RandfRange(0.03f, 0.06f));
				for (int i = 0; i < 16; i++)
				{
					float a = rng.RandfRange(-0.5f, 0.5f), len = rng.RandfRange(0.18f, 0.78f);
					var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
					var s = new Vector2(0.5f, 0.15f) + dir * 0.06f;
					f.Streak(s, s + dir * len, rng.RandfRange(0.012f, 0.026f), rng.RandfRange(0.005f, 0.01f), rng, rng.RandfRange(1.4f, 2.4f));
				}
				for (int i = 0; i < 40; i++)
				{
					float a = rng.RandfRange(-0.7f, 0.7f), r = rng.RandfRange(0.15f, 0.85f);
					f.Ball(0.5f + Mathf.Sin(a) * r, 0.15f + Mathf.Cos(a) * r, rng.RandfRange(0.004f, 0.012f));
				}
				break;
			}
			case Kind.Drip:
			{
				// a splash at the top, its runs straight down the texture (the decal's +Z is down the wall), each a bead
				// at its foot where it gathered
				f.Ball(0.5f, 0.08f, 0.1f, 1.2f);
				for (int i = 0; i < 5; i++) f.Ball(0.5f + rng.RandfRange(-0.2f, 0.2f), 0.08f + rng.RandfRange(-0.03f, 0.04f), rng.RandfRange(0.03f, 0.07f));
				int runs = rng.RandiRange(4, 6);
				for (int i = 0; i < runs; i++)
				{
					float x = 0.5f + rng.RandfRange(-0.28f, 0.28f), end = rng.RandfRange(0.4f, 0.93f);
					f.Streak(new Vector2(x, 0.1f), new Vector2(x + rng.RandfRange(-0.02f, 0.02f), end), rng.RandfRange(0.018f, 0.03f), rng.RandfRange(0.014f, 0.022f), rng, 1.6f);
				}
				break;
			}
		}

		var noise = new FastNoiseLite { Seed = 77 + (int)kind, Frequency = 0.05f };
		var h = new float[N * N];
		for (int i = 0; i < N * N; i++)
		{
			float d = f.D[i];
			// thickness: a steep lip at the edge, then flat across the top (surface tension), deeper where it piled up
			float lip = Mathf.SmoothStep(0.5f, 0.68f, d);
			h[i] = lip * (0.55f + 0.45f * Mathf.Clamp((d - 0.68f) / 1.4f, 0f, 1f));
		}
		var albB = new byte[N * N * 4];
		var norB = new byte[N * N * 4];
		var ormB = new byte[N * N * 4];
		for (int y = 0; y < N; y++)
			for (int x = 0; x < N; x++)
			{
				int i = y * N + x;
				float d = f.D[i];
				float alpha = Mathf.SmoothStep(0.46f, 0.52f, d);
				float th = h[i];
				float nz = noise.GetNoise2D(x, y);
				// thin: the rich red; thick: near black-red; the very edge a dried ring a shade darker
				var col = Rich.Lerp(Thick, Mathf.SmoothStep(0.4f, 1f, th) * 0.7f);   // (2026-10-23: rich red through most of it, dark only where it lies deepest; it read near black)
				float ring = Mathf.SmoothStep(0.46f, 0.52f, d) * (1f - Mathf.SmoothStep(0.52f, 0.62f, d));
				float dark = 1f - ring * 0.35f + nz * 0.08f;
				if (kind == Kind.Caked) dark *= 0.8f + 0.2f * noise.GetNoise2D(x * 3f, y * 3f);
				albB[i * 4] = (byte)(Mathf.Clamp(col.R * dark, 0f, 1f) * 255f);
				albB[i * 4 + 1] = (byte)(Mathf.Clamp(col.G * dark, 0f, 1f) * 255f);
				albB[i * 4 + 2] = (byte)(Mathf.Clamp(col.B * dark, 0f, 1f) * 255f);
				albB[i * 4 + 3] = (byte)(alpha * 0.97f * 255f);
				float hx = h[y * N + Mathf.Min(N - 1, x + 1)] - h[y * N + Mathf.Max(0, x - 1)];
				float hy = h[Mathf.Min(N - 1, y + 1) * N + x] - h[Mathf.Max(0, y - 1) * N + x];
				var n = new Vector3(-hx * 9f, -hy * 9f, 1f).Normalized();
				norB[i * 4] = (byte)((n.X * 0.5f + 0.5f) * 255f);
				norB[i * 4 + 1] = (byte)((n.Y * 0.5f + 0.5f) * 255f);
				norB[i * 4 + 2] = (byte)((n.Z * 0.5f + 0.5f) * 255f);
				norB[i * 4 + 3] = albB[i * 4 + 3];
				// glossy where it's wet and thick; duller at the drying rim; caked, duller all over
				float rough = Mathf.Lerp(0.42f, 0.05f, Mathf.SmoothStep(0f, 0.6f, th)) + ring * 0.2f + (kind == Kind.Caked ? 0.18f : 0f);
				ormB[i * 4] = 255;
				ormB[i * 4 + 1] = (byte)(Mathf.Clamp(rough, 0f, 1f) * 255f);
				ormB[i * 4 + 2] = 0;
				ormB[i * 4 + 3] = albB[i * 4 + 3];
			}
		var alb = Image.CreateFromData(N, N, false, Image.Format.Rgba8, albB);
		var nor = Image.CreateFromData(N, N, false, Image.Format.Rgba8, norB);
		var orm = Image.CreateFromData(N, N, false, Image.Format.Rgba8, ormB);
		alb.GenerateMipmaps(); nor.GenerateMipmaps(); orm.GenerateMipmaps();
		return (alb, nor, orm);
	}
}
