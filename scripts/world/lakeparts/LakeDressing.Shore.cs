using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.LakeParts;

/// <summary>
/// The lake's character (2026-10-03, the owner's photos of a lake by them: "The lake needs some love ... include that
/// around the lake to give it some character"):
/// <list type="bullet">
/// <item>bald cypresses at the water's edge and standing out in it: their trunks flaring wide at the foot, their roots
/// heaved up out of the mud in a tangle and running down into the water, knees poking up round them, feathery boughs
/// and the moss hanging thick off them;</item>
/// <item>the wrack: a band of dead reed straw washed up along the waterline and dried pale, a few leaves in it, and
/// drift: bleached branches and root wads stranded in it;</item>
/// <item>reed beds: tall, dense stands of reed out in the shallows, their tops gone gold;</item>
/// <item>marsh grass: bright mats of it lying out over the water, flattened, the tips trailing in;</item>
/// <item>a birdhouse on a cypress by the landing, and a warning sign on two posts out in the marsh.</item>
/// </list>
/// </summary>
public partial class LakeDressing
{
	/// <summary>Points round the lake at a given distance from the shoreline (negative: out in the water), from an angle
	/// round its centre (radians from +z).</summary>
	private static Vector2 ShorePoint(float th, float d)
	{
		Vector2 dir = new(Mathf.Sin(th), Mathf.Cos(th));
		Vector2 p = new(0f, LakeShape.CenterZ);
		for (int i = 0; i < 600 && LakeShape.ShoreDist(p.X, p.Y) < d; i++) p += dir * 0.4f;
		return p;
	}

	private bool NearLanding(Vector2 p) => Mathf.Abs(p.X) < 7f && (p.Y > LakeShape.DockEndZ - 4f || p.Y < LakeShape.FarShoreZ + 6f);

	/// <summary>For tests and previews: where the cypresses stand (world), and how many of each of the shore's pieces.</summary>
	public List<Vector3> CypressSpots { get; } = new();
	public int WrackCount { get; private set; }
	public int ReedBedCount { get; private set; }

