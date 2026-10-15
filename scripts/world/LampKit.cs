using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-15, the props pass; the owner: "make sure they're proportioned and not too blocky looking. For example the
/// green lamp in the room") Lamps built to real measure, turned and bent rather than stacked from boxes and primitive
/// cylinders. Each takes its own space (y = 0 the surface it stands on, its front toward -Z) and returns the light it
/// throws.
/// <list type="bullet">
/// <item><see cref="Banker"/>: the green banker's desk lamp (the Emeralite): an oval stepped brass base 21 x 14 cm, a
/// turned stem, a socket under the shade and a two-armed harp cradling it, a half-round trough of cased emerald glass
/// 27 cm long (green outside, opal white inside, so it glows from under), tipped toward the reader, a bead pull chain;
/// 37 cm to the shade's top. Its light is a pool on the desk under the shade (a spot), and only a little spill
/// besides.</item>
/// <item><see cref="Floor"/>: a standard lamp, 1.6 m: a weighted foot, a pole with turned collars, a drum shade.</item>
/// <item><see cref="Sconce"/>: a wall light: a round back plate, a scrolled arm, a cup and a flared shade.</item>
/// </list>
/// </summary>
public static class LampKit
{
	private static StandardMaterial3D _green, _opal, _bulb, _fabric;

	/// <summary>The shade's outside: dark emerald glass, glossy, a faint glow from the light inside it.</summary>
	public static StandardMaterial3D GreenGlass => _green ??= new StandardMaterial3D
	{
		ResourceName = "lamp_green_glass", AlbedoColor = new Color(0.06f, 0.3f, 0.13f), Roughness = 0.12f, MetallicSpecular = 0.9f,
		EmissionEnabled = true, Emission = new Color(0.1f, 0.55f, 0.22f), EmissionEnergyMultiplier = 0.32f, VertexColorUseAsAlbedo = true,
		RimEnabled = true, Rim = 0.35f, RimTint = 0.6f,
	};

	/// <summary>The shade's inside (cased opal glass) and a fabric shade's lining: white, lit by the bulb.</summary>
	public static StandardMaterial3D Opal => _opal ??= new StandardMaterial3D
	{
		ResourceName = "lamp_opal", AlbedoColor = new Color(1f, 0.93f, 0.8f), Roughness = 0.5f,
		EmissionEnabled = true, Emission = new Color(1f, 0.82f, 0.56f), EmissionEnergyMultiplier = 1.6f,
	};

	public static StandardMaterial3D Bulb => _bulb ??= new StandardMaterial3D
	{
		ResourceName = "lamp_bulb", AlbedoColor = new Color(1f, 0.92f, 0.75f), EmissionEnabled = true, Emission = new Color(1f, 0.82f, 0.55f),
		EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
	};

	/// <summary>A fabric shade's outside (vertex colour its colour), lit through from inside.</summary>
	public static StandardMaterial3D Fabric => _fabric ??= new StandardMaterial3D
	{
		ResourceName = "lamp_fabric", AlbedoTexture = DetailKit.Albedo(DetailKit.Kind.Fabric), AlbedoColor = Colors.White, Roughness = 0.95f,
		VertexColorUseAsAlbedo = true, EmissionEnabled = true, Emission = new Color(1f, 0.78f, 0.5f), EmissionEnergyMultiplier = 0.35f,
		Uv1Scale = new Vector3(6f, 6f, 1f),
	};

	private static readonly Color BrassTone = new(0.82f, 0.64f, 0.36f);

