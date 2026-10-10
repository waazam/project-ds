using System.Collections.Generic;
using Godot;
using ProjectDS.World.BossParts;

namespace ProjectDS.World;

/// <summary>
/// The catwalk takes the slams (2026-10-10, the owner: "If we can have the catwalk in act 18 get visibly damaged when
/// the monster slams the catwalk"). Where a limb comes down:
/// - the grating is dented: a dark, crumpled dish pressed into it (a decal, with its own normal map), scuffed bright
///   where the steel was scraped;
/// - torn strips of grating curl up round the dent's rim;
/// - the railing's span beside it is bent, sagging down and out over the pit, with a groan of steel after the slam;
/// - a short spray of sparks off the steel.
/// It stays walkable: nothing new collides, and the guard over the pit is untouched. The marks stay for the fight
/// (the oldest go once there are many).
/// </summary>
public partial class BossRoom
{
	private readonly List<(MeshInstance3D mi, Vector3 a, Vector3 b)> _spans = new();
	private readonly Queue<Node3D> _dents = new();
	private readonly HashSet<MeshInstance3D> _bent = new();
	private static ImageTexture _dentAlbedo, _dentNormal;
	private const int MaxDents = 22;

	private void Damage(Vector3 world)
	{
		Vector3 at = ToLocal(world);
		at.Y = 0f;
		var dent = new Node3D { Name = "Dent", Position = at };
		AddChild(dent);
		float size = _rng.RandfRange(2.0f, 2.8f);
		MakeDentTextures();
		dent.AddChild(new Decal
		{
			Size = new Vector3(size, 0.6f, size), TextureAlbedo = _dentAlbedo, TextureNormal = _dentNormal,
			RotationDegrees = new Vector3(0, _rng.RandfRange(0, 360), 0), UpperFade = 0.2f, LowerFade = 0.2f,
			CullMask = 1, NormalFade = 0.4f,
		});
		// torn strips of grating, curling up round the rim (no collision: they're ankle-high scraps)
		var k = new MeshKit();
		k.Mat(BossTextures.GratingMat);
		k.Color = Colors.White * 0.7f;
		int strips = _rng.RandiRange(2, 4);
		for (int i = 0; i < strips; i++)
		{
			float a = _rng.RandfRange(0, Mathf.Tau), r = size * _rng.RandfRange(0.28f, 0.4f);
			Vector3 dir = new(Mathf.Cos(a), 0, Mathf.Sin(a));
			Vector3 side = new(-dir.Z, 0, dir.X);
			Vector3 root = dir * r;
			if (!OnDeck(at + root)) continue;
			float len = _rng.RandfRange(0.25f, 0.5f), lift = _rng.RandfRange(0.25f, 0.6f);
			// two bends: flat out of the deck, then up
			Vector3 p1 = root + dir * len * 0.4f + Vector3.Up * 0.03f;
			Vector3 p2 = p1 + (dir * Mathf.Cos(lift) + Vector3.Up * Mathf.Sin(lift)) * len * 0.6f;
			Strip(k, root + Vector3.Up * 0.01f, p1, side, 0.16f);
			Strip(k, p1, p2, side, 0.14f);
		}
		k.CommitTo(dent, "Torn", false);
		BendNearestSpan(at);
		Sparks(world);
		_dents.Enqueue(dent);
		while (_dents.Count > MaxDents) _dents.Dequeue().QueueFree();
	}

	private bool OnDeck(Vector3 p)
	{
		float m = Mathf.Max(Mathf.Abs(p.X), Mathf.Abs(p.Z));
		return m > CatIn + 0.05f && m < Half - 0.05f;
	}

	private static void Strip(MeshKit k, Vector3 a, Vector3 b, Vector3 side, float w)
	{
		Vector3 d = b - a;
		Vector3 n = side.Cross(d).Normalized();
		if (n.Y < 0) n = -n;
		Vector3 h = side * w * 0.5f;
		k.Quad(a - h, a + h, b + h, b - h, n, new Vector2(0, 0), new Vector2(w * 4f, 0), new Vector2(w * 4f, d.Length() * 4f), new Vector2(0, d.Length() * 4f));
		k.Quad(a + h, a - h, b - h, b + h, -n, new Vector2(0, 0), new Vector2(w * 4f, 0), new Vector2(w * 4f, d.Length() * 4f), new Vector2(0, d.Length() * 4f));
	}

