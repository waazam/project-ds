using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.World;

namespace ProjectDS.Player;

/// <summary>
/// The things the right hand holds, built for being held (2026-10-13j, the owner: "create objects that look good and fit
/// in the hands correctly and face the correct way"). Each is modelled on the real thing at its real size, in grip
/// space (<see cref="FirstPersonHand"/>'s socket: the origin in the middle of the closed fist, the handle up +Y through
/// it, -Z forward, away from the eye), and worn the way a found thing is worn: oiled and grimy where it's held, scale on
/// the forged steel, a honed edge catching the light, scuffed leatherette, a rusted bar.
/// <list type="bullet">
/// <item><b>The camera:</b> a 1970s all-mechanical 35 mm SLR (143 x 92 x 48 mm, the size of a K1000 or an AE-1): a chrome
/// top plate, the prism's hump over the lens, black pebbled leatherette, a 50 mm lens with its knurled focus ring and its
/// coated glass; the advance lever, the speed dial and the shutter button on the right, the rewind crank on the left. Held
/// by its right end, the lens away from the eye.</item>
/// <item><b>The axe:</b> a 26" camp axe: an oval hickory handle swelling to a knob at the butt, a forged head (poll,
/// eye and a flaring bit) dark with forge scale, the edge honed bright. Held low on the handle, the head up, the bit
/// forward.</item>
/// <item><b>The hammer:</b> a 16 oz curved-claw hammer on a hickory handle: the round, chamfered face forward, the claw's
/// split prongs curling back.</item>
/// <item><b>The knife:</b> a fixed-blade hunting knife: stacked leather washers, a brass guard, a drop-point blade, its
/// spine up and its edge down.</item>
/// <item><b>The crowbar:</b> a 24" wrecking bar of hexagonal stock, rusted: the gooseneck's claw up and forward, the
/// chisel end below the fist.</item>
/// <item><b>The lighter:</b> a brass flip-top lighter, worn through at its corners, upright in the fingers.</item>
/// <item><b>The walkie-talkie:</b> an '80s handheld two-way radio: a tough olive-black case, a speaker grille, two knobs
/// and a rubber antenna on top, the push-to-talk bar on its side, a channel sticker, the belt clip on its back; its LED
/// dark until it comes alive outside the bunker.</item>
/// <item><b>The cap:</b> the newel post's stone cap, by its neck, the ball up and the broken spigot down.</item>
/// </list>
/// The flare (<see cref="HeldFlare"/>) and the flamethrower (<see cref="Flamethrower"/>) build their own.
/// </summary>
public static class HeldModels
{
	/// <summary>How a thing is held: the hand's pose, where the hand is (camera space) and how it's turned (degrees), and
	/// where the thing sits in the hand (grip space).</summary>
	public sealed class Spec
	{
		public HandPose Pose = HandPose.Stick;
		public Vector3 HandAt = new(0.24f, -0.26f, -0.46f);
		public Vector3 HandTurn;
		public Vector3 ItemAt;
		public Vector3 ItemTurn;
	}

	/// <summary>The hand's Stick pose closes with its knuckles toward its own -X (the forearm off along +X, the back of the
	/// hand to +Z): a tool's working end (the bit, the face, the claw, the edge) turned that way leads with the knuckles, as
	/// in a real grip. The hand's turn (yaw -60) then brings the knuckles round to forward and a little in, the forearm
	/// back to the lower right, and the back of the hand (never the palm) to the eye.</summary>
	private static readonly Vector3 KnucklesWay = new(0f, 90f, 0f);

	public static Spec SpecOf(ToolKind kind) => kind switch
	{
		ToolKind.Camera => new Spec { Pose = HandPose.Camera, HandAt = new(0.2f, -0.22f, -0.42f), HandTurn = new(-6f, -10f, 4f), ItemAt = new(-0.06f, 0.01f, 0f) },
		ToolKind.Axe => new Spec { HandAt = new(0.25f, -0.26f, -0.46f), HandTurn = new(-24f, -72f, 8f), ItemTurn = KnucklesWay },
		ToolKind.Hammer => new Spec { HandAt = new(0.24f, -0.24f, -0.44f), HandTurn = new(-22f, -72f, 8f), ItemTurn = KnucklesWay },
		ToolKind.Crowbar => new Spec { HandAt = new(0.25f, -0.27f, -0.46f), HandTurn = new(-18f, -72f, 8f), ItemTurn = KnucklesWay },
		ToolKind.Knife => new Spec { HandAt = new(0.23f, -0.25f, -0.44f), HandTurn = new(-36f, -60f, 6f), ItemTurn = KnucklesWay },
		ToolKind.Lighter => new Spec { Pose = HandPose.Stick, HandAt = new(0.19f, -0.21f, -0.4f), HandTurn = new(-6f, -60f, 4f), ItemAt = new(0f, 0.012f, 0f), ItemTurn = KnucklesWay },
		ToolKind.Radio => new Spec { Pose = HandPose.Stick, HandAt = new(0.21f, -0.22f, -0.44f), HandTurn = new(-8f, -60f, 6f), ItemAt = new(0f, 0.045f, 0f), ItemTurn = new(0f, -30f, 0f) },   // (its speaker turned to the eye)
		ToolKind.NewelPost => new Spec { HandAt = new(0.26f, -0.31f, -0.62f), HandTurn = new(-10f, -66f, 6f), ItemTurn = KnucklesWay },
		_ => new Spec(),
	};

	/// <summary>Builds <paramref name="kind"/> in grip space under a new node (null if it has no held model here).</summary>
	public static Node3D Build(ToolKind kind)
	{
		var root = new Node3D { Name = "Held" + kind };
		var k = new MeshKit();
		switch (kind)
		{
			case ToolKind.Camera: Camera(k); break;
			case ToolKind.Axe: Axe(k); break;
			case ToolKind.Hammer: Hammer(k); break;
			case ToolKind.Knife: Knife(k); break;
			case ToolKind.Crowbar: Crowbar(k); break;
			case ToolKind.Lighter: Lighter(k); break;
			case ToolKind.Radio: Walkie(k, root); break;
			case ToolKind.NewelPost: Cap(k); break;
			default: root.Free(); return null;
		}
		var mi = k.CommitTo(root, "Mesh", false);
		mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		return root;
	}

	// ───────────────────────────── geometry ─────────────────────────────