	/// <summary>The banker's lamp at <paramref name="xf"/> (its foot on the desk, the pull chain's side toward -Z) under
	/// <paramref name="parent"/>. Returns its pool of light (a spot under the shade).</summary>
	public static SpotLight3D Banker(Node3D parent, Transform3D xf, float energy = 2.4f, string name = "BankerLamp")
	{
		var brass = new MeshKit();
		brass.Mat(ItemTextures.BrassMat);
		brass.Color = BrassTone;
		brass.Xf = xf;
		// the base: an oval, a bevelled plinth stepping up into a low dome and a collar
		TurnedKit.Lathe(brass, Vector3.Zero, TurnedKit.P(0f, 0f, 0.105f, 0f, 0.105f, 0.007f, 0.099f, 0.012f, 0.09f, 0.0135f, 0.088f, 0.019f,
			0.078f, 0.026f, 0.06f, 0.033f, 0.04f, 0.039f, 0.026f, 0.044f, 0.02f, 0.05f, 0.016f, 0.053f, 0f, 0.054f), 20, 38f, 1f, 0.68f);
		// the stem, a knop half way, a collar at the top where the harp and the socket join it
		TurnedKit.Lathe(brass, Vector3.Zero, TurnedKit.P(0f, 0.053f, 0.013f, 0.053f, 0.011f, 0.062f, 0.0075f, 0.075f, 0.0075f, 0.118f, 0.012f, 0.124f,
			0.0135f, 0.13f, 0.012f, 0.136f, 0.0075f, 0.142f, 0.0075f, 0.2f, 0.012f, 0.205f, 0.013f, 0.215f, 0.0075f, 0.222f, 0.0075f, 0.262f, 0f, 0.262f), 10);
		// the harp: two arms out of the collar, up to the shade's ends, a ball finial at each
		foreach (float s in new[] { -1f, 1f })
		{
			TurnedKit.Rod(brass, TurnedKit.Curve(new Vector3(0, 0.21f, 0), new Vector3(s * 0.1f, 0.2f, 0), new Vector3(s * 0.112f, 0.268f, 0), 7), 0.0042f, 6, true);
			TurnedKit.Lathe(brass, new Vector3(s * 0.112f, 0.262f, 0), TurnedKit.P(0f, 0f, 0.006f, 0.002f, 0.0075f, 0.007f, 0.005f, 0.012f, 0f, 0.013f), 6);
		}
		// the socket: a short brass drum lying along the shade under its middle
		var sockXf = xf * new Transform3D(new Basis(Vector3.Back, Mathf.Pi * 0.5f), new Vector3(0.04f, 0.278f, 0));
		TurnedKit.Lathe(brass, sockXf, TurnedKit.P(0f, 0f, 0.013f, 0f, 0.015f, 0.006f, 0.015f, 0.05f, 0.012f, 0.058f, 0f, 0.058f), 10);
		brass.CommitTo(parent, name + "Brass", true);
		// the pull chain off the socket's end, hanging down past the front rim, a brass drop on it (casting nothing: right
		// under the bulb, its shadow was a black blot across the pool)
		var chain = new MeshKit();
		chain.Mat(ItemTextures.BrassMat);
		chain.Color = BrassTone;
		chain.Xf = xf;
		Vector3 c0 = new(-0.022f, 0.276f, -0.012f), c1 = new(-0.024f, 0.205f, -0.05f);
		TurnedKit.Rod(chain, TurnedKit.Curve(c0, new Vector3(-0.023f, 0.24f, -0.05f), c1, 6), 0.0018f, 4, false);
		TurnedKit.Lathe(chain, c1 + Vector3.Down * 0.024f, TurnedKit.P(0f, 0f, 0.0055f, 0.006f, 0.006f, 0.012f, 0.003f, 0.022f, 0.002f, 0.025f, 0f, 0.025f), 8);
		chain.CommitTo(parent, name + "Chain", false);

		// the shade: a half-round trough of cased glass, its rim tipped down toward the reader
		var shadeXf = xf * new Transform3D(new Basis(Vector3.Right, -0.2f), new Vector3(0, 0.282f, 0.008f));
		var glass = new MeshKit();
		glass.Xf = shadeXf;
		const float L = 0.135f, D = 0.072f, H = 0.088f, T = 0.004f, End = 0.016f;
		const int Seg = 12;
		Vector2 Sec(int i, float shrink)
		{
			// the section (z, y) at step i of Seg round from the back rim (0) over the top to the front rim (Seg): a
			// half-ellipse whose rims flare out a little
			float t = Mathf.Pi * i / Seg;
			float flare = Mathf.Pow(Mathf.Abs(Mathf.Cos(t)), 6f) * 0.006f;
			return new Vector2(Mathf.Cos(t) * (D + flare - shrink), Mathf.Sin(t) * (H - shrink) - flare * 0.4f);
		}
		// the stations along the shade: its body, then each end rounding in (scaled toward its middle)
		float[] xs = { -L, -L + End * 0.35f, -L + End, L - End, L - End * 0.35f, L };
		float[] sc = { 0.78f, 0.93f, 1f, 1f, 0.93f, 0.78f };
		void Surface(Material m, float shrink, bool inside)
		{
			glass.Mat(m);
			glass.Color = inside ? Colors.White : Colors.White;
			for (int s = 0; s < xs.Length - 1; s++)
				for (int i = 0; i < Seg; i++)
				{
					Vector3 V(int si, int ii) { var q = Sec(ii, shrink) * sc[si]; return new Vector3(xs[si] * (inside ? (L - T) / L : 1f), q.Y, q.X); }
					Vector3 Nrm(int si, int ii)
					{
						float t = Mathf.Pi * ii / Seg;
						var n = new Vector3(0, Mathf.Sin(t) / H, Mathf.Cos(t) / D).Normalized();
						if (si == 0 || si == xs.Length - 1) n = (n + Vector3.Right * Mathf.Sign(xs[si]) * 1.4f).Normalized();
						else if (si == 1 || si == xs.Length - 2) n = (n + Vector3.Right * Mathf.Sign(xs[si]) * 0.5f).Normalized();
						return inside ? -n : n;
					}
					Vector3 a = V(s, i), b = V(s + 1, i), c = V(s + 1, i + 1), d = V(s, i + 1);
					glass.Tri(a, b, c, Nrm(s, i), Nrm(s + 1, i), Nrm(s + 1, i + 1), new Vector2(xs[s], i / (float)Seg), new Vector2(xs[s + 1], i / (float)Seg), new Vector2(xs[s + 1], (i + 1) / (float)Seg));
					glass.Tri(a, c, d, Nrm(s, i), Nrm(s + 1, i + 1), Nrm(s, i + 1), new Vector2(xs[s], i / (float)Seg), new Vector2(xs[s + 1], (i + 1) / (float)Seg), new Vector2(xs[s], (i + 1) / (float)Seg));
				}
			// the end caps: a half-ellipse fan
			foreach (int si in new[] { 0, xs.Length - 1 })
			{
				float x = xs[si] * (inside ? (L - T) / L : 1f);
				Vector3 n = Vector3.Right * Mathf.Sign(xs[si]) * (inside ? -1f : 1f);
				Vector3 mid = new(x, 0, 0);
				for (int i = 0; i < Seg; i++)
				{
					var q0 = Sec(i, shrink) * sc[si];
					var q1 = Sec(i + 1, shrink) * sc[si];
					glass.Tri(mid, new Vector3(x, q0.Y, q0.X), new Vector3(x, q1.Y, q1.X), n, new Vector2(0.5f, 0), new Vector2(i / (float)Seg, 1), new Vector2((i + 1) / (float)Seg, 1));
				}
			}
		}
		Surface(GreenGlass, 0f, false);
		Surface(Opal, T, true);
		// the rims: the glass's edge, a pale line of the white casing showing under the green
		glass.Mat(Opal);
		glass.Color = new Color(0.75f, 0.85f, 0.72f);
		foreach (int i in new[] { 0, Seg })
		{
			Vector2 o = Sec(i, 0f), n = Sec(i, T);
			for (int s = 0; s < xs.Length - 1; s++)
			{
				Vector3 a = new(xs[s], o.Y * sc[s], o.X * sc[s]), b = new(xs[s + 1], o.Y * sc[s + 1], o.X * sc[s + 1]);
				Vector3 c = new(xs[s + 1], n.Y * sc[s + 1], n.X * sc[s + 1]), d = new(xs[s], n.Y * sc[s], n.X * sc[s]);
				glass.Quad(a, b, c, d, Vector3.Down);
			}
		}
		glass.CommitTo(parent, name + "Shade", true);

		// the bulb, lying in the shade along the socket
		var bulb = new MeshKit();
		bulb.Mat(Bulb);
		bulb.Xf = xf * new Transform3D(new Basis(Vector3.Back, -Mathf.Pi * 0.5f), new Vector3(0.04f, 0.29f, 0.004f));
		TurnedKit.Lathe(bulb, Vector3.Zero, TurnedKit.P(0f, 0f, 0.011f, 0f, 0.012f, 0.014f, 0.02f, 0.034f, 0.021f, 0.048f, 0.016f, 0.06f, 0f, 0.066f), 8);
		var bm = bulb.CommitTo(parent, name + "Bulb", false);
		bm.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

		// its light: a warm pool on the desk under the shade, tipped toward the reader as the shade is
		var spot = new SpotLight3D
		{
			Name = name + "Pool", LightColor = new Color(1f, 0.84f, 0.62f), LightEnergy = energy, SpotRange = 1.9f, SpotAngle = 58f, SpotAngleAttenuation = 0.85f,
			SpotAttenuation = 0.9f, ShadowEnabled = true,
		};
		parent.AddChild(spot);
		spot.Transform = xf * new Transform3D(new Basis(Vector3.Right, -Mathf.Pi * 0.5f - 0.2f), new Vector3(0, 0.28f, 0.004f));
		// and a little spill: the glow through the green glass and off the brass
		parent.AddChild(new OmniLight3D
		{
			Name = name + "Spill", Transform = xf * new Transform3D(Basis.Identity, new Vector3(0, 0.3f, -0.05f)), LightColor = new Color(0.75f, 0.9f, 0.62f),
			LightEnergy = energy * 0.22f, OmniRange = 4.5f, OmniAttenuation = 1.4f, ShadowEnabled = false,
		});
		return spot;
	}