	/// <summary>The railing's span nearest the slam, bent: sagging down in the middle and pushed out over the pit.</summary>
	private void BendNearestSpan(Vector3 at)
	{
		MeshInstance3D best = null;
		Vector3 ba = default, bb = default;
		float bestD = 2.6f;
		foreach (var (mi, a, b) in _spans)
		{
			float d = new Vector2(((a + b) * 0.5f).X - at.X, ((a + b) * 0.5f).Z - at.Z).Length();
			if (d < bestD) { bestD = d; best = mi; ba = a; bb = b; }
		}
		if (best == null || _bent.Contains(best)) return;
		_bent.Add(best);
		Vector3 mid = (ba + bb) * 0.5f;
		Vector3 outw = -new Vector3(mid.X, 0, mid.Z).Normalized();   // the pit's side
		float sag = _rng.RandfRange(0.28f, 0.45f), push = _rng.RandfRange(0.12f, 0.3f);
		var k = new MeshKit();
		k.Mat(BossTextures.GalvanizedMat);
		k.Color = Colors.White;
		foreach (var (hgt, r, s) in new[] { (1.1f, 0.03f, 1f), (0.55f, 0.022f, 0.6f) })
		{
			Vector3 Bent(float u)
			{
				float bump = Mathf.Max(0f, Mathf.Sin(u * Mathf.Pi));
				// a sharp kink where it was hit, not a smooth sag
				float kink = Mathf.Pow(bump, 0.6f);
				return ba.Lerp(bb, u) + Vector3.Up * (hgt - sag * s * kink) + outw * push * s * kink;
			}
			Vector3 prev = Bent(0f);
			for (int i = 1; i <= 6; i++)
			{
				Vector3 p = Bent(i / 6f);
				k.Cylinder(prev, p, r, r, 8, false);
				k.Blob(p, Vector3.One * r * 1.05f, i, 0f, false);
				prev = p;
			}
		}
		var bent = k.CommitTo(this, best.Name + "Bent", true);
		best.Visible = false;
		var groan = SfxPlayer("lodge_rail_groan", 2, ToGlobal(mid), -2f, 8f);
		if (groan != null) { groan.PitchScale = _rng.RandfRange(0.55f, 0.7f); }
		_ = bent;
	}

	private void Sparks(Vector3 world)
	{
		var pm = new ParticleProcessMaterial
		{
			Direction = Vector3.Up, Spread = 70f, InitialVelocityMin = 2.5f, InitialVelocityMax = 6f, Gravity = new Vector3(0, -12f, 0),
			ScaleMin = 0.5f, ScaleMax = 1f, Color = new Color(1f, 0.62f, 0.25f),
		};
		var p = new GpuParticles3D
		{
			Amount = 22, Lifetime = 0.7, OneShot = true, Explosiveness = 0.95f, ProcessMaterial = pm,
			DrawPass1 = new QuadMesh
			{
				Size = Vector2.One * 0.05f,
				Material = new StandardMaterial3D
				{
					AlbedoColor = new Color(1f, 0.6f, 0.25f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
					BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
				},
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityAabb = new Aabb(new Vector3(-6, -4, -6), new Vector3(12, 8, 12)),
		};
		AddChild(p);
		p.GlobalPosition = world + Vector3.Up * 0.1f;
		p.Emitting = true;
		GetTree().CreateTimer(1.5).Timeout += p.QueueFree;
	}

	/// <summary>The dent's look, drawn once: a dark crumpled dish (its normal map pressed in, creased radially), the
	/// rim scraped bright, fading out to nothing at the edge.</summary>
	private static void MakeDentTextures()
	{
		if (_dentAlbedo != null) return;
		const int n = 128;
		var noise = new FastNoiseLite { Seed = 1818, Frequency = 0.08f };
		var h = new float[n, n];
		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
				float r = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
				float dish = -Mathf.Max(0f, 1f - r * r * 1.4f);
				float crease = 0.18f * Mathf.Sin(a * 9f + noise.GetNoise2D(x, y) * 3f) * Mathf.Max(0f, 1f - r) * r * 2f;
				h[x, y] = dish + crease + noise.GetNoise2D(x * 2f, y * 2f) * 0.08f;
			}
		var alb = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
		var nor = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
				float r = Mathf.Sqrt(u * u + v * v);
				float alpha = Mathf.Clamp(1f - Mathf.SmoothStep(0.62f, 0.98f, r), 0f, 1f);
				float scrape = Mathf.SmoothStep(0.45f, 0.6f, r) * (1f - Mathf.SmoothStep(0.6f, 0.75f, r)) * (0.5f + 0.5f * noise.GetNoise2D(x * 3f, y * 3f));
				float dark = 0.12f + 0.1f * noise.GetNoise2D(x * 1.5f + 50, y * 1.5f);
				float g = Mathf.Lerp(dark, 0.42f, Mathf.Clamp(scrape, 0f, 1f));
				alb.SetPixel(x, y, new Color(g * 1.02f, g, g * 0.95f, alpha * 0.92f));
				float hx = h[Mathf.Min(n - 1, x + 1), y] - h[Mathf.Max(0, x - 1), y];
				float hy = h[x, Mathf.Min(n - 1, y + 1)] - h[x, Mathf.Max(0, y - 1)];
				var nv = new Vector3(-hx * 6f, -hy * 6f, 1f).Normalized();
				nor.SetPixel(x, y, new Color(nv.X * 0.5f + 0.5f, nv.Y * 0.5f + 0.5f, nv.Z * 0.5f + 0.5f, alpha));
			}
		alb.GenerateMipmaps();
		nor.GenerateMipmaps();
		_dentAlbedo = ImageTexture.CreateFromImage(alb);
		_dentNormal = ImageTexture.CreateFromImage(nor);
	}
}