	private void BuildShoreCharacter()
	{
		_meshes["cypress_a"] = CypressMesh(1, 13f);
		_meshes["cypress_b"] = CypressMesh(2, 17f);
		_meshes["cypress_c"] = CypressMesh(3, 10f);
		_meshes["reed_tall"] = TallReedMesh();
		_meshes["marsh_mat"] = MarshMatMesh();
		_meshes["wrack"] = WrackMesh();
		_meshes["drift"] = DriftMesh(5);
		_meshes["rootwad"] = RootWadMesh(6);
		var rng = new RandomNumberGenerator { Seed = 4710 };
		var clump = new FastNoiseLite { Seed = 4711, Frequency = 0.06f };

		// the cypresses: in groups along the shore, some with their feet in the water
		for (int i = 0; i < 46; i++)
		{
			float th = rng.RandfRange(-Mathf.Pi, Mathf.Pi);
			var p = ShorePoint(th, rng.RandfRange(-3.2f, 2.2f));
			if (NearLanding(p) || KeepOpen(p.X, p.Y, -6f)) continue;
			string key = rng.Randf() < 0.4f ? "cypress_a" : rng.Randf() < 0.6f ? "cypress_b" : "cypress_c";
			float s = rng.RandfRange(0.85f, 1.2f);
			var pos = new Vector3(p.X, Mathf.Max(LakeShape.Ground(p.X, p.Y), -0.9f) - 0.15f, p.Y);
			Add(key, pos, new Basis(Vector3.Up, rng.RandfRange(0, Mathf.Tau)).Scaled(Vector3.One * s));
			_trunks.Add((pos, 0.55f * s, 6f));
			CypressSpots.Add(pos);
		}

		// reed beds out in the shallows, and marsh mats lying out over the water, in patches
		for (int i = 0; i < 26000; i++)
		{
			float x = rng.RandfRange(-LakeShape.SemiX - 6f, LakeShape.SemiX + 6f);
			float z = rng.RandfRange(LakeShape.FarShoreZ - 6f, LakeShape.NearShoreZ + 6f);
			float d = LakeShape.ShoreDist(x, z);
			var p = new Vector2(x, z);
			if (d > 1.5f || d < -9f || NearLanding(p)) continue;
			float c = clump.GetNoise2D(x, z);
			float gy = LakeShape.Ground(x, z);
			if (c > 0.3f && d > -6.5f && d < 0.5f)
			{
				float s = rng.RandfRange(0.8f, 1.25f);
				ReedBedCount++;
				Add("reed_tall", new Vector3(x, gy - 0.05f, z), new Basis(Vector3.Up, rng.RandfRange(0, Mathf.Tau)).Scaled(new Vector3(s, s * rng.RandfRange(0.85f, 1.2f), s)));
			}
			else if (c < -0.35f && d > -4.5f && d < -0.3f && rng.Randf() < 0.35f)
			{
				float s = rng.RandfRange(0.9f, 1.4f);
				// lying on the water itself (its blades just over the surface)
				Add("marsh_mat", new Vector3(x, 0.02f, z), new Basis(Vector3.Up, rng.RandfRange(0, Mathf.Tau)).Scaled(new Vector3(s, s * rng.RandfRange(0.7f, 1.1f), s)));
			}
		}

		// the wrack along the waterline: straw lying flat, following the shore, and drift stranded in it
		for (float th = -Mathf.Pi; th < Mathf.Pi; th += 0.012f)
		{
			if (rng.Randf() < 0.25f) continue;
			var p = ShorePoint(th + rng.RandfRange(-0.004f, 0.004f), rng.RandfRange(0.2f, 2.6f));
			if (NearLanding(p) && rng.Randf() < 0.5f) continue;
			float gy = LakeShape.Ground(p.X, p.Y);
			var tang = new Vector3(Mathf.Cos(th), 0, -Mathf.Sin(th));
			var basis = Basis.LookingAt(tang, Vector3.Up) * new Basis(Vector3.Up, rng.RandfRange(-0.4f, 0.4f));
			float s = rng.RandfRange(0.8f, 1.4f);
			WrackCount++;
			Add("wrack", new Vector3(p.X, gy + 0.012f, p.Y), basis.Scaled(new Vector3(s, 1f, s * rng.RandfRange(0.7f, 1.1f))));
			if (rng.Randf() < 0.07f)
				Add(rng.Randf() < 0.65f ? "drift" : "rootwad", new Vector3(p.X, gy - 0.02f, p.Y),
					new Basis(Vector3.Up, rng.RandfRange(0, Mathf.Tau)).Scaled(Vector3.One * rng.RandfRange(0.7f, 1.3f)));
		}

		BuildBirdhouse();
		BuildPipelineSign();
		GD.Print($"[lake] the shore: {CypressSpots.Count} cypresses, {ReedBedCount} reed stands, {WrackCount} patches of wrack");
	}

	// ------------------------------------------------------------------ meshes