	/// <summary>A turned brass candlestick standing at <paramref name="at"/>, <paramref name="h"/> to its socket's lip
	/// (a domed foot 0.45 h across, a baluster stem, a drip pan), and in <paramref name="wax"/> (if given) a candle
	/// <paramref name="candle"/> tall, burnt down unevenly, a run of wax down its side. Returns the wick's top.</summary>
	public static Vector3 Candlestick(MeshKit metal, MeshKit wax, Vector3 at, float h = 0.22f, float candle = 0.16f, int seed = 0)
	{
		float f = h / 0.22f;
		var prof = new List<Vector2>();
		foreach (var p in TurnedKit.P(0f, 0f, 0.05f, 0f, 0.05f, 0.006f, 0.044f, 0.014f, 0.032f, 0.026f, 0.016f, 0.036f, 0.011f, 0.046f, 0.015f, 0.054f,
			0.011f, 0.062f, 0.008f, 0.09f, 0.012f, 0.13f, 0.017f, 0.15f, 0.012f, 0.172f, 0.008f, 0.186f, 0.011f, 0.194f, 0.032f, 0.2f, 0.034f, 0.204f,
			0.016f, 0.206f, 0.015f, 0.22f, 0.012f, 0.22f, 0.012f, 0.212f, 0f, 0.212f))
			prof.Add(new Vector2(p.X * Mathf.Sqrt(f), p.Y * f));
		TurnedKit.Lathe(metal, at, prof, 12, 40f);
		Vector3 top = at + Vector3.Up * (h - 0.008f * f);
		if (wax == null) return top;
		float r = 0.0115f * Mathf.Sqrt(f);
		float lean = (seed % 3 - 1) * 0.004f;
		// the candle, its top burnt into a hollow round the wick, a run down one side
		TurnedKit.Lathe(wax, top, TurnedKit.P(0f, 0f, r, 0f, r, candle - 0.008f, r * 0.85f, candle, r * 0.4f, candle - 0.004f, 0f, candle - 0.003f), 8, 40f);
		float a = seed * 2.1f;
		Vector3 side = new(Mathf.Cos(a), 0, Mathf.Sin(a));
		TurnedKit.Rod(wax, new[] { top + side * r * 0.9f + Vector3.Up * (candle - 0.006f), top + side * (r + 0.002f) + Vector3.Up * (candle * 0.6f), top + side * (r + 0.001f) + Vector3.Up * (candle * 0.35f + lean) }, 0.0028f, 4, true);
		return top + Vector3.Up * candle;
	}