	/// <summary>
	/// A tube of superellipse sections (|x/hx|^p + |y/hy|^p = 1: p = 2 an ellipse, 4 a soft rounded box, 10 nearly
	/// square) through <paramref name="c"/>, each section in the plane of <paramref name="u"/> (its x) and
	/// <paramref name="v"/> (its y). Smooth normals round it; flat caps.
	/// </summary>
	public static void SLoft(MeshKit k, IList<Vector3> c, IList<Vector2> half, float p, Vector3 u, Vector3 v, int sides,
		bool cap0, bool cap1, float uvScale = 8f)
	{
		int n = c.Count;
		var pts = new Vector3[n, sides + 1];
		var nr = new Vector3[n, sides + 1];
		var vc = new float[n];
		float e = 2f / p;
		for (int r = 0; r < n; r++)
		{
			float hx = Mathf.Max(half[r].X, 1e-5f), hy = Mathf.Max(half[r].Y, 1e-5f);
			for (int i = 0; i <= sides; i++)
			{
				float a = Mathf.Tau * i / sides + Mathf.Pi / sides;
				float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
				float x = Mathf.Sign(ca) * Mathf.Pow(Mathf.Abs(ca), e) * hx;
				float y = Mathf.Sign(sa) * Mathf.Pow(Mathf.Abs(sa), e) * hy;
				pts[r, i] = c[r] + u * x + v * y;
				var g = u * (Mathf.Sign(ca) * Mathf.Pow(Mathf.Abs(ca), 2f - e) / hx) + v * (Mathf.Sign(sa) * Mathf.Pow(Mathf.Abs(sa), 2f - e) / hy);
				nr[r, i] = g.LengthSquared() > 1e-12f ? g.Normalized() : u;
			}
			vc[r] = r == 0 ? 0f : vc[r - 1] + c[r].DistanceTo(c[r - 1]) * uvScale;
		}
		for (int r = 0; r < n - 1; r++)
			for (int i = 0; i < sides; i++)
			{
				int j = i + 1;
				float u0 = (float)i / sides * 2f, u1 = (float)j / sides * 2f;
				k.Tri(pts[r, i], pts[r, j], pts[r + 1, j], nr[r, i], nr[r, j], nr[r + 1, j], new Vector2(u0, vc[r]), new Vector2(u1, vc[r]), new Vector2(u1, vc[r + 1]));
				k.Tri(pts[r, i], pts[r + 1, j], pts[r + 1, i], nr[r, i], nr[r + 1, j], nr[r + 1, i], new Vector2(u0, vc[r]), new Vector2(u1, vc[r + 1]), new Vector2(u0, vc[r + 1]));
			}
		void Cap(int r, Vector3 dir)
		{
			for (int i = 0; i < sides; i++)
			{
				var a = pts[r, i] - c[r]; var b = pts[r, i + 1] - c[r];
				k.Tri(c[r], pts[r, i], pts[r, i + 1], dir, new Vector2(0.5f, 0.5f),
					new Vector2(0.5f + a.Dot(u) * uvScale, 0.5f + a.Dot(v) * uvScale), new Vector2(0.5f + b.Dot(u) * uvScale, 0.5f + b.Dot(v) * uvScale));
			}
		}
		if (n > 1)
		{
			if (cap0) Cap(0, (c[0] - c[1]).Normalized());
			if (cap1) Cap(n - 1, (c[n - 1] - c[n - 2]).Normalized());
		}
	}

	/// <summary>A rounded box: <see cref="SLoft"/> along <paramref name="axis"/>, its ends eased in by <paramref name="bevel"/>.</summary>
	public static void RBox(MeshKit k, Vector3 centre, Vector3 size, Vector3 axis, Vector3 u, float p = 6f, float bevel = 0.002f, int sides = 28, float uvScale = 8f)
	{
		axis = axis.Normalized(); u = u.Normalized();
		var v = axis.Cross(u).Normalized();
		// size: x along u, y along v, z along the axis
		float hx = size.X * 0.5f, hy = size.Y * 0.5f, hz = size.Z * 0.5f;
		var cs = new List<Vector3>(); var hs = new List<Vector2>();
		void S(float z, float shrink) { cs.Add(centre + axis * z); hs.Add(new Vector2(hx - shrink, hy - shrink)); }
		S(-hz, bevel); S(-hz + bevel * 0.35f, bevel * 0.25f); S(-hz + bevel, 0f); S(hz - bevel, 0f); S(hz - bevel * 0.35f, bevel * 0.25f); S(hz, bevel);
		SLoft(k, cs, hs, p, u, v, sides, true, true, uvScale);
	}

	/// <summary>A turned shape round <paramref name="axis"/> from <paramref name="at"/>: each (radius, distance along).</summary>
	public static void Turn(MeshKit k, Vector3 at, Vector3 axis, IList<Vector2> profile, int sides, bool cap0, bool cap1, float uvScale = 8f)
	{
		axis = axis.Normalized();
		var refv = Mathf.Abs(axis.Y) > 0.9f ? Vector3.Right : Vector3.Up;
		var u = refv.Cross(axis).Normalized();
		var v = axis.Cross(u).Normalized();
		var cs = new List<Vector3>(); var hs = new List<Vector2>();
		foreach (var p in profile) { cs.Add(at + axis * p.Y); hs.Add(new Vector2(p.X, p.X)); }
		SLoft(k, cs, hs, 2f, u, v, sides, cap0, cap1, uvScale);
	}

	/// <summary>A ring of <paramref name="count"/> little ridges round an axis (knurling on a ring, ribs on a knob).</summary>
	private static void Ridges(MeshKit k, Vector3 at, Vector3 axis, float r, float len, int count, float h, float w)
	{
		axis = axis.Normalized();
		var refv = Mathf.Abs(axis.Y) > 0.9f ? Vector3.Right : Vector3.Up;
		var u = refv.Cross(axis).Normalized();
		var v = axis.Cross(u).Normalized();
		for (int i = 0; i < count; i++)
		{
			float a = Mathf.Tau * i / count;
			var d = u * Mathf.Cos(a) + v * Mathf.Sin(a);
			var side = axis.Cross(d).Normalized();
			k.Box(at + d * (r + h * 0.5f), new Vector3(w, h, len), 40f, new Basis(side, d, axis));
		}
	}

	// ───────────────────────────── textures and materials ─────────────────────────────

	private static readonly Dictionary<string, Material> _mats = new();