	/// <summary>A bald cypress: its trunk flaring out wide toward its foot (fluted), its roots heaved up in a tangle and
	/// running off into the mud and the water, knees round it, a crown of feathery drooping boughs, moss hanging thick.</summary>
	private static Mesh CypressMesh(int seed, float height)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7717) };
		var k = new MeshKit();
		k.Mat(ProcTextures.TreeBarkMat);
		var bark = new Color(0.55f, 0.5f, 0.45f);
		Vector3 lean = new(rng.RandfRange(-0.3f, 0.3f), 0, rng.RandfRange(-0.3f, 0.3f));
		Vector3 Axis(float y) => new Vector3(0, y, 0) + lean * (y / height) * (y / height) * 2f;
		var rings = new List<(Vector3, float, Color)>
		{
			(new Vector3(0, -0.4f, 0), 1.25f, bark), (new Vector3(0, 0.05f, 0), 1.05f, bark), (Axis(0.5f), 0.68f, bark),
			(Axis(1.2f), 0.44f, bark), (Axis(2.4f), 0.34f, bark), (Axis(height * 0.5f), 0.26f, bark * 1.05f), (Axis(height * 0.95f), 0.06f, bark * 1.1f),
		};
		ForestScatter.TrunkLoft(k, rings, 14, 1f);
		// the flutes: buttresses running down the flare
		for (int i = 0; i < 7; i++)
		{
			float a = Mathf.Tau * i / 7f + rng.RandfRange(-0.2f, 0.2f);
			var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
			k.Color = bark * 0.95f;
			k.Cylinder(o * 0.32f + Vector3.Up * 2.2f, o * 1.1f + Vector3.Down * 0.1f, 0.1f, 0.26f, 5, false);
		}
		// the roots: heaved up, twisting out across the ground and down into the water
		for (int i = 0; i < 11; i++)
		{
			float a = rng.RandfRange(0, Mathf.Tau);
			var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
			var side = new Vector3(-o.Z, 0, o.X);
			float len = rng.RandfRange(1.4f, 3.2f);
			var pts = new List<Vector3> { o * 0.7f + Vector3.Up * 0.25f };
			for (int j = 1; j <= 4; j++)
			{
				float u = j / 4f;
				pts.Add(o * (0.7f + len * u) + side * rng.RandfRange(-0.4f, 0.4f) + Vector3.Up * (0.3f * Mathf.Sin(u * Mathf.Pi) + rng.RandfRange(0f, 0.15f) - 0.25f * u * u));
			}
			k.Color = bark * rng.RandfRange(0.7f, 0.95f);
			BunkerParts.BunkerKit.Tube(k, pts, rng.RandfRange(0.07f, 0.13f), 0.025f, 5);
			// a smaller one off it, curling back
			var mid = pts[2];
			BunkerParts.BunkerKit.Tube(k, new List<Vector3> { mid, mid + side * 0.4f + Vector3.Up * 0.12f, mid + side * 0.7f - o * 0.2f - Vector3.Up * 0.1f }, 0.04f, 0.015f, 4);
		}
		// knees: little knobbed stumps poking up round it
		for (int i = 0; i < 6; i++)
		{
			float a = rng.RandfRange(0, Mathf.Tau), r = rng.RandfRange(1.6f, 3.2f);
			var p = new Vector3(Mathf.Cos(a) * r, -0.2f, Mathf.Sin(a) * r);
			float h = rng.RandfRange(0.3f, 0.75f);
			k.Color = bark * 0.85f;
			k.Cylinder(p, p + new Vector3(rng.RandfRange(-0.05f, 0.05f), h, 0), 0.13f, 0.05f, 6, true);
		}
		// the crown: limbs out from the upper half of the trunk in every direction, sprays of feathery bough hanging off
		// each in layers (a bald cypress's soft, bright, ragged crown), moss hanging thick through it
		var mossRng = new RandomNumberGenerator { Seed = (ulong)(seed * 31 + 7) };
		int limbs = 16;
		for (int i = 0; i < limbs; i++)
		{
			float f = Mathf.Lerp(0.38f, 0.95f, i / (limbs - 1f)) + rng.RandfRange(-0.03f, 0.03f);
			float a = i * 2.39996f + rng.RandfRange(-0.3f, 0.3f);   // the golden angle: all the way round
			var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
			float len = Mathf.Lerp(4.0f, 1.4f, Mathf.Pow(i / (limbs - 1f), 0.8f)) * rng.RandfRange(0.8f, 1.15f);
			Vector3 from = Axis(height * f), to = from + o * len + Vector3.Up * rng.RandfRange(0.1f, 0.8f);
			k.Mat(ProcTextures.TreeBarkMat);
			k.Color = bark;
			k.Cylinder(from, to, 0.12f * (1.2f - f), 0.03f, 5, false);
			k.Mat(CypressFoliage);
			Vector3 side = new Vector3(-o.Z, 0, o.X);
			for (int b = 0; b < 14; b++)
			{
				float u = 0.1f + 0.9f * b / 13f;
				Vector3 at = from.Lerp(to, u) + Vector3.Up * rng.RandfRange(-0.15f, 0.25f);
				float w = rng.RandfRange(1.3f, 2.1f) * (1.1f - 0.3f * u);
				// a spray: out and drooping, turned a little each way
				Vector3 sd = (side * Mathf.Cos(b * 1.3f) + o * 0.3f).Normalized();
				Vector3 tip = at + (o * 0.5f + sd * 0.35f).Normalized() * w * 0.7f + Vector3.Down * w * 0.55f;
				float lum = rng.RandfRange(0.8f, 1.15f);
				k.Color = new Color(lum, lum, lum);
				Vector3 n = (Vector3.Up + o * 0.3f).Normalized();
				k.Quad(at - sd * w * 0.5f, at + sd * w * 0.5f, tip + sd * w * 0.38f, tip - sd * w * 0.38f, n,
					new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
			}
			for (int m = 0; m < 3; m++)
				if (mossRng.Randf() < 0.7f)
					ForestScatter.MossCard(k, from.Lerp(to, mossRng.RandfRange(0.2f, 0.95f)), mossRng.RandfRange(0.8f, 2.2f), mossRng.RandfRange(0.35f, 0.6f), mossRng);
		}
		// its top: a few sprays round the leader
		k.Mat(CypressFoliage);
		for (int b = 0; b < 6; b++)
		{
			float a = b * 1.05f;
			Vector3 at = Axis(height * 0.97f), o = new(Mathf.Cos(a), 0, Mathf.Sin(a)), sd = new(-o.Z, 0, o.X);
			Vector3 tip = at + o * 0.9f + Vector3.Down * 0.6f;
			k.Color = Colors.White;
			k.Quad(at - sd * 0.6f, at + sd * 0.6f, tip + sd * 0.4f, tip - sd * 0.4f, Vector3.Up, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
		}
		return k.Commit();
	}

	private static StandardMaterial3D _cypressFoliage;
	/// <summary>A bald cypress's feathery sprays: the fir spray photo, a brighter, yellower green, the same on both faces and
	/// lit through from behind (the crown is mostly seen from below, against the sky).</summary>
	private static StandardMaterial3D CypressFoliage => _cypressFoliage ??= new StandardMaterial3D
	{
		ResourceName = "cypress_foliage", AlbedoTexture = GD.Load<Texture2D>("res://assets/textures/winter/fir_bough.png"),
		AlbedoColor = new Color(0.95f, 1.15f, 0.62f), Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, AlphaScissorThreshold = 0.4f,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 1f, VertexColorUseAsAlbedo = true,
		BacklightEnabled = true, Backlight = new Color(0.25f, 0.32f, 0.14f),
		TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
	};

	/// <summary>Tall reed: a dense stand of thin stems, crossed cards higher than a man, gone gold at their tops, plumes.</summary>
	private static Mesh TallReedMesh()
	{
		var k = new MeshKit();
		var mat = (ShaderMaterial)ProcTextures.Cached("lake_reed_tall", () =>
		{
			var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/foliage.gdshader") };
			DetailKit.Hook(m, DetailKit.Kind.Foliage);
			m.SetShaderParameter("albedo_tex", ProcTextures.GrassTuft());
			m.SetShaderParameter("tint", new Color(0.62f, 0.68f, 0.36f));
			m.SetShaderParameter("sway", 0.14f);
			m.SetShaderParameter("sway_speed", 0.8f);
			m.SetShaderParameter("normal_up", 0.35f);
			return m;
		});
		k.Mat(mat);
		for (int i = 0; i < 6; i++)
		{
			float a = Mathf.Pi * i / 6f + 0.13f;
			Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.45f;
			Vector3 n = new Vector3(-d.Z, 0.3f, d.X).Normalized();
			float h = 2.3f + 0.4f * (i % 3) * 0.5f;
			// lower half green, upper half gold (vertex colour over the tint)
			k.Color = new Color(0.85f, 0.9f, 0.7f);
			k.Quad(-d, d, d * 1.15f + new Vector3(0, h * 0.5f, 0), -d * 1.15f + new Vector3(0, h * 0.5f, 0), n,
				new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(0, 0.5f));
			k.Color = new Color(1.35f, 1.15f, 0.7f);
			k.Quad(-d * 1.15f + new Vector3(0, h * 0.5f, 0), d * 1.15f + new Vector3(0, h * 0.5f, 0), d * 1.3f + new Vector3(0, h, 0), -d * 1.3f + new Vector3(0, h, 0), n,
				new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0), new Vector2(0, 0));
		}
		// plumes
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.42f, 0.32f, 0.22f);
		var rng = new RandomNumberGenerator { Seed = 4721 };
		for (int i = 0; i < 7; i++)
		{
			var p = new Vector3(rng.RandfRange(-0.4f, 0.4f), 0, rng.RandfRange(-0.4f, 0.4f));
			float h = rng.RandfRange(2.3f, 2.8f);
			k.Cylinder(p, p + new Vector3(0.03f, h, 0), 0.009f, 0.006f, 3, false);
			k.Cylinder(p + new Vector3(0.03f, h, 0), p + new Vector3(0.12f, h + 0.32f, 0.04f), 0.035f, 0.01f, 4, false);
		}
		return k.Commit();
	}

	/// <summary>A mat of marsh grass lying out over the water: blades fanning out from a root, bent down so the tips
	/// trail on the surface.</summary>
	private static Mesh MarshMatMesh()
	{
		var k = new MeshKit();
		var mat = (ShaderMaterial)ProcTextures.Cached("lake_marsh", () =>
		{
			var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/foliage.gdshader") };
			DetailKit.Hook(m, DetailKit.Kind.Foliage);
			m.SetShaderParameter("albedo_tex", ProcTextures.GrassTuft());
			m.SetShaderParameter("tint", new Color(0.5f, 0.72f, 0.3f));
			m.SetShaderParameter("sway", 0.03f);
			m.SetShaderParameter("normal_up", 0.6f);
			return m;
		});
		k.Mat(mat);
		var rng = new RandomNumberGenerator { Seed = 4731 };
		for (int i = 0; i < 14; i++)
		{
			float a = Mathf.Tau * i / 14f + rng.RandfRange(-0.2f, 0.2f);
			var o = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
			var side = new Vector3(-o.Z, 0, o.X) * 0.28f;
			float len = rng.RandfRange(0.9f, 1.6f);
			Vector3 mid = o * len * 0.45f + Vector3.Up * 0.32f, tip = o * len + Vector3.Up * 0.02f;
			float l = rng.RandfRange(0.85f, 1.15f);
			k.Color = new Color(l, l, l * 0.9f);
			k.Quad(-side * 0.6f, side * 0.6f, mid + side, mid - side, Vector3.Up, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(0, 0.5f));
			k.Quad(mid - side, mid + side, tip + side * 1.2f, tip - side * 1.2f, Vector3.Up, new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0), new Vector2(0, 0));
		}
		return k.Commit();
	}

	private static StandardMaterial3D _strawMat;

	/// <summary>Dead reed straw washed up and dried: thin pale stalks lying every way, a few dark leaves (alpha-cut).</summary>
	private static StandardMaterial3D StrawMat()
	{
		if (_strawMat != null) return _strawMat;
		const int s = 256;
		var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
		img.Fill(new Color(0.45f, 0.4f, 0.32f, 0f));
		var rng = new RandomNumberGenerator { Seed = 4741 };
		void Stick(float x0, float y0, float ang, float len, float w, Color c)
		{
			float dx = Mathf.Cos(ang), dy = Mathf.Sin(ang);
			for (float t = 0; t < len; t += 0.5f)
				for (float o = -w; o <= w; o += 0.5f)
				{
					int x = (int)(x0 + dx * t - dy * o), y = (int)(y0 + dy * t + dx * o);
					if (x < 0 || y < 0 || x >= s || y >= s) continue;
					float edge = Mathf.Abs(o) / Mathf.Max(w, 0.5f);
					img.SetPixel(x, y, new Color(c.R * (1f - 0.35f * edge), c.G * (1f - 0.35f * edge), c.B * (1f - 0.35f * edge), 1f));
				}
		}
		// mostly along one way (washed up along the shore), some across
		for (int i = 0; i < 300; i++)
		{
			float ang = rng.Randf() < 0.7f ? rng.RandfRange(-0.35f, 0.35f) : rng.RandfRange(0f, Mathf.Pi);
			float lum = rng.RandfRange(0.55f, 0.95f);
			var c = new Color(0.62f, 0.55f, 0.4f) * lum;
			if (rng.Randf() < 0.15f) c = new Color(0.3f, 0.26f, 0.2f) * lum;   // a darker, wetter stalk
			Stick(rng.RandfRange(-40, s), rng.RandfRange(0, s), ang, rng.RandfRange(30, 120), rng.RandfRange(0.8f, 1.8f), c);
		}
		// oak leaves fallen into it
		for (int i = 0; i < 24; i++)
		{
			float cx = rng.RandfRange(0, s), cy = rng.RandfRange(0, s), r = rng.RandfRange(4f, 8f), ang = rng.RandfRange(0, Mathf.Tau);
			var c = new Color(0.36f, 0.22f, 0.12f) * rng.RandfRange(0.7f, 1.1f);
			for (int y = (int)(cy - r); y <= cy + r; y++)
				for (int x = (int)(cx - r); x <= cx + r; x++)
				{
					if (x < 0 || y < 0 || x >= s || y >= s) continue;
					float u = ((x - cx) * Mathf.Cos(ang) + (y - cy) * Mathf.Sin(ang)) / r, v = (-(x - cx) * Mathf.Sin(ang) + (y - cy) * Mathf.Cos(ang)) / (r * 0.55f);
					if (u * u + v * v < 1f) img.SetPixel(x, y, new Color(c.R, c.G, c.B, 1f));
				}
		}
		// fade its edges out (a patch, not a mat with a border)
		for (int y = 0; y < s; y++)
			for (int x = 0; x < s; x++)
			{
				float dx = (x - s * 0.5f) / (s * 0.5f), dy = (y - s * 0.5f) / (s * 0.5f);
				float r = Mathf.Sqrt(dx * dx + dy * dy);
				if (r > 0.75f && rng.Randf() < (r - 0.75f) * 4f) img.SetPixel(x, y, new Color(0, 0, 0, 0));
			}
		img.GenerateMipmaps();
		_strawMat = new StandardMaterial3D
		{
			ResourceName = "lake_straw", AlbedoTexture = ImageTexture.CreateFromImage(img), Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
			AlphaScissorThreshold = 0.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 1f,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		};
		return _strawMat;
	}

	/// <summary>A patch of the wrack, lying flat (two layers, the top one a hair higher and turned, so it has thickness).</summary>
	private static Mesh WrackMesh()
	{
		var k = new MeshKit();
		k.Mat(StrawMat());
		k.Color = Colors.White;
		foreach (var (y, rot, sc) in new[] { (0f, 0f, 1f), (0.025f, 0.6f, 0.8f) })
		{
			var b = new Basis(Vector3.Up, rot);
			Vector3 P(float x, float z) => b * new Vector3(x * sc, y, z * sc);
			k.Quad(P(-1.1f, -0.6f), P(1.1f, -0.6f), P(1.1f, 0.6f), P(-1.1f, 0.6f), Vector3.Up,
				new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
		}
		return k.Commit();
	}

	/// <summary>A bleached branch washed up: forked, lying half on the sand.</summary>
	private static Mesh DriftMesh(int seed)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 977) };
		var k = new MeshKit();
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.82f, 0.78f, 0.7f);
		var a = new Vector3(-1.3f, 0.05f, 0); var b = new Vector3(1.3f, 0.08f, rng.RandfRange(-0.3f, 0.3f));
		BunkerParts.BunkerKit.Tube(k, new List<Vector3> { a, (a + b) * 0.5f + Vector3.Up * 0.06f, b }, 0.08f, 0.03f, 6);
		for (int i = 0; i < 3; i++)
		{
			var p = a.Lerp(b, rng.RandfRange(0.25f, 0.85f));
			BunkerParts.BunkerKit.Tube(k, new List<Vector3> { p, p + new Vector3(rng.RandfRange(-0.4f, 0.4f), rng.RandfRange(0.05f, 0.25f), rng.RandfRange(0.2f, 0.6f) * (i % 2 == 0 ? 1 : -1)) }, 0.035f, 0.01f, 4);
		}
		return k.Commit();
	}

	/// <summary>A root wad: the torn-up roots of something the water took, tangled, stranded on the shore.</summary>
	private static Mesh RootWadMesh(int seed)
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 977) };
		var k = new MeshKit();
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.5f, 0.46f, 0.4f);
		var c = new Vector3(0, 0.18f, 0);
		k.Blob(c, new Vector3(0.38f, 0.2f, 0.32f), seed, 0.3f, false);
		// the roots sprawl out low and tangled, a few reaching up and over, bleached by the water
		k.Color = new Color(0.56f, 0.52f, 0.46f);
		for (int i = 0; i < 14; i++)
		{
			float a = rng.RandfRange(0, Mathf.Tau), el = i < 4 ? rng.RandfRange(0.3f, 0.7f) : rng.RandfRange(-0.15f, 0.25f);
			var o = new Vector3(Mathf.Cos(a) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(a) * Mathf.Cos(el));
			var side = new Vector3(-o.Z, 0, o.X);
			float len = rng.RandfRange(0.6f, 1.4f);
			var p1 = c + o * len * 0.5f + side * rng.RandfRange(-0.2f, 0.2f);
			var p2 = c + o * len + side * rng.RandfRange(-0.3f, 0.3f);
			if (i >= 4) { p1.Y = Mathf.Max(0.04f, p1.Y); p2.Y = rng.RandfRange(0.02f, 0.12f); }
			var p3 = p2 + (p2 - p1).Normalized() * 0.3f + side * rng.RandfRange(-0.3f, 0.3f) + Vector3.Down * (i < 4 ? 0.25f : 0f);
			p3.Y = Mathf.Max(p3.Y, 0.02f);
			BunkerParts.BunkerKit.Tube(k, new List<Vector3> { c + o * 0.2f, p1, p2, p3 }, rng.RandfRange(0.035f, 0.07f), 0.012f, 5);
		}
		return k.Commit();
	}

	// ------------------------------------------------------------------ things people left

	/// <summary>A birdhouse nailed to a cypress near the landing, as the owner's photo (its roof greened, its hole dark).</summary>
	private void BuildBirdhouse()
	{
		var p = ShorePoint(0.42f, 0.4f);
		var foot = new Vector3(p.X, LakeShape.Ground(p.X, p.Y) - 0.15f, p.Y);
		Add("cypress_c", foot, new Basis(Vector3.Up, 1.1f));
		_trunks.Add((foot, 0.55f, 6f));
		var k = new MeshKit();
		k.Mat(BuildingTextures.BoardsMat);
		var face = (new Vector3(0, 0, 9f) - foot) with { Y = 0 };
		face = face.Normalized();
		var b = Basis.LookingAt(-face, Vector3.Up);
		Vector3 at = foot + face * 0.42f + Vector3.Up * 3.3f;
		k.Color = new Color(0.62f, 0.55f, 0.46f);
		k.Box(at, new Vector3(0.24f, 0.32f, 0.22f), 1f, b);
		k.Box(at + b * new Vector3(0, -0.4f, -0.03f), new Vector3(0.12f, 0.5f, 0.06f), 1f, b);   // its post down the trunk
		k.Color = new Color(0.36f, 0.4f, 0.3f);
		k.Box(at + b * new Vector3(-0.08f, 0.2f, 0), new Vector3(0.2f, 0.03f, 0.28f), 1f, b * new Basis(Vector3.Back, 0.55f));
		k.Box(at + b * new Vector3(0.08f, 0.2f, 0), new Vector3(0.2f, 0.03f, 0.28f), 1f, b * new Basis(Vector3.Back, -0.55f));
		k.Mat(ProcTextures.Flat("birdhouse_hole", new Color(0.02f, 0.02f, 0.02f)));
		k.Cylinder(at + b * new Vector3(0, 0.05f, 0.105f), at + b * new Vector3(0, 0.05f, 0.112f), 0.035f, 0.035f, 10, true);
		k.CommitTo(this, "Birdhouse", false);
	}

	/// <summary>Out in the marsh grass off the far shore's flank, a warning sign on two posts, as the owner's photo.</summary>
	private void BuildPipelineSign()
	{
		var p = ShorePoint(2.55f, -2.2f);
		var foot = new Vector3(p.X, LakeShape.Ground(p.X, p.Y), p.Y);
		var toLake = (new Vector3(0, 0, LakeShape.CenterZ) - foot) with { Y = 0 };
		var b = Basis.LookingAt(-toLake.Normalized(), Vector3.Up);
		var root = new Node3D { Name = "PipelineSign" };
		AddChild(root);
		root.GlobalTransform = new Transform3D(b * new Basis(Vector3.Forward, 0.04f), foot);
		var k = new MeshKit();
		k.Mat(ProcTextures.MetalMat);
		k.Color = new Color(0.55f, 0.55f, 0.52f);
		foreach (float x in new[] { -0.55f, 0.55f })
			k.Cylinder(new Vector3(x, -0.6f, 0), new Vector3(x, 2.9f, 0), 0.04f, 0.04f, 8, true);
		k.Mat(ProcTextures.Flat("pipeline_sign", new Color(0.78f, 0.77f, 0.72f), 0.8f));
		k.Box(new Vector3(0, 2.2f, 0.06f), new Vector3(1.5f, 1.0f, 0.03f), 1f);
		k.Mat(ProcTextures.Flat("pipeline_sign_rim", new Color(0.55f, 0.45f, 0.1f), 0.8f));
		k.Box(new Vector3(0, 2.2f, 0.045f), new Vector3(1.58f, 1.08f, 0.02f), 1f);
		k.CommitTo(root, "Mesh", true);
		var ink = new Color(0.06f, 0.06f, 0.06f);
		SignKit.Text(root, "WARNING", new Vector3(0, 2.55f, 0.08f), Basis.Identity, 0.12f, ink, shadow: false);
		SignKit.Text(root, "DO NOT ANCHOR", new Vector3(0, 2.33f, 0.08f), Basis.Identity, 0.17f, ink, shadow: false);
		SignKit.Text(root, "OR DREDGE", new Vector3(0, 2.13f, 0.08f), Basis.Identity, 0.17f, ink, shadow: false);
		SignKit.Text(root, "GAS PIPELINE CROSSING", new Vector3(0, 1.93f, 0.08f), Basis.Identity, 0.1f, ink, shadow: false);
		SignKit.Text(root, "IN EMERGENCY CALL STATION 7", new Vector3(0, 1.79f, 0.08f), Basis.Identity, 0.06f, ink, shadow: false);
	}
}