	/// <summary>A standard lamp at <paramref name="xf"/> (its foot on the floor), its shade's bottom at
	/// <paramref name="height"/> less 0.26 m, coloured <paramref name="shade"/>. Returns its light.</summary>
	public static OmniLight3D Floor(Node3D parent, Transform3D xf, Color shade, float energy, float height = 1.62f, string name = "FloorLamp")
	{
		var brass = new MeshKit();
		brass.Mat(ItemTextures.BrassMat);
		brass.Color = BrassTone * 0.92f;
		brass.Xf = xf;
		float top = height - 0.3f;   // the pole's top, under the shade's middle
		// a weighted foot: a domed disc, a step, a turned boss
		TurnedKit.Lathe(brass, Vector3.Zero, TurnedKit.P(0f, 0f, 0.15f, 0f, 0.15f, 0.012f, 0.14f, 0.022f, 0.11f, 0.034f, 0.07f, 0.042f, 0.045f, 0.05f,
			0.04f, 0.07f, 0.05f, 0.085f, 0.035f, 0.1f, 0.018f, 0.11f, 0f, 0.11f), 16);
		// the pole, collars at a third and two thirds, a ribbed knop under the shade
		var prof = new List<Vector2> { new(0f, 0.105f), new(0.014f, 0.105f) };
		foreach (float f in new[] { 0.36f, 0.68f })
		{
			float y = 0.11f + (top - 0.11f) * f;
			prof.AddRange(new[] { new Vector2(0.013f, y - 0.025f), new Vector2(0.022f, y - 0.012f), new Vector2(0.024f, y), new Vector2(0.022f, y + 0.012f), new Vector2(0.013f, y + 0.025f) });
		}
		prof.AddRange(new[] { new Vector2(0.012f, top - 0.06f), new Vector2(0.024f, top - 0.04f), new Vector2(0.026f, top - 0.02f), new Vector2(0.016f, top), new Vector2(0.01f, top + 0.06f), new Vector2(0f, top + 0.06f) });
		TurnedKit.Lathe(brass, Vector3.Zero, prof, 10);
		// the shade's spider: three wires from the bulb's collar out to the shade's top ring
		for (int i = 0; i < 3; i++)
		{
			float a = Mathf.Tau * i / 3f;
			TurnedKit.Rod(brass, new[] { new Vector3(0, top + 0.05f, 0), new Vector3(Mathf.Cos(a) * 0.12f, top + 0.19f, Mathf.Sin(a) * 0.12f) }, 0.0025f, 4, false);
		}
		brass.CommitTo(parent, name + "Body", true);
		// the drum shade: flared, a rolled binding top and bottom, lit through
		var cloth = new MeshKit();
		cloth.Xf = xf;
		cloth.Mat(Fabric);
		cloth.Color = shade;
		float y0 = top - 0.06f, y1 = top + 0.2f;
		var outer = TurnedKit.P(0.215f, y0, 0.212f, y0 + 0.05f, 0.19f, y0 + 0.15f, 0.135f, y1);
		TurnedKit.Lathe(cloth, Vector3.Zero, outer, 18, 60f);
		cloth.Mat(Opal);
		cloth.Color = new Color(1f, 0.85f, 0.65f);
		TurnedKit.Lathe(cloth, Vector3.Zero, TurnedKit.P(0.129f, y1 - 0.002f, 0.184f, y0 + 0.15f, 0.206f, y0 + 0.05f, 0.209f, y0 + 0.002f), 18, 60f);
		cloth.Mat(ItemTextures.BrassMat);
		cloth.Color = BrassTone * 0.8f;
		foreach (var (r, y) in new[] { (0.215f, y0), (0.135f, y1) })
			TurnedKit.Lathe(cloth, Vector3.Zero, TurnedKit.P(r - 0.006f, y - 0.004f, r + 0.004f, y - 0.004f, r + 0.006f, y, r + 0.004f, y + 0.005f, r - 0.006f, y + 0.005f), 18);
		cloth.CommitTo(parent, name + "Shade", true);
		var bulb = new MeshKit();
		bulb.Mat(Bulb);
		TurnedKit.Lathe(bulb, xf * new Vector3(0, top + 0.055f, 0), TurnedKit.P(0f, 0f, 0.012f, 0f, 0.014f, 0.016f, 0.03f, 0.05f, 0.025f, 0.075f, 0f, 0.085f), 8);
		bulb.CommitTo(parent, name + "Bulb", false).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		var light = new OmniLight3D { Name = name + "Light", LightColor = new Color(1f, 0.82f, 0.6f), LightEnergy = energy, OmniRange = 7f, OmniAttenuation = 1.1f, ShadowEnabled = false };
		parent.AddChild(light);
		light.Transform = xf * new Transform3D(Basis.Identity, new Vector3(0, top + 0.02f, 0));
		return light;
	}

