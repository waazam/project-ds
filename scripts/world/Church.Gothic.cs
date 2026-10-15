using System.Collections.Generic;
using Godot;
using ProjectDS.World.ChurchParts;

namespace ProjectDS.World;

/// <summary>
/// The church's second pass, after the owner's feedback (the first was too white: "like staring into clouds
/// on a sunny day") and their plan for its inside: dark, sooted stone lit by candles; alcoves down both aisles
/// full of burning votives; a round raised dais at the crossing under an iron crown of candles; an altar at
/// the end of each transept arm, the left one bloodied, the blood dragged off toward the vestry's door; five
/// small chapels opening off the apse, each with its own candle and a figure; and round the high altar, a
/// circle of red light worked into the floor.
/// </summary>
public partial class Church
{
	/// <summary>The apse facets (of seven) that open into chapels instead of holding a window.</summary>
	public static bool FacetHasChapel(int i) => i >= 1 && i <= 5;
	public const float ChapelDepth = 3.0f, ChapelHalf = 1.4f, ChapelFloorY = ChancelY + 0.15f;
	public static readonly Vector3 CrossingDaisLocal = new(0, 0, (NaveEnd + CrossEnd) * 0.5f);
	public const float CrossingDaisR = 2.6f, CrossingDaisH = 0.25f;

	private readonly List<Transform3D> _flames = new();