	private static StandardMaterial3D Mat(string key, Func<StandardMaterial3D> make)
	{
		if (_mats.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
		var s = make();
		s.ResourceName = key;
		s.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
		_mats[key] = s;
		return s;
	}

	private static float F(int x, int y, int w, int h, int cx, int cy, int oct, int seed) => ItemTextures.Fbm(x, y, w, h, cx, cy, oct, seed);

	/// <summary>A normal map from a height function (tileable), <paramref name="strength"/> how steep.</summary>
	private static Texture2D NormalFrom(string key, int w, int h, Func<int, int, float> height, float strength)
		=> ItemTextures.Make(key, w, h, (x, y) =>
		{
			float hl = height((x - 1 + w) % w, y), hr = height((x + 1) % w, y);
			float hd = height(x, (y - 1 + h) % h), hu = height(x, (y + 1) % h);
			var n = new Vector3((hl - hr) * strength, (hd - hu) * strength, 1f).Normalized();
			return new Color(n.X * 0.5f + 0.5f, n.Y * 0.5f + 0.5f, n.Z * 0.5f + 0.5f);
		});

	/// <summary>Hickory, oiled and handled: the grain running along V, darker and smoother where hands have been.</summary>
	public static StandardMaterial3D Hickory => Mat("held_hickory", () => new StandardMaterial3D
	{
		AlbedoTexture = ItemTextures.Make("held_hickory", 64, 256, (x, y) =>
		{
			float warp = F(x, y, 64, 256, 2, 4, 3, 811) * 7f;
			float grain = 0.5f + 0.5f * Mathf.Sin(x / 64f * Mathf.Tau * 6f + warp);
			float fine = F(x, y, 64, 256, 16, 8, 2, 813);
			float t = 0.62f + 0.22f * grain + 0.16f * fine;
			var c = new Color(0.5f, 0.35f, 0.21f) * t;
			float grime = Mathf.Clamp(F(x, y, 64, 256, 4, 16, 3, 817) * 1.4f - 0.45f, 0f, 1f);
			c = c.Lerp(new Color(0.16f, 0.11f, 0.07f), grime * 0.6f);
			return new Color(c.R, c.G, c.B);
		}),
		NormalEnabled = true,
		NormalTexture = NormalFrom("held_hickory_n", 64, 256, (x, y) => 0.5f + 0.5f * Mathf.Sin(x / 64f * Mathf.Tau * 6f + F(x, y, 64, 256, 2, 4, 3, 811) * 7f), 0.6f),
		Roughness = 0.62f, MetallicSpecular = 0.4f, VertexColorUseAsAlbedo = true,
	});

	/// <summary>Forged steel under a skin of forge scale, scratched bright here and there.</summary>
	public static StandardMaterial3D Forged => Mat("held_forged", () => new StandardMaterial3D
	{
		AlbedoTexture = ItemTextures.Make("held_forged", 128, 128, (x, y) =>
		{
			float scale = F(x, y, 128, 128, 6, 6, 4, 901);
			float pit = F(x, y, 128, 128, 24, 24, 2, 903);
			float scratch = Mathf.Pow(Mathf.Abs(Mathf.Sin((x * 0.9f + y * 0.15f + F(x, y, 128, 128, 3, 3, 2, 907) * 40f) * 0.7f)), 40f);
			float t = 0.16f + 0.1f * scale - 0.05f * Mathf.Max(0f, pit - 0.55f) * 4f + 0.18f * scratch;
			var c = new Color(t, t * 1.0f, t * 1.04f);
			float rust = Mathf.Clamp((F(x, y, 128, 128, 5, 5, 3, 911) - 0.62f) * 4f, 0f, 1f);
			c = c.Lerp(new Color(0.24f, 0.12f, 0.06f), rust * 0.55f);
			return c;
		}),
		Metallic = 0.75f, Roughness = 0.5f, VertexColorUseAsAlbedo = true,
		RoughnessTexture = ItemTextures.Make("held_forged_r", 64, 64, (x, y) => { float r = 0.45f + 0.4f * F(x, y, 64, 64, 6, 6, 3, 921); return new Color(r, r, r); }),
	});

	/// <summary>The honed edge and the struck face: bright, fine-scratched steel.</summary>
	public static StandardMaterial3D Honed => Mat("held_honed", () => new StandardMaterial3D
	{
		AlbedoColor = new Color(0.66f, 0.67f, 0.68f), Metallic = 0.95f, Roughness = 0.24f,
		AlbedoTexture = ItemTextures.Make("held_honed", 64, 16, (x, y) => { float s = 0.85f + 0.15f * F(x, y, 64, 16, 32, 2, 2, 931); return new Color(s, s, s); }),
	});

	/// <summary>Old iron gone to rust, the dark bar showing through where it's been handled.</summary>
	public static StandardMaterial3D RustIron => Mat("held_rust", () => new StandardMaterial3D
	{
		AlbedoTexture = ItemTextures.Make("held_rust", 128, 128, (x, y) =>
		{
			float r = F(x, y, 128, 128, 6, 6, 4, 941);
			float fine = F(x, y, 128, 128, 32, 32, 2, 943);
			var iron = new Color(0.1f, 0.095f, 0.09f) * (0.8f + 0.4f * fine);
			var rust = new Color(0.36f, 0.17f, 0.07f).Lerp(new Color(0.22f, 0.1f, 0.05f), fine);
			return iron.Lerp(rust, Mathf.SmoothStep(0.38f, 0.6f, r));
		}),
		NormalEnabled = true,
		NormalTexture = NormalFrom("held_rust_n", 64, 64, (x, y) => F(x, y, 64, 64, 16, 16, 3, 947), 2.2f),
		Metallic = 0.35f, Roughness = 0.85f, VertexColorUseAsAlbedo = true,
	});

	/// <summary>Black pebbled leatherette, scuffed.</summary>
	public static StandardMaterial3D Leatherette => Mat("held_leatherette", () => new StandardMaterial3D
	{
		AlbedoTexture = ItemTextures.Make("held_leatherette", 64, 64, (x, y) =>
		{
			float t = 0.05f + 0.02f * F(x, y, 64, 64, 16, 16, 2, 951) + 0.03f * Mathf.Max(0f, F(x, y, 64, 64, 3, 3, 3, 953) - 0.6f);
			return new Color(t, t, t * 1.05f);
		}),
		NormalEnabled = true,
		NormalTexture = NormalFrom("held_leatherette_n", 64, 64, (x, y) =>
		{
			// pebbles: a cell's distance, like the grain pressed into the vinyl
			float best = 9f;
			for (int i = -1; i <= 1; i++)
				for (int j = -1; j <= 1; j++)
				{
					int cx = ((x / 4) + i + 16) % 16, cy = ((y / 4) + j + 16) % 16;
					float px = (x / 4 + i) * 4 + ItemTextures.Hash(cx, cy, 957) * 4f, py = (y / 4 + j) * 4 + ItemTextures.Hash(cx, cy, 958) * 4f;
					best = Mathf.Min(best, (px - x) * (px - x) + (py - y) * (py - y));
				}
			return Mathf.Sqrt(best) / 3f;
		}, 1.6f),
		Roughness = 0.72f, MetallicSpecular = 0.45f, VertexColorUseAsAlbedo = true,
	});

	/// <summary>Satin chrome, the plating gone dull and the edges rubbed.</summary>
	public static StandardMaterial3D Chrome => Mat("held_chrome", () => new StandardMaterial3D
	{
		AlbedoTexture = ItemTextures.Make("held_chrome", 128, 32, (x, y) =>
		{
			float s = 0.7f + 0.12f * F(x, y, 128, 32, 64, 2, 2, 961) - 0.18f * Mathf.Max(0f, F(x, y, 128, 32, 4, 2, 3, 963) - 0.55f) * 3f;
			return new Color(s, s, s * 0.98f);
		}),
		AlbedoColor = new Color(0.78f, 0.78f, 0.77f), Metallic = 0.9f, Roughness = 0.32f, VertexColorUseAsAlbedo = true,
	});

	/// <summary>Black enamel, a little chipped (the lens barrel, the speed dial, the lever's tip).</summary>
	public static StandardMaterial3D BlackEnamel => Mat("held_black", () => new StandardMaterial3D
	{
		AlbedoColor = new Color(0.045f, 0.045f, 0.048f), Roughness = 0.4f, MetallicSpecular = 0.55f, VertexColorUseAsAlbedo = true,
	});

	/// <summary>Coated glass: deep, with the coating's amber and violet in it.</summary>
	public static StandardMaterial3D LensGlass => Mat("held_lens", () => new StandardMaterial3D
	{
		AlbedoColor = new Color(0.02f, 0.025f, 0.04f), Roughness = 0.04f, MetallicSpecular = 1f, Metallic = 0.2f,
		RimEnabled = true, Rim = 0.6f, RimTint = 0.2f,
		EmissionEnabled = true, Emission = new Color(0.18f, 0.08f, 0.22f), EmissionEnergyMultiplier = 0.08f,
	});

	/// <summary>The walkie's tough plastic: olive-black, a fine texture, rubbed shiny at the edges.</summary>
	public static StandardMaterial3D RadioPlastic => Mat("held_radio", () => new StandardMaterial3D
	{
		AlbedoTexture = ItemTextures.Make("held_radio", 64, 64, (x, y) =>
		{
			float t = 0.085f + 0.015f * F(x, y, 64, 64, 32, 32, 2, 971) - 0.02f * Mathf.Max(0f, F(x, y, 64, 64, 4, 4, 3, 973) - 0.6f) * 2f;
			return new Color(t * 0.95f, t, t * 0.9f);
		}),
		NormalEnabled = true,
		NormalTexture = NormalFrom("held_radio_n", 64, 64, (x, y) => F(x, y, 64, 64, 32, 32, 2, 971), 0.8f),
		Roughness = 0.58f, MetallicSpecular = 0.4f, VertexColorUseAsAlbedo = true,
	});

	/// <summary>Soft black rubber (the antenna, the push-to-talk bar, the knobs).</summary>
	public static StandardMaterial3D Rubber => Mat("held_rubber", () => new StandardMaterial3D
	{
		AlbedoColor = new Color(0.035f, 0.035f, 0.035f), Roughness = 0.82f, MetallicSpecular = 0.25f, VertexColorUseAsAlbedo = true,
	});

	/// <summary>A speaker grille: rows of slots.</summary>
	public static StandardMaterial3D Grille => Mat("held_grille", () => new StandardMaterial3D
	{
		AlbedoTexture = ItemTextures.Make("held_grille", 32, 64, (x, y) => { bool slot = (y % 8) < 4 && x > 2 && x < 29; float t = slot ? 0.01f : 0.09f; return new Color(t, t, t); }),
		NormalEnabled = true,
		NormalTexture = NormalFrom("held_grille_n", 32, 64, (x, y) => (y % 8) < 4 && x > 2 && x < 29 ? 0f : 1f, 1.2f),
		Roughness = 0.6f, MetallicSpecular = 0.35f,
	});

	/// <summary>Worn brass: tarnished brown in the hollows, bright where a thumb rubs.</summary>
	public static StandardMaterial3D Brass => Mat("held_brass", () => new StandardMaterial3D
	{
		AlbedoTexture = ItemTextures.Make("held_brass", 64, 64, (x, y) =>
		{
			float t = F(x, y, 64, 64, 4, 4, 3, 981);
			var c = new Color(0.62f, 0.46f, 0.2f).Lerp(new Color(0.3f, 0.21f, 0.1f), Mathf.SmoothStep(0.45f, 0.75f, t));
			return c * (0.9f + 0.1f * F(x, y, 64, 64, 32, 32, 2, 983));
		}),
		Metallic = 0.85f, Roughness = 0.38f, VertexColorUseAsAlbedo = true,
	});

	/// <summary>Stacked leather washers (the knife's handle): brown rings, oiled dark.</summary>
	public static StandardMaterial3D StackedLeather => Mat("held_leather_stack", () => new StandardMaterial3D
	{
		AlbedoTexture = ItemTextures.Make("held_leather_stack", 32, 128, (x, y) =>
		{
			float ring = (y % 8) == 0 ? 0.55f : 1f;
			float t = (0.75f + 0.25f * F(x, y, 32, 128, 8, 16, 3, 991)) * ring;
			return new Color(0.32f * t, 0.19f * t, 0.1f * t);
		}),
		NormalEnabled = true,
		NormalTexture = NormalFrom("held_leather_stack_n", 32, 128, (x, y) => (y % 8) == 0 ? 0f : 1f, 0.9f),
		Roughness = 0.7f, MetallicSpecular = 0.35f, VertexColorUseAsAlbedo = true,
	});

	/// <summary>Paper (a sticker, a label), yellowed.</summary>
	public static StandardMaterial3D Label => Mat("held_label", () => new StandardMaterial3D
	{
		AlbedoTexture = ItemTextures.Make("held_label", 32, 32, (x, y) =>
		{
			float t = 0.75f + 0.2f * F(x, y, 32, 32, 4, 4, 3, 997);
			bool line = (y == 10 || y == 11 || y == 18) && x > 4 && x < 26 + (y == 18 ? -8 : 0);
			return line ? new Color(0.2f, 0.18f, 0.16f) : new Color(0.78f * t, 0.72f * t, 0.56f * t);
		}),
		Roughness = 0.9f, MetallicSpecular = 0.2f,
	});

	// ───────────────────────────── the things ─────────────────────────────

	/// <summary>The SLR, built centred (its body's middle at the origin, the lens on -Z), then shifted so its right end
	/// sits in the fist (<see cref="SpecOf"/>).</summary>
	private static void Camera(MeshKit k)
	{
		const float W = 0.143f, H = 0.062f, D = 0.036f;   // the body below the top plate
		const float TopH = 0.016f;
		float bodyTop = H * 0.5f, plateTop = bodyTop + TopH;
		// the body: leatherette front and back between the chrome ends (the leatherette band a little proud)
		k.Mat(Leatherette); k.Color = Colors.White;
		RBox(k, Vector3.Zero, new Vector3(D, H, W - 0.006f), Vector3.Right, Vector3.Back, 8f, 0.003f, 28, 24f);
		k.Mat(Chrome); k.Color = new Color(0.9f, 0.9f, 0.9f);
		foreach (float s in new[] { -1f, 1f })   // (the chrome ends)
			RBox(k, new Vector3(s * (W * 0.5f - 0.0035f), 0f, 0f), new Vector3(D + 0.0008f, H, 0.007f), Vector3.Right, Vector3.Back, 8f, 0.002f, 24);
		// the bottom plate and the top plate (chrome), the top a touch deeper
		k.Mat(Chrome); k.Color = Colors.White;
		RBox(k, new Vector3(-0.005f, -H * 0.5f - 0.002f, 0f), new Vector3(W - 0.004f, 0.005f, D + 0.002f), Vector3.Back, Vector3.Right, 8f, 0.002f, 28);
		RBox(k, new Vector3(-0.002f, bodyTop + TopH * 0.5f, 0.002f), new Vector3(W - 0.002f, TopH, D + 0.006f), Vector3.Back, Vector3.Right, 8f, 0.003f, 28);
		// the prism's hump over the lens: a trapezoid in profile, chrome sides, a black face with the maker's plate
		float lensX = -0.004f;
		{
			var hump = new List<Vector3>(); var hh = new List<Vector2>();
			for (int i = 0; i <= 5; i++)
			{
				float t = i / 5f;
				float y = plateTop - 0.002f + t * 0.03f;
				hump.Add(new Vector3(lensX, y, 0.003f - t * 0.002f));
				hh.Add(new Vector2(Mathf.Lerp(0.03f, 0.014f, t * t), Mathf.Lerp(0.02f, 0.011f, t)));
			}
			k.Mat(Chrome); k.Color = new Color(0.85f, 0.85f, 0.85f);
			SLoft(k, hump, hh, 6f, Vector3.Right, Vector3.Forward, 24, false, true);
			k.Mat(BlackEnamel); k.Color = Colors.White;
			k.Box(new Vector3(lensX, plateTop + 0.012f, -0.0185f), new Vector3(0.036f, 0.012f, 0.002f), 20f, new Basis(Vector3.Right, -0.42f));
			// the hot shoe on its crown
			k.Mat(Chrome);
			k.Box(new Vector3(lensX, plateTop + 0.0295f, 0.003f), new Vector3(0.02f, 0.003f, 0.018f), 20f);
			// the eyepiece on the back
			k.Mat(Rubber);
			RBox(k, new Vector3(lensX, plateTop + 0.01f, D * 0.5f + 0.006f), new Vector3(0.024f, 0.02f, 0.008f), Vector3.Back, Vector3.Right, 4f, 0.002f, 20);
		}
		// the lens: the chrome bayonet ring, the black barrel, the knurled focus ring, the aperture ring, the filter rim and the glass
		var lc = new Vector3(lensX, -0.001f, -D * 0.5f);
		k.Mat(Chrome); k.Color = Colors.White;
		Turn(k, lc, Vector3.Forward, new[] { new Vector2(0.03f, -0.001f), new Vector2(0.03f, 0.006f), new Vector2(0.028f, 0.007f) }, 32, false, false);
		k.Mat(BlackEnamel);
		Turn(k, lc, Vector3.Forward, new[] { new Vector2(0.028f, 0.006f), new Vector2(0.0285f, 0.016f), new Vector2(0.027f, 0.017f), new Vector2(0.027f, 0.04f), new Vector2(0.029f, 0.041f), new Vector2(0.029f, 0.047f), new Vector2(0.0265f, 0.048f), new Vector2(0.024f, 0.048f) }, 32, false, false);
		k.Mat(Rubber);
		Ridges(k, lc + Vector3.Forward * 0.0285f, Vector3.Forward, 0.027f, 0.019f, 40, 0.0016f, 0.0016f);   // the focus ring's knurl
		k.Mat(Chrome); k.Color = new Color(0.8f, 0.8f, 0.8f);
		Turn(k, lc, Vector3.Forward, new[] { new Vector2(0.0291f, 0.0102f), new Vector2(0.0291f, 0.0108f) }, 32, false, false);   // (the aperture ring's thin chrome line)
		// the glass, set in, and the black inside of the barrel round it
		k.Mat(BlackEnamel);
		Turn(k, lc, Vector3.Forward, new[] { new Vector2(0.024f, 0.048f), new Vector2(0.021f, 0.044f), new Vector2(0.019f, 0.043f) }, 32, false, false);
		k.Mat(LensGlass); k.Color = Colors.White;
		Turn(k, lc, Vector3.Forward, new[] { new Vector2(0.019f, 0.043f), new Vector2(0.014f, 0.0455f), new Vector2(0.006f, 0.0466f), new Vector2(0.0f, 0.0469f) }, 32, false, false);
		// the right end of the top plate: the shutter speed dial, the shutter button, the advance lever
		float rx = 0.042f;
		k.Mat(BlackEnamel);
		Turn(k, new Vector3(rx, plateTop, 0.002f), Vector3.Up, new[] { new Vector2(0.012f, 0f), new Vector2(0.012f, 0.006f), new Vector2(0.0105f, 0.0072f), new Vector2(0f, 0.0072f) }, 24, false, false);
		k.Mat(Rubber);
		Ridges(k, new Vector3(rx, plateTop + 0.003f, 0.002f), Vector3.Up, 0.012f, 0.0055f, 30, 0.0008f, 0.0012f);
		k.Mat(Chrome);
		Turn(k, new Vector3(rx, plateTop + 0.0072f, 0.002f), Vector3.Up, new[] { new Vector2(0.004f, 0f), new Vector2(0.004f, 0.004f), new Vector2(0.0032f, 0.005f), new Vector2(0f, 0.005f) }, 16, false, false);
		// the advance lever: tucked in along the back of the plate, its plastic tip standing off the body's right end
		k.Mat(Chrome);
		k.Box(new Vector3(0.05f, plateTop + 0.0015f, 0.012f), new Vector3(0.04f, 0.0025f, 0.006f), 20f, new Basis(Vector3.Up, 0.18f));
		k.Mat(BlackEnamel);
		RBox(k, new Vector3(0.071f, plateTop + 0.002f, 0.016f), new Vector3(0.007f, 0.007f, 0.012f), Vector3.Up, Vector3.Right, 4f, 0.002f, 16);
		// the left end: the rewind knob and its folded crank
		k.Mat(Chrome);
		Turn(k, new Vector3(-0.05f, plateTop, 0.001f), Vector3.Up, new[] { new Vector2(0.011f, 0f), new Vector2(0.011f, 0.006f), new Vector2(0.0095f, 0.0075f), new Vector2(0f, 0.0075f) }, 24, false, false);
		k.Box(new Vector3(-0.05f, plateTop + 0.0085f, 0.006f), new Vector3(0.006f, 0.0018f, 0.016f), 20f);
		// the strap lugs, and a stub of the tan leather strap from each
		foreach (float s in new[] { -1f, 1f })
		{
			k.Mat(Chrome);
			k.Box(new Vector3(s * (W * 0.5f + 0.002f), bodyTop + 0.004f, 0f), new Vector3(0.004f, 0.008f, 0.01f), 20f);
			k.Mat(ItemTextures.StrapMat); k.Color = new Color(0.55f, 0.38f, 0.22f);
			k.Box(new Vector3(s * (W * 0.5f + 0.008f), bodyTop - 0.006f, 0.003f), new Vector3(0.0025f, 0.026f, 0.014f), 20f, new Basis(Vector3.Back, s * 0.35f));
			k.Color = Colors.White;
		}
		// the self-timer lever on the front, beside the lens, and the frame counter's window
		k.Mat(BlackEnamel);
		k.Box(new Vector3(0.038f, 0.012f, -D * 0.5f - 0.002f), new Vector3(0.006f, 0.014f, 0.004f), 20f);
		k.Mat(LensGlass);
		k.Box(new Vector3(0.036f, plateTop + 0.0005f, -0.008f), new Vector3(0.006f, 0.0012f, 0.004f), 20f);
	}

	/// <summary>The camp axe: the handle up +Y from below the fist to the head, the bit toward -Z.</summary>
	private static void Axe(MeshKit k)
	{
		// the handle: oval, its long axis along the bit, a slight belly, a knob at the butt
		k.Mat(Hickory); k.Color = Colors.White;
		var cs = new List<Vector3>(); var hs = new List<Vector2>();
		void H(float y, float hx, float hz, float z = 0f) { cs.Add(new Vector3(0, y, z)); hs.Add(new Vector2(hx, hz)); }
		H(-0.115f, 0.009f, 0.013f, 0.004f);
		H(-0.112f, 0.0135f, 0.02f, 0.004f);
		H(-0.1f, 0.0155f, 0.024f, 0.003f);      // the knob
		H(-0.082f, 0.0135f, 0.019f, 0.001f);
		H(-0.05f, 0.0128f, 0.0175f);
		H(0.05f, 0.0122f, 0.0168f, -0.002f);
		H(0.2f, 0.0112f, 0.0158f, -0.004f);     // (a gentle sweep toward the bit)
		H(0.33f, 0.0106f, 0.0162f, -0.003f);
		H(0.40f, 0.0108f, 0.0175f, -0.001f);
		H(0.43f, 0.0112f, 0.0188f, 0f);
		H(0.505f, 0.0108f, 0.0182f, 0f);
		SLoft(k, cs, hs, 2.4f, Vector3.Right, Vector3.Back, 18, true, true, 4f);
		// the wedge's kerf, dark across the handle's top
		k.Mat(BlackEnamel);
		k.Box(new Vector3(0, 0.5056f, 0), new Vector3(0.0035f, 0.0012f, 0.03f), 20f);
		// the head: sections from the poll (+Z) to the bit (-Z), each a rounded rectangle across X (thickness) and Y
		const float hy = 0.468f;
		var hc = new List<Vector3>(); var hh = new List<Vector2>();
		const float g = 1.15f;   // (a touch over a 26" axe's own head: at arm's length it read small)
		void S(float z, float t, float h, float dy = 0f) { hc.Add(new Vector3(0, hy + dy * g, z * g)); hh.Add(new Vector2(t * g, h * g)); }
		S(0.05f, 0.0125f, 0.028f);
		S(0.047f, 0.0148f, 0.032f);
		S(0.03f, 0.016f, 0.036f);
		S(0.0f, 0.0168f, 0.0375f);
		S(-0.025f, 0.014f, 0.037f, -0.001f);
		S(-0.05f, 0.0085f, 0.041f, -0.004f);
		S(-0.075f, 0.0048f, 0.05f, -0.008f);
		S(-0.098f, 0.0026f, 0.058f, -0.011f);
		k.Mat(Forged); k.Color = Colors.White;
		SLoft(k, hc, hh, 5f, Vector3.Right, Vector3.Up, 24, true, false, 10f);
		// the bevel: honed bright, out to the edge (a little convex)
		var bc = new List<Vector3> { hc[^1], new Vector3(0, hy - 0.0115f * g, -0.106f * g), new Vector3(0, hy - 0.012f * g, -0.1115f * g) };
		var bh = new List<Vector2> { hh[^1], new Vector2(0.0011f * g, 0.0598f * g), new Vector2(0.00025f * g, 0.0575f * g) };
		k.Mat(Honed); k.Color = Colors.White;
		SLoft(k, bc, bh, 3.2f, Vector3.Right, Vector3.Up, 24, false, true, 10f);
	}

	/// <summary>The claw hammer: the handle up +Y, the face toward -Z, the claw curling back toward +Z.</summary>
	private static void Hammer(MeshKit k)
	{
		k.Mat(Hickory); k.Color = new Color(0.95f, 0.92f, 0.86f);
		var cs = new List<Vector3>(); var hs = new List<Vector2>();
		void H(float y, float hx, float hz, float z = 0f) { cs.Add(new Vector3(0, y, z)); hs.Add(new Vector2(hx, hz)); }
		H(-0.105f, 0.008f, 0.011f, 0.003f);
		H(-0.102f, 0.0125f, 0.0175f, 0.003f);
		H(-0.09f, 0.0142f, 0.0205f, 0.002f);    // the flared butt
		H(-0.065f, 0.0128f, 0.0172f, 0.001f);
		H(0.02f, 0.0118f, 0.0158f);
		H(0.12f, 0.0104f, 0.0138f, -0.001f);    // the waist under the head
		H(0.175f, 0.0108f, 0.0148f);
		H(0.2f, 0.0112f, 0.0158f);
		SLoft(k, cs, hs, 2.6f, Vector3.Right, Vector3.Back, 18, true, false, 4f);
		const float hy = 0.214f;
		k.Mat(Forged); k.Color = new Color(0.9f, 0.9f, 0.92f);
		// the eye's cheeks round the handle's top
		RBox(k, new Vector3(0, hy, 0.002f), new Vector3(0.0255f, 0.034f, 0.04f), Vector3.Back, Vector3.Right, 5f, 0.003f, 24);
		// the neck forward to the round face, its chamfer, and the honed face
		Turn(k, new Vector3(0, hy, -0.018f), Vector3.Forward, new[] { new Vector2(0.0125f, 0f), new Vector2(0.0118f, 0.015f), new Vector2(0.0128f, 0.03f), new Vector2(0.0158f, 0.043f), new Vector2(0.0158f, 0.05f) }, 24, false, false);
		k.Mat(Honed);
		Turn(k, new Vector3(0, hy, -0.068f), Vector3.Forward, new[] { new Vector2(0.0158f, 0f), new Vector2(0.0146f, 0.0025f), new Vector2(0.0128f, 0.0034f), new Vector2(0f, 0.0036f) }, 24, false, false);
		// the claw: two prongs curling back and down, a V between them
		k.Mat(Forged);
		foreach (float s in new[] { -1f, 1f })
		{
			var pc = new List<Vector3>(); var ph = new List<Vector2>();
			for (int i = 0; i <= 8; i++)
			{
				float t = i / 8f;
				float a = t * 1.05f;
				pc.Add(new Vector3(s * Mathf.Lerp(0.0062f, 0.0078f, t), hy + 0.006f + Mathf.Sin(a) * 0.004f - t * t * 0.036f, 0.02f + Mathf.Sin(a * 1.2f) * 0.052f));
				ph.Add(new Vector2(Mathf.Lerp(0.0062f, 0.0045f, t), Mathf.Lerp(0.0145f, 0.0022f, t)));
			}
			SLoft(k, pc, ph, 3f, Vector3.Right, Vector3.Up, 16, false, true, 10f);
		}
	}

	/// <summary>The hunting knife: the handle up +Y in the fist, the brass guard over it, the blade on up, its spine +Z and
	/// its edge -Z.</summary>
	private static void Knife(MeshKit k)
	{
		// the handle: stacked leather between an aluminium butt cap and the guard
		k.Mat(StackedLeather); k.Color = Colors.White;
		var cs = new List<Vector3>(); var hs = new List<Vector2>();
		void H(float y, float hx, float hz) { cs.Add(new Vector3(0, y, 0)); hs.Add(new Vector2(hx, hz)); }
		H(-0.055f, 0.0115f, 0.0145f); H(-0.03f, 0.0122f, 0.0158f); H(0.0f, 0.0118f, 0.0152f); H(0.035f, 0.0112f, 0.0146f); H(0.058f, 0.0108f, 0.0138f);
		SLoft(k, cs, hs, 2.4f, Vector3.Right, Vector3.Back, 18, false, false, 4f);
		k.Mat(Chrome); k.Color = new Color(0.75f, 0.75f, 0.75f);
		var bc = new List<Vector3> { new(0, -0.069f, 0), new(0, -0.066f, 0), new(0, -0.055f, 0) };
		var bh = new List<Vector2> { new(0.006f, 0.008f), new(0.011f, 0.014f), new(0.0118f, 0.0148f) };
		SLoft(k, bc, bh, 2.6f, Vector3.Right, Vector3.Back, 18, true, false, 10f);
		k.Mat(Brass); k.Color = Colors.White;
		RBox(k, new Vector3(0, 0.0625f, -0.001f), new Vector3(0.017f, 0.009f, 0.042f), Vector3.Back, Vector3.Right, 4f, 0.002f, 20);
		// the blade: a drop point, ground from a 4 mm spine to the edge, the tip dropping to the middle
		k.Mat(Forged); k.Color = new Color(1.15f, 1.15f, 1.18f);
		const int n = 12;
		var spine = new Vector3[n + 1];
		var edge = new Vector3[n + 1];
		var grind = new Vector3[n + 1];
		for (int i = 0; i <= n; i++)
		{
			float t = i / (float)n;
			float y = 0.067f + t * 0.118f;
			float spineZ = t < 0.72f ? 0.0105f : Mathf.Lerp(0.0105f, -0.001f, Mathf.Pow((t - 0.72f) / 0.28f, 1.3f));
			float edgeZ = t < 0.65f ? -0.0145f + t * 0.004f : Mathf.Lerp(-0.012f, -0.001f, Mathf.Pow((t - 0.65f) / 0.35f, 0.7f));
			if (i == n) { spineZ = edgeZ = -0.001f; }
			spine[i] = new Vector3(0, y, spineZ);
			edge[i] = new Vector3(0, y, edgeZ);
			grind[i] = new Vector3(0, y, Mathf.Lerp(edgeZ, spineZ, 0.55f));
		}
		for (int side = -1; side <= 1; side += 2)
		{
			var nx = new Vector3(side, 0, 0);
			for (int i = 0; i < n; i++)
			{
				float th0 = 0.0021f * (1f - Mathf.Pow(i / (float)n, 2f)), th1 = 0.0021f * (1f - Mathf.Pow((i + 1) / (float)n, 2f));
				Vector3 s0 = spine[i] + nx * th0, s1 = spine[i + 1] + nx * th1;
				Vector3 g0 = grind[i] + nx * th0, g1 = grind[i + 1] + nx * th1;
				// the flat, from the spine to the grind line
				k.Mat(Forged);
				k.Quad(s0, s1, g1, g0, nx, new Vector2(0, i / (float)n), new Vector2(0, (i + 1) / (float)n), new Vector2(0.5f, (i + 1) / (float)n), new Vector2(0.5f, i / (float)n));
				// the grind, down to the edge (bright, honed)
				k.Mat(Honed);
				var gn = (nx * 0.92f + Vector3.Forward * 0.4f).Normalized();
				k.Quad(g0, g1, edge[i + 1], edge[i], gn, new Vector2(0.5f, i / (float)n), new Vector2(0.5f, (i + 1) / (float)n), new Vector2(1, (i + 1) / (float)n), new Vector2(1, i / (float)n));
			}
		}
		// the spine's own flat
		k.Mat(Forged);
		for (int i = 0; i < n; i++)
		{
			float th0 = 0.0021f * (1f - Mathf.Pow(i / (float)n, 2f)), th1 = 0.0021f * (1f - Mathf.Pow((i + 1) / (float)n, 2f));
			var up = (spine[i + 1] - spine[i]).Cross(Vector3.Right).Normalized();
			if (up.Z < 0) up = -up;
			k.Quad(spine[i] + Vector3.Left * th0, spine[i] + Vector3.Right * th0, spine[i + 1] + Vector3.Right * th1, spine[i + 1] + Vector3.Left * th1, up);
		}
		// the ricasso's base, a block into the guard
		k.Box(new Vector3(0, 0.069f, -0.002f), new Vector3(0.0042f, 0.006f, 0.024f), 20f);
	}

	/// <summary>The wrecking bar: hexagonal stock up +Y through the fist, the gooseneck curling forward (-Z) and over, its
	/// split claw; the chisel end below, bent a little forward.</summary>
	private static void Crowbar(MeshKit k)
	{
		k.Mat(RustIron); k.Color = Colors.White;
		var rings = new List<ItemMeshes.Ring>
		{
			// the chisel end: flattened across X, bent a touch toward -Z
			new(new Vector3(0, -0.215f, -0.03f), 0.013f, 0.0028f),
			new(new Vector3(0, -0.2f, -0.022f), 0.0118f, 0.0055f),
			new(new Vector3(0, -0.175f, -0.01f), 0.0102f, 0.0092f),
			new(new Vector3(0, -0.14f, -0.002f), 0.0098f, 0.0098f),
			new(new Vector3(0, 0.0f, 0f), 0.0098f, 0.0098f),
			new(new Vector3(0, 0.3f, 0f), 0.0098f, 0.0098f),
		};
		// the gooseneck: round the bend forward and down, flattening toward the claw
		for (int i = 1; i <= 8; i++)
		{
			float a = i / 8f * Mathf.Pi * 0.95f;
			float flat = Mathf.SmoothStep(0.5f, 1f, i / 8f);
			rings.Add(new(new Vector3(0, 0.3f + Mathf.Sin(a) * 0.045f, -(1f - Mathf.Cos(a)) * 0.045f), Mathf.Lerp(0.0098f, 0.012f, flat), Mathf.Lerp(0.0098f, 0.0058f, flat)));
		}
		var last = rings[^1].C;
		rings.Add(new(last + new Vector3(0, -0.016f, -0.002f), 0.0124f, 0.004f));
		ItemMeshes.Loft(k, rings, 6, true, false, Vector3.Right, 6f);
		// where it's held: a wrap of old black friction tape, fat enough for the fist to close round (the bar alone is 2 cm)
		k.Mat(Rubber); k.Color = new Color(1.3f, 1.25f, 1.2f);
		for (int i = 0; i < 9; i++)
		{
			float y0 = -0.072f + i * 0.016f;
			Turn(k, Vector3.Zero, Vector3.Up, new[] { new Vector2(0.0098f, y0 - 0.001f), new Vector2(0.0142f, y0 + 0.001f), new Vector2(0.0146f, y0 + 0.009f), new Vector2(0.0141f, y0 + 0.0165f), new Vector2(0.0098f, y0 + 0.0175f) }, 12, false, false, 20f);
		}
		k.Mat(RustIron); k.Color = Colors.White;
		// the claw's two prongs either side of the nail slot
		foreach (float s in new[] { -1f, 1f })
		{
			var a = last + new Vector3(s * 0.0068f, -0.016f, -0.002f);
			k.Cylinder(a, a + new Vector3(s * 0.0006f, -0.015f, 0.004f), 0.0052f, 0.0016f, 6, true, 10f);
		}
	}

	/// <summary>The flip-top lighter, upright: its case in the fingers, the lid above.</summary>
	private static void Lighter(MeshKit k)
	{
		k.Mat(Brass); k.Color = Colors.White;
		RBox(k, new Vector3(0, 0.0f, 0), new Vector3(0.038f, 0.013f, 0.04f), Vector3.Up, Vector3.Right, 7f, 0.0025f, 28);
		k.Color = new Color(0.92f, 0.9f, 0.86f);
		RBox(k, new Vector3(0, 0.0295f, 0), new Vector3(0.038f, 0.013f, 0.0168f), Vector3.Up, Vector3.Right, 7f, 0.0025f, 28);
		// the seam between them, and the hinge's barrel at the right
		k.Mat(BlackEnamel);
		k.Box(new Vector3(0, 0.0206f, 0), new Vector3(0.0384f, 0.0009f, 0.0134f), 20f);
		k.Mat(Brass);
		k.Cylinder(new Vector3(0.0195f, 0.012f, 0.0f), new Vector3(0.0195f, 0.03f, 0.0f), 0.0024f, 0.0024f, 10, true, 20f);
	}

	/// <summary>The walkie-talkie: the case up +Y, its speaker toward +Z (the eye), the antenna standing off the top.</summary>
	private static void Walkie(MeshKit k, Node3D root)
	{
		const float W = 0.06f, D = 0.034f, H = 0.152f;
		float y0 = -0.06f, y1 = y0 + H;
		// the battery pack below and the case above it, a seam between
		k.Mat(RadioPlastic); k.Color = new Color(0.82f, 0.86f, 0.8f);
		RBox(k, new Vector3(0, y0 + 0.024f, -0.001f), new Vector3(W - 0.002f, D - 0.002f, 0.048f), Vector3.Up, Vector3.Right, 5f, 0.004f, 28);
		k.Color = Colors.White;
		RBox(k, new Vector3(0, y0 + 0.049f + (H - 0.049f) * 0.5f, 0), new Vector3(W, D, H - 0.049f), Vector3.Up, Vector3.Right, 5f, 0.005f, 28);
		// the speaker grille on the face, set into a raised rim
		k.Mat(Grille); k.Color = Colors.White;
		float gy = y0 + 0.085f;
		k.Quad(new Vector3(-0.022f, gy - 0.028f, D * 0.5f + 0.0004f), new Vector3(0.022f, gy - 0.028f, D * 0.5f + 0.0004f), new Vector3(0.022f, gy + 0.028f, D * 0.5f + 0.0004f), new Vector3(-0.022f, gy + 0.028f, D * 0.5f + 0.0004f), Vector3.Back,
			new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
		k.Mat(RadioPlastic); k.Color = new Color(0.7f, 0.72f, 0.68f);
		foreach (float s in new[] { -1f, 1f })
		{
			k.Box(new Vector3(s * 0.024f, gy, D * 0.5f + 0.001f), new Vector3(0.003f, 0.062f, 0.002f), 20f);
			k.Box(new Vector3(0, gy + s * 0.0305f, D * 0.5f + 0.001f), new Vector3(0.051f, 0.003f, 0.002f), 20f);
		}
		k.Color = Colors.White;
		// the channel sticker under the grille, its corner lifting
		k.Mat(Label);
		k.Box(new Vector3(0.002f, y0 + 0.041f, D * 0.5f + 0.0008f), new Vector3(0.036f, 0.016f, 0.0006f), 30f, new Basis(Vector3.Back, 0.03f));
		// the top: the antenna's collar and its rubber antenna (ribbed, tapering), the volume and channel knobs, the LED
		k.Mat(Rubber);
		Turn(k, new Vector3(0.017f, y1, -0.002f), Vector3.Up, new[] { new Vector2(0.0085f, 0f), new Vector2(0.0085f, 0.012f), new Vector2(0.0072f, 0.016f), new Vector2(0.006f, 0.05f), new Vector2(0.0048f, 0.1f), new Vector2(0.0044f, 0.118f), new Vector2(0.0032f, 0.122f), new Vector2(0f, 0.123f) }, 14, false, false);
		for (int i = 0; i < 9; i++)
			Turn(k, new Vector3(0.017f, y1 + 0.02f + i * 0.0105f, -0.002f), Vector3.Up, new[] { new Vector2(0.0063f - i * 0.00015f, 0f), new Vector2(0.0069f - i * 0.00015f, 0.002f), new Vector2(0.0063f - i * 0.00015f, 0.004f) }, 14, false, false);
		foreach (var (x, r, h) in new[] { (-0.016f, 0.0075f, 0.013f), (-0.0005f, 0.0058f, 0.009f) })
		{
			Turn(k, new Vector3(x, y1, -0.002f), Vector3.Up, new[] { new Vector2(r, 0f), new Vector2(r, h), new Vector2(r - 0.0015f, h + 0.0012f), new Vector2(0f, h + 0.0012f) }, 18, false, false);
			Ridges(k, new Vector3(x, y1 + h * 0.5f, -0.002f), Vector3.Up, r, h * 0.85f, 16, 0.0009f, 0.0011f);
		}
		var led = new StandardMaterial3D { ResourceName = "held_walkie_led", AlbedoColor = new Color(0.25f, 0.03f, 0.02f), Roughness = 0.2f, EmissionEnabled = true, Emission = new Color(1f, 0.08f, 0.03f), EmissionEnergyMultiplier = 0f };
		k.Mat(led); k.Color = Colors.White;
		k.Box(new Vector3(0.006f, y1 + 0.0012f, 0.01f), new Vector3(0.004f, 0.003f, 0.004f), 20f);
		root.SetMeta("led", led);
		// the push-to-talk bar on the left side (the thumb's), and the belt clip on the back
		k.Mat(Rubber);
		RBox(k, new Vector3(-W * 0.5f - 0.002f, y0 + 0.095f, 0.0f), new Vector3(0.006f, 0.018f, 0.04f), Vector3.Up, Vector3.Right, 4f, 0.002f, 18);
		k.Mat(RadioPlastic); k.Color = new Color(0.75f, 0.78f, 0.72f);
		RBox(k, new Vector3(0, y0 + 0.085f, -D * 0.5f - 0.0035f), new Vector3(0.03f, 0.004f, 0.075f), Vector3.Up, Vector3.Right, 5f, 0.0015f, 20);
		k.Color = Colors.White;
	}

	/// <summary>The newel post's cap, by its neck (the narrowest of it, just under the ball, through the fist).</summary>
	private static void Cap(MeshKit k)
	{
		k.Xf = new Transform3D(Basis.Identity, new Vector3(0, -0.126f, 0));
		StaircaseBuilder.BuildNewelCap(k);
		k.Xf = Transform3D.Identity;
	}

	/// <summary>The walkie's LED: dark while the radio's dead, a small red light once it's alive (from Act 10's end).</summary>
	public static void SetLed(Node3D model, bool on)
	{
		if (model?.GetMeta("led", default).AsGodotObject() is StandardMaterial3D led)
			led.EmissionEnergyMultiplier = on ? 2.4f : 0f;
	}
}