	/// <summary>A wall sconce: <paramref name="at"/> the shade's middle, <paramref name="toWall"/> the way to the wall it's
	/// fixed to (the plate <paramref name="reach"/> behind the shade). Returns its light.</summary>
	public static OmniLight3D Sconce(Node3D parent, Vector3 at, Vector3 toWall, Color shade, float energy, float reach = 0.3f, string name = "Sconce")
	{
		Vector3 n = toWall.Normalized();
		var brass = new MeshKit();
		brass.Mat(ItemTextures.BrassMat);
		brass.Color = BrassTone * 0.95f;
		Vector3 plate = at + n * reach + Vector3.Down * 0.14f;
		// the back plate: a turned oval rosette standing on the wall
		var plateXf = new Transform3D(Basis.LookingAt(n, Vector3.Up) * new Basis(Vector3.Right, Mathf.Pi * 0.5f), plate - n * 0.002f);
		TurnedKit.Lathe(brass, plateXf, TurnedKit.P(0f, 0f, 0.055f, 0f, 0.055f, 0.006f, 0.045f, 0.014f, 0.025f, 0.02f, 0.016f, 0.03f, 0f, 0.032f), 14, 38f, 0.75f, 1f);
		// the arm: out of the rosette, down in a scroll and up under the cup
		Vector3 cupFoot = at + Vector3.Down * 0.11f;
		TurnedKit.Rod(brass, TurnedKit.Curve(plate - n * 0.03f, plate - n * reach * 0.6f + Vector3.Down * 0.09f, cupFoot + Vector3.Down * 0.035f, 9), 0.008f, 6, true);
		// the cup and the candle-sleeve the bulb sits in
		TurnedKit.Lathe(brass, cupFoot + Vector3.Down * 0.04f, TurnedKit.P(0f, 0f, 0.012f, 0f, 0.01f, 0.012f, 0.02f, 0.022f, 0.045f, 0.035f, 0.05f, 0.04f, 0.044f, 0.042f, 0.016f, 0.04f, 0f, 0.04f), 12);
		brass.CommitTo(parent, name + "Body", true);
		// a small flared shade of parchment over the bulb
		var cloth = new MeshKit();
		cloth.Mat(Fabric);
		cloth.Color = shade;
		float y0 = -0.07f, y1 = 0.07f;
		TurnedKit.Lathe(cloth, at, TurnedKit.P(0.11f, y0, 0.1f, y0 + 0.05f, 0.075f, y1 - 0.02f, 0.06f, y1), 14, 60f);
		cloth.Mat(Opal);
		cloth.Color = new Color(1f, 0.85f, 0.65f);
		TurnedKit.Lathe(cloth, at, TurnedKit.P(0.056f, y1 - 0.002f, 0.071f, y1 - 0.02f, 0.096f, y0 + 0.05f, 0.106f, y0 + 0.002f), 14, 60f);
		cloth.CommitTo(parent, name + "Shade", true);
		var bulb = new MeshKit();
		bulb.Mat(Bulb);
		TurnedKit.Lathe(bulb, cupFoot, TurnedKit.P(0f, 0f, 0.012f, 0f, 0.013f, 0.03f, 0.02f, 0.06f, 0.016f, 0.085f, 0f, 0.1f), 8);
		bulb.CommitTo(parent, name + "Bulb", false).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		var light = new OmniLight3D { Name = name + "Light", Position = at + Vector3.Down * 0.02f, LightColor = new Color(1f, 0.82f, 0.6f), LightEnergy = energy, OmniRange = 7f, OmniAttenuation = 1.1f, ShadowEnabled = false };
		parent.AddChild(light);
		return light;
	}
}