	private void BuildGothic()
	{
		BuildCandleAlcoves();
		BuildCrossingDais();
		BuildTranseptAltars();
		BuildApseChapels();
		BuildSigil();
		BuildDoorSconces();
		// high up, a faint warm glow along the triforium, so the vault's ribs read in the dark over the candles
		foreach (float x in new[] { -6.2f, 6.2f })
			foreach (float z in new[] { 11f, 26f, 41f, 60.5f, 75f })
				AddChild(new OmniLight3D { Position = new Vector3(x, 27f, z), LightColor = new Color(1f, 0.7f, 0.45f), LightEnergy = 0.8f, OmniRange = 16f, OmniAttenuation = 1.0f, ShadowEnabled = false });
		// every little flame in the church in one draw
		var mesh = new SphereMesh { Radius = 0.012f, Height = 0.045f, RadialSegments = 6, Rings = 3 };
		mesh.Material = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.8f, 0.45f), EmissionEnabled = true, Emission = new Color(1f, 0.6f, 0.25f), EmissionEnergyMultiplier = 4f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = _flames.Count };
		for (int i = 0; i < _flames.Count; i++) mm.SetInstanceTransform(i, _flames[i]);
		AddChild(new MultiMeshInstance3D { Name = "Flames", Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
	}

	private static StandardMaterial3D _wax;
	private static StandardMaterial3D Wax => _wax ??= WaxMaterial.Make(new Color(0.88f, 0.84f, 0.74f), 0.6f, "church_wax");

	/// <summary>A candle standing at <paramref name="at"/> (its foot), lit.
	/// (2026-10-15, the props pass: turned, 8 sides, its top burnt down into a hollow round the wick and a lip of wax
	/// run over at its foot; it was a six-sided stick with a flat top)</summary>
	private void Candle(MeshKit wax, Vector3 at, float h, float r, bool lit = true)
	{
		TurnedKit.Lathe(wax, at, TurnedKit.P(0f, 0f, r * 1.12f, 0f, r * 1.08f, Mathf.Min(0.012f, h * 0.15f), r, Mathf.Min(0.02f, h * 0.25f), r * 0.96f, h - 0.006f,
			r * 0.84f, h, r * 0.45f, h - 0.005f, 0f, h - 0.004f), 8, 40f);
		if (!lit) return;
		// (2026-10-15, the repetition pass: each flame its own size and lean, from the candle's place; they were one bead,
		// one size, all 259 of them)
		float H(float k) => Mathf.PosMod(Mathf.Sin(at.X * 12.9898f + at.Z * 78.233f + at.Y * 37.719f + k) * 43758.5453f, 1f);
		float sxz = 0.75f + 0.45f * H(1f), sy = 0.7f + 0.65f * H(2f);
		var b = new Basis(Vector3.Up, H(3f) * Mathf.Tau) * new Basis(Vector3.Right, (H(4f) - 0.5f) * 0.2f) * Basis.FromScale(new Vector3(sxz, sy, sxz));
		_flames.Add(new Transform3D(b, at + Vector3.Up * (h + 0.025f * sy)));
	}

	private void Warm(Vector3 at, float energy, float range, bool shadows = false)
	{
		var l = new OmniLight3D { Position = at, LightColor = new Color(1f, 0.64f, 0.32f), LightEnergy = energy * 1.25f, OmniRange = range * 1.1f, OmniAttenuation = 1.3f, ShadowEnabled = shadows };
		AddChild(l);
		_warm.Add((l, l.LightEnergy));
	}

	private readonly List<(OmniLight3D light, float energy)> _warm = new();
	private float _flickerT;

	/// <summary>The candlelight breathes: every flame's light wavers a little, slowly and softly, each out of
	/// step with the rest (small and slow: a glow that lives, never a flicker that flashes).</summary>
	private void FlickerCandles(float dt)
	{
		_flickerT += dt;
		for (int i = 0; i < _warm.Count; i++)
		{
			var (l, e) = _warm[i];
			float w = 0.93f + 0.045f * Mathf.Sin(_flickerT * 2.1f + i * 1.9f) + 0.025f * Mathf.Sin(_flickerT * 4.7f + i * 0.7f);
			l.LightEnergy = e * w;
		}
	}

	/// <summary>A candle on an iron bracket beside a door, so the way (or the way that isn't open yet) can
	/// always be found in the dark.</summary>
	private void DoorSconce(MeshKit iron, MeshKit wax, Vector3 at, Vector3 outOfWall)
	{
		// (2026-10-15: a wrought bracket, a wall plate, an arm curling up under a drip pan with a pricket; it was a bar
		// and a disc)
		Vector3 cup = at + outOfWall * 0.22f;
		var plate = new Transform3D(Basis.LookingAt(-outOfWall, Vector3.Up) * new Basis(Vector3.Right, Mathf.Pi * 0.5f), at - outOfWall * 0.02f + Vector3.Down * 0.08f);
		TurnedKit.Lathe(iron, plate, TurnedKit.P(0f, 0f, 0.05f, 0f, 0.05f, 0.012f, 0.03f, 0.022f, 0f, 0.024f), 8, 38f, 0.7f, 1f);
		TurnedKit.Rod(iron, TurnedKit.Curve(at - outOfWall * 0.01f + Vector3.Down * 0.08f, at + outOfWall * 0.2f + Vector3.Down * 0.14f, cup + Vector3.Down * 0.05f, 8), 0.012f, 6, true);
		TurnedKit.Rod(iron, TurnedKit.Curve(at + outOfWall * 0.02f + Vector3.Down * 0.08f, at + outOfWall * 0.08f + Vector3.Up * 0.02f, cup + Vector3.Down * 0.035f - outOfWall * 0.04f, 6), 0.008f, 5, true);
		TurnedKit.Lathe(iron, cup + Vector3.Down * 0.055f, TurnedKit.P(0f, 0f, 0.012f, 0f, 0.016f, 0.03f, 0.06f, 0.05f, 0.07f, 0.065f, 0.064f, 0.07f, 0.05f, 0.062f, 0.012f, 0.062f, 0f, 0.064f), 10);
		Candle(wax, cup + Vector3.Up * 0.01f, 0.18f, 0.03f);
		Warm(at + outOfWall * 0.5f + Vector3.Up * 0.3f, 0.75f, 7f);
	}

	private void BuildDoorSconces()
	{
		var iron = new MeshKit();
		iron.Mat(ChurchTextures.IronMat);
		iron.Color = Colors.White;
		var wax = new MeshKit();
		wax.Mat(Wax);
		float mid = (NaveEnd + CrossEnd) * 0.5f;
		foreach (float x in new[] { -3.7f, 3.7f }) DoorSconce(iron, wax, new Vector3(x, 2.7f, 0.02f), Vector3.Back);
		DoorSconce(iron, wax, new Vector3(9.9f, 2.3f, 0.02f), Vector3.Back);
		foreach (float s in new[] { -1f, 1f })
			foreach (float dz in new[] { -2f, 2f })
				DoorSconce(iron, wax, new Vector3(s * (TransHalf - 0.02f), 2.4f, mid + dz), new Vector3(-s, 0, 0));
		iron.CommitTo(this, "DoorSconces", true);
		wax.CommitTo(this, "DoorSconceCandles", false);
	}

	// ------------------------------------------------------------------ the aisles' candle alcoves

	/// <summary>In every aisle bay, under its window: an iron rack of votives, three tiers of them, most burning.</summary>
	private void BuildCandleAlcoves()
	{
		var iron = new MeshKit();
		iron.Mat(ChurchTextures.IronMat);
		iron.Color = Colors.White;
		var wax = new MeshKit();
		wax.Mat(Wax);
		wax.Color = Colors.White;
		var rng = new RandomNumberGenerator { Seed = 2177 };
		// (2026-10-15, the repetition pass: every bay had the same rack under the same window, the same eight-a-tier rows
		// burning. Now each bay its own: some racks full and burning, some sparse, one gone cold and burnt down to stubs, one
		// bay with no rack at all (its marks on the floor); each rack a little along the bay, its candles out of line, gaps
		// where some have burnt away, one lying where it fell)
		var cold = new HashSet<(float, int)> { (-1f, 5), (1f, 1) };
		var bare = new HashSet<(float, int)> { (-1f, 2) };
		foreach (float s in new[] { -1f, 1f })
			for (int b = 0; b < NaveBays; b++)
			{
				if (s > 0 && b == BrokenBay) continue;
				if (bare.Contains((s, b))) continue;
				bool isCold = cold.Contains((s, b));
				float sparse = isCold ? 0.35f : rng.Randf() < 0.4f ? rng.RandfRange(0.3f, 0.5f) : rng.RandfRange(0.08f, 0.2f);
				float z = (b + 0.5f) * BayLen + rng.RandfRange(-0.6f, 0.6f), x = s * (AisleOuter - 0.55f);
				int lit = 0;
				// the rack: three stepped shelves on legs, the lowest nearest
				for (int t = 0; t < 3; t++)
				{
					float y = 0.7f + t * 0.22f, xx = x + s * t * 0.16f;
					BuildKit.Box(iron, new Vector3(xx, y, z), new Vector3(0.2f, 0.025f, 1.6f), 1f);
					for (int c = 0; c < 8; c++)
					{
						if (rng.Randf() < sparse) continue;
						float h = isCold ? rng.RandfRange(0.02f, 0.06f) : rng.RandfRange(0.05f, 0.14f);
						bool on = isCold ? rng.Randf() < 0.08f : rng.Randf() < 0.88f;
						Candle(wax, new Vector3(xx + rng.RandfRange(-0.04f, 0.04f), y + 0.012f, z - 0.7f + c * 0.2f + rng.RandfRange(-0.04f, 0.04f)), h, 0.022f, on);
						if (on) lit++;
					}
				}
				// now and then one fallen off, lying on the floor under the rack
				if (rng.Randf() < 0.35f)
				{
					float h = rng.RandfRange(0.06f, 0.13f), fz = z + rng.RandfRange(-0.6f, 0.6f);
					var lie = new Transform3D(new Basis(Vector3.Up, rng.RandfRange(0f, Mathf.Tau)) * new Basis(Vector3.Forward, Mathf.Pi * 0.5f), new Vector3(x - s * rng.RandfRange(0.25f, 0.45f), 0.024f, fz));
					TurnedKit.Lathe(wax, lie, TurnedKit.P(0f, -h * 0.5f, 0.023f, -h * 0.5f, 0.022f, h * 0.5f, 0.01f, h * 0.5f + 0.004f, 0f, h * 0.5f + 0.004f), 8);
				}
				foreach (float dz in new[] { -0.78f, 0.78f })
					foreach (float dx in new[] { 0f, 0.32f })
						iron.Beam(new Vector3(x + s * dx, 0, z + dz), new Vector3(x + s * dx, 1.2f, z + dz), 0.025f, 0.025f);
				Collide(_stone, new Vector3(x + s * 0.16f, 0.6f, z), new Vector3(0.55f, 1.2f, 1.7f));
				// (its glow as bright as what's burning on it)
				if (lit > 0) Warm(new Vector3(x - s * 0.35f, 1.3f, z), 1.1f * Mathf.Clamp(0.25f + lit / 18f, 0.25f, 1f), 8.5f);
			}
		iron.CommitTo(this, "VotiveRacks", true);
		wax.CommitTo(this, "Votives", false);
	}

	// ------------------------------------------------------------------ the crossing

	/// <summary>The crossing: a round dais of dark stone with the star medallion let into it, and over it an
	/// iron crown of candles on a long chain.</summary>
	private void BuildCrossingDais()
	{
		var at = CrossingDaisLocal;
		var k = new MeshKit();
		k.Mat(ChurchTextures.AshlarDampMat);
		k.Color = Colors.White;
		k.Cylinder(at, at + Vector3.Up * CrossingDaisH, CrossingDaisR, CrossingDaisR - 0.1f, 24, false);
		k.Mat(ChurchTextures.MedallionMat);
		for (int i = 0; i < 24; i++)
		{
			float a0 = Mathf.Tau * i / 24f, a1 = Mathf.Tau * (i + 1) / 24f;
			float r = CrossingDaisR - 0.1f;
			Vector3 c = at + Vector3.Up * CrossingDaisH, p0 = c + new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r), p1 = c + new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r);
			Vector2 U(Vector3 p) => new(0.5f + (p.X - c.X) / (2f * r), 0.5f + (p.Z - c.Z) / (2f * r));
			k.Tri(c, p0, p1, Vector3.Up, U(c), U(p0), U(p1));
		}
		k.CommitTo(this, "CrossingDais", true);
		_stone.AddChild(new CollisionShape3D { Position = at + Vector3.Up * (CrossingDaisH * 0.5f), Shape = new CylinderShape3D { Radius = CrossingDaisR, Height = CrossingDaisH } });
		// the crown: two hoops of iron and a ring of fat candles, low over the dais
		var crown = new MeshKit();
		crown.Mat(ChurchTextures.IronMat);
		crown.Color = Colors.White;
		var wax = new MeshKit();
		wax.Mat(Wax);
		Vector3 cc = at + Vector3.Up * 5.2f;
		crown.Cylinder(cc + Vector3.Up * 1.2f, new Vector3(cc.X, NaveVaultY(0) - 0.4f, cc.Z), 0.03f, 0.03f, 4, false);
		// (2026-10-15: round iron hoops, a band of uprights between them, a cup under every candle; the hoops were
		// rings of square bars)
		foreach (var (rr, yy) in new[] { (1.7f, 0f), (1.7f, 0.35f) })
			ItemMeshes.Torus(crown, cc + Vector3.Up * yy, Vector3.Up, rr, 0.03f, 36, 5);
		for (int i = 0; i < 24; i++)
		{
			float a = Mathf.Tau * (i + 0.5f) / 24f;
			Vector3 d = new(Mathf.Cos(a) * 1.7f, 0, Mathf.Sin(a) * 1.7f);
			crown.Cylinder(cc + d, cc + d + Vector3.Up * 0.35f, 0.012f, 0.012f, 4, false);
		}
		crown.Cylinder(cc + Vector3.Up * 1.12f, cc + Vector3.Up * 1.28f, 0.07f, 0.05f, 8, true);   // the boss the chains meet at
		for (int i = 0; i < 12; i++)
		{
			float a = Mathf.Tau * i / 12f;
			Vector3 p = cc + new Vector3(Mathf.Cos(a) * 1.7f, 0.35f, Mathf.Sin(a) * 1.7f);
			crown.Cylinder(p, cc + Vector3.Up * 1.2f, 0.012f, 0.012f, 4, false);
			TurnedKit.Lathe(crown, p, TurnedKit.P(0f, 0f, 0.025f, 0f, 0.03f, 0.012f, 0.06f, 0.025f, 0.065f, 0.04f, 0.055f, 0.042f, 0.035f, 0.035f, 0f, 0.036f), 8);
			Candle(wax, p + Vector3.Up * 0.036f, 0.2f, 0.035f);
		}
		crown.CommitTo(this, "Crown", false);
		wax.CommitTo(this, "CrownCandles", false);
		Warm(cc + Vector3.Up * 0.3f, 2.4f, 16f);
	}

	// ------------------------------------------------------------------ the transepts' altars

	/// <summary>An altar against the east wall of each transept arm, two candles burning on it. The left one's
	/// cloth is torn and bloodied, and the blood goes on across the floor in a long smear to the vestry's door.</summary>
	private void BuildTranseptAltars()
	{
		var k = new MeshKit();
		var wax = new MeshKit();
		wax.Mat(Wax);
		foreach (float s in new[] { -1f, 1f })
		{
			Vector3 at = new(s * 18.5f, 0, CrossEnd - 1.0f);
			k.Mat(ChurchTextures.AshlarDampMat);
			k.Color = Colors.White;
			BuildKit.Box(k, at + new Vector3(0, 0.1f, 0), new Vector3(3f, 0.2f, 1.8f), 1f, BuildKit.Face.NY);
			BuildKit.Box(k, at + new Vector3(0, 0.7f, 0.2f), new Vector3(2.1f, 1f, 0.9f), 1f, BuildKit.Face.NY);
			Collide(_stone, at + new Vector3(0, 0.6f, 0.1f), new Vector3(3f, 1.2f, 1.8f));
			k.Mat(new StandardMaterial3D { AlbedoColor = s < 0 ? new Color(0.62f, 0.56f, 0.5f) : new Color(0.7f, 0.66f, 0.6f), Roughness = 0.9f });
			BuildKit.Box(k, at + new Vector3(0, 1.21f, 0.2f), new Vector3(2.2f, 0.02f, 1f), 1f);
			BuildKit.Box(k, at + new Vector3(0, 1.0f, -0.27f), new Vector3(2.2f, 0.42f, 0.02f), 1f);
			k.Mat(ChurchTextures.IronMat);
			// the cross (2026-10-15: its arms ending in flared terminals, its foot in a stepped base; it was two square bars)
			k.ChamferBox(at + new Vector3(0, 1.8f, 0.5f), new Vector3(0.05f, 1.1f, 0.05f), 0.01f);
			k.ChamferBox(at + new Vector3(0, 2.05f, 0.5f), new Vector3(0.5f, 0.05f, 0.05f), 0.01f, 1f, null, 0);
			foreach (var (c, sz) in new[] { (new Vector3(-0.25f, 2.05f, 0.5f), new Vector3(0.03f, 0.09f, 0.08f)), (new Vector3(0.25f, 2.05f, 0.5f), new Vector3(0.03f, 0.09f, 0.08f)), (new Vector3(0, 2.35f, 0.5f), new Vector3(0.09f, 0.03f, 0.08f)) })
				k.BevelBox(at + c, sz, 0.008f);
			k.BevelBox(at + new Vector3(0, 1.27f, 0.5f), new Vector3(0.16f, 0.06f, 0.12f), 0.012f);
			// and its two candles in tall iron candlesticks (they stood on the cloth with nothing under them)
			var sticks = new MeshKit();
			sticks.Mat(ChurchTextures.IronMat);
			sticks.Color = Colors.White;
			foreach (float x in new[] { -0.8f, 0.8f })
			{
				LampKit.Candlestick(sticks, null, at + new Vector3(x, 1.22f, 0.35f), 0.34f);
				Candle(wax, at + new Vector3(x, 1.22f + 0.34f - 0.012f, 0.35f), 0.3f, 0.021f);
			}
			sticks.CommitTo(this, s < 0 ? "TransAltarSticksL" : "TransAltarSticksR", true);
			Warm(at + new Vector3(0, 1.9f, -0.4f), 1.2f, 8f);
		}
		k.CommitTo(this, "TransAltars", true);
		wax.CommitTo(this, "TransAltarCandles", false);
		// the blood: a pool by the left altar, splashes on its cloth, and a long dragged smear to the vestry door
		var blood = new MeshKit();
		blood.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.015f, 0.015f), Roughness = 0.15f, MetallicSpecular = 0.7f });
		var rng = new RandomNumberGenerator { Seed = 666 };
		void Splat(Vector3 c, float r, float y)
		{
			const int n = 14;
			var rim = new Vector3[n];
			for (int i = 0; i < n; i++)
			{
				float a = Mathf.Tau * i / n;
				float rr = r * rng.RandfRange(0.6f, 1.15f);
				rim[i] = c + new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr * rng.RandfRange(0.7f, 1f));
			}
			for (int i = 0; i < n; i++) blood.Tri(c + Vector3.Up * y, rim[i], rim[(i + 1) % n], Vector3.Up, Vector2.Zero, Vector2.Zero, Vector2.Zero);
		}
		Vector3 pool = new(-18.2f, 0, CrossEnd - 2.6f);
		Splat(pool, 0.9f, 0.006f);
		Vector3 door = new(-TransHalf + 0.8f, 0, (NaveEnd + CrossEnd) * 0.5f);
		for (int i = 1; i <= 18; i++)
		{
			float t = i / 18f;
			var p = pool.Lerp(door, t) + new Vector3(rng.RandfRange(-0.2f, 0.2f), 0, rng.RandfRange(-0.2f, 0.2f));
			Splat(p, Mathf.Lerp(0.42f, 0.16f, t), 0.006f + 0.0005f * (i % 3));
		}
		blood.CommitTo(this, "Blood", false);
	}

	// ------------------------------------------------------------------ the apse's chapels

	/// <summary>Five small chapels opening off the apse (the owner's plan): a step up into each, a narrow
	/// vault, a lancet of stained glass at the back, a stone figure on a plinth and a candle burning before it.</summary>
	private void BuildApseChapels()
	{
		var k = new MeshKit();
		var wax = new MeshKit();
		wax.Mat(Wax);
		var glass = new[] { ChurchTextures.GlassMat(ChurchTextures.Lancet(7), 1.1f), ChurchTextures.GlassMat(ChurchTextures.Lancet(8), 1.1f), ChurchTextures.GlassMat(ChurchTextures.Lancet(9), 1.1f) };
		for (int i = 0; i < 7; i++)
		{
			if (!FacetHasChapel(i)) continue;
			ApseFacet(i, out var p0, out var along, out var inward, out float len);
			Vector3 o = -inward;
			float mid = len * 0.5f;
			Vector3 P(float u, float d, float y) { var q = p0 + along * u + o * d; return new Vector3(q.X, y, q.Z); }
			float f = ChapelFloorY, d0 = 0f, d1 = 0.9f + ChapelDepth;
			// the floor (a step up from the apse's), and its riser
			k.Mat(ChurchTextures.CobbleMat);
			k.Color = Colors.White;
			k.Quad(P(mid - ChapelHalf, d0, f), P(mid + ChapelHalf, d0, f), P(mid + ChapelHalf, d1, f), P(mid - ChapelHalf, d1, f), Vector3.Up);
			k.Quad(P(mid - ChapelHalf, 0, ChancelY), P(mid + ChapelHalf, 0, ChancelY), P(mid + ChapelHalf, 0, f), P(mid - ChapelHalf, 0, f), inward);
			var basis = Basis.LookingAt(inward, Vector3.Up);
			Collide(_marbleBody, (P(mid, (d0 + d1) * 0.5f, f - 0.3f)), new Vector3(ChapelHalf * 2f, 0.6f, d1 - d0), basis);
			// the side walls and the back wall (a lancet in it)
			var ash = ChurchTextures.AshlarMat;
			Wall(k, ash, P(mid - ChapelHalf, 0.9f, 0), o, along, 0.3f, 0f, ChapelDepth, 0f, _ => 8.2f, null);
			Wall(k, ash, P(mid + ChapelHalf, 0.9f, 0), o, -along, 0.3f, 0f, ChapelDepth, 0f, _ => 8.2f, null);
			Wall(k, ash, P(mid - ChapelHalf - 0.3f, d1, 0), along, inward, 0.3f, 0f, ChapelHalf * 2f + 0.6f, 0f, _ => 8.2f,
				new List<Hole> { Hole.Lancet(ChapelHalf + 0.3f - 0.55f, ChapelHalf + 0.3f + 0.55f, 2.2f, 5.6f, glass[i % 3], 1f) });
			foreach (var (c, s) in new[] { (P(mid - ChapelHalf - 0.15f, 0.9f + ChapelDepth * 0.5f, 4f), new Vector3(0.3f, 8f, ChapelDepth)), (P(mid + ChapelHalf + 0.15f, 0.9f + ChapelDepth * 0.5f, 4f), new Vector3(0.3f, 8f, ChapelDepth)), (P(mid, d1 + 0.15f, 4f), new Vector3(ChapelHalf * 2f + 0.6f, 8f, 0.3f)) })
				Collide(_stone, c, s, basis);
			// its vault: a pointed barrel running back from the opening
			k.Mat(ChurchTextures.PlasterMat);
			k.Color = Colors.White;
			for (float u = -ChapelHalf; u < ChapelHalf - 0.01f; u += 0.2f)
			{
				float un = Mathf.Min(u + 0.2f, ChapelHalf);
				float ya = 5.4f + Pointed(u, ChapelHalf, 2.4f), yb = 5.4f + Pointed(un, ChapelHalf, 2.4f);
				k.Quad(P(mid + u, 0.4f, ya), P(mid + un, 0.4f, yb), P(mid + un, d1, yb), P(mid + u, d1, ya), Vector3.Down);
			}
			// the figure on its plinth, a candle before it
			k.Mat(ChurchTextures.AshlarDampMat);
			Vector3 plinth = P(mid, d1 - 0.8f, f);
			BuildKit.Box(k, plinth + Vector3.Up * 0.45f, new Vector3(0.8f, 0.9f, 0.8f), 1f, BuildKit.Face.NY, basis);
			k.Cylinder(plinth + Vector3.Up * 0.9f, plinth + Vector3.Up * 2.2f, 0.28f, 0.18f, 8, true);
			k.Blob(plinth + Vector3.Up * 2.35f, new Vector3(0.14f, 0.17f, 0.15f), 50 + i, 0.1f);
			Collide(_stone, plinth + Vector3.Up * 0.45f, new Vector3(0.8f, 0.9f, 0.8f), basis);
			Candle(wax, P(mid, d1 - 1.45f, f), 0.35f, 0.04f);
			Candle(wax, P(mid - 0.5f, d1 - 1.3f, f), 0.22f, 0.035f);
			Warm(P(mid, d1 - 1.7f, f + 0.8f), 0.8f, 5.5f);
		}
		k.CommitTo(this, "ApseChapels", true).CastShadow = GeometryInstance3D.ShadowCastingSetting.DoubleSided;
		wax.CommitTo(this, "ChapelCandles", false);
	}

	/// <summary>An apse facet's frame: its start corner (on the inner face), its direction along, the way
	/// into the church, its length.</summary>
	private static void ApseFacet(int i, out Vector3 p0, out Vector3 along, out Vector3 inward, out float len)
	{
		const int facets = 7;
		const float ri = ApseR - 0.45f;
		float a0 = -Mathf.Pi * 0.5f + Mathf.Pi * i / facets, a1 = -Mathf.Pi * 0.5f + Mathf.Pi * (i + 1) / facets;
		p0 = new Vector3(Mathf.Sin(a0) * ri, 0, ChancelEnd + Mathf.Cos(a0) * ri);
		var p1 = new Vector3(Mathf.Sin(a1) * ri, 0, ChancelEnd + Mathf.Cos(a1) * ri);
		along = (p1 - p0).Normalized();
		len = (p1 - p0).Length();
		inward = -new Vector3(Mathf.Sin((a0 + a1) * 0.5f), 0, Mathf.Cos((a0 + a1) * 0.5f));
	}

	// ------------------------------------------------------------------ the red circle

	/// <summary>Round the high altar, worked into the apse's floor: a great circle of red light (rings, a star,
	/// marks like writing), glowing up into the dark, a red light over it.</summary>
	private void BuildSigil()
	{
		Vector3 c = new(0, ChancelY + 0.012f, ChancelEnd - 0.4f);
		const float r = 5.6f;
		var k = new MeshKit();
		// (2026-10-14h, the interiors pass: it glowed, an unshaded red emissive disc with a red light over it, a neon sign on
		// the floor. Now it's what it would be: old blood and paint gone brown-red, soaked into the stone, dull, seen only
		// by the candles left burning round it and by the lantern.)
		k.Mat(new StandardMaterial3D
		{
			ResourceName = "ch_sigil_paint", AlbedoTexture = ChurchTextures.Sigil, AlbedoColor = new Color(0.4f, 0.07f, 0.05f), Roughness = 0.8f, MetallicSpecular = 0.3f,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
		});
		k.Color = Colors.White;
		const int n = 48;
		for (int i = 0; i < n; i++)
		{
			float a0 = Mathf.Tau * i / n, a1 = Mathf.Tau * (i + 1) / n;
			Vector3 p0 = c + new Vector3(Mathf.Cos(a0) * r, 0, Mathf.Sin(a0) * r), p1 = c + new Vector3(Mathf.Cos(a1) * r, 0, Mathf.Sin(a1) * r);
			Vector2 U(Vector3 p) => new(0.5f + (p.X - c.X) / (2f * r), 0.5f + (p.Z - c.Z) / (2f * r));
			k.Tri(c, p0, p1, Vector3.Up, U(c), U(p0), U(p1));
		}
		k.CommitTo(this, "Sigil", false).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		// candles left burning at the star's five points, low, in their own wax; one warm light for them all
		var wax = new MeshKit();
		wax.Mat(WaxMaterial.Make(new Color(0.6f, 0.55f, 0.45f), 0.6f, "ch_sigil_wax"));
		var flames = new MeshKit();
		flames.Mat(new StandardMaterial3D { ResourceName = "ch_sigil_flame", AlbedoColor = new Color(1f, 0.8f, 0.42f), EmissionEnabled = true, Emission = new Color(1f, 0.62f, 0.26f), EmissionEnergyMultiplier = 3.5f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
		var rng = new RandomNumberGenerator { Seed = 3301 };
		for (int i = 0; i < 5; i++)
		{
			float a = Mathf.Tau * i / 5f - Mathf.Pi * 0.5f;
			// (the texture's star points, on the 0.7 ring; its u runs with x, its v with z)
			Vector3 p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (r * 0.7f);
			wax.Blob(p + Vector3.Up * 0.004f, new Vector3(0.14f, 0.008f, 0.12f), 90 + i, 0.3f);
			for (int s = 0; s < 3; s++)
			{
				if (s > 0 && rng.Randf() < 0.3f) continue;
				Vector3 q = p + new Vector3(rng.RandfRange(-0.08f, 0.08f), 0, rng.RandfRange(-0.08f, 0.08f));
				float h = s == 0 ? rng.RandfRange(0.12f, 0.2f) : rng.RandfRange(0.04f, 0.1f), rr = rng.RandfRange(0.025f, 0.04f);
				wax.Cylinder(q, q + Vector3.Up * h, rr * 1.1f, rr, 8, true);
				wax.Blob(q + Vector3.Up * h, new Vector3(rr, 0.01f, rr), i * 5 + s, 0.3f);
				if (s == 0 || rng.Randf() < 0.5f) flames.Blob(q + Vector3.Up * (h + 0.035f), new Vector3(0.011f, 0.026f, 0.011f), 2, 0f, false);
			}
		}
		wax.CommitTo(this, "SigilCandles", false);
		flames.CommitTo(this, "SigilFlames", false).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		AddChild(new OmniLight3D { Name = "SigilLight", Position = c + Vector3.Up * 0.7f, LightColor = new Color(1f, 0.66f, 0.38f), LightEnergy = 1.3f, OmniRange = 8f, OmniAttenuation = 1.3f, ShadowEnabled = false });
	}
}
