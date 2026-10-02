using Godot;

namespace ProjectDS.World;

/// <summary>
/// Footprints in the snow (the owner, 2026-10-01: the player leaving prints behind, and the wendigo's): two pools of
/// small cards laid on the snow's surface, one of boot prints, one of the wendigo's (a long narrow pad, three long
/// toes, the claws' slots in front). The oldest are reused as new ones are laid (the newest few hundred stay), and each
/// card fades in its last stretch so nothing pops.
///
/// The prints are drawn: each texture is an alpha mask (the print's shape) with the shading of a hollow pressed into
/// snow (its far wall lit, its near wall and floor in shade, a lip of pushed-up snow round it), so it reads in the
/// lantern's light without a normal map. Cold blue-grey, never black.
/// </summary>
public partial class SnowPrints : Node3D
{
	public const int BootCount = 480, ClawCount = 240;
	private MultiMesh _boots, _claws;
	private int _nextBoot, _nextClaw, _bootsLaid, _clawsLaid;
	/// <summary>For tests: how many have been laid in all.</summary>
	public int BootsLaid => _bootsLaid;
	public int ClawsLaid => _clawsLaid;
	/// <summary>Where the newest boot print is (world; tests).</summary>
	public Vector3 LastBoot { get; private set; }

	public override void _Ready()
	{
		_boots = Pool("Boots", BootCount, new Vector2(0.15f, 0.33f), BootTexture());
		_claws = Pool("Claws", ClawCount, new Vector2(0.32f, 0.66f), ClawTexture());
	}

	private MultiMesh Pool(string name, int count, Vector2 size, Texture2D tex)
	{
		var mat = new StandardMaterial3D
		{
			// multiplied over the snow (it darkens it into a hollow: the snow's own grain and light show through it; a lit
			// card read as a sticker on it)
			ResourceName = "snowprint_" + name.ToLower(), AlbedoTexture = tex, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Mul, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Back,
			RenderPriority = 1,   // (over the snow, which it lies a hair above)
		};
		mat.SetMeta("detail_kind", -1);
		var quad = new QuadMesh { Size = size, Orientation = PlaneMesh.OrientationEnum.Y, Material = mat };
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = quad, InstanceCount = count, VisibleInstanceCount = 0 };
		AddChild(new MultiMeshInstance3D { Name = name, Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });   // (no visibility range: it measures to the middle of the whole trail's bounds, which can be far off)
		return mm;
	}

	/// <summary>Lays a print at <paramref name="ground"/> (world, on the snow's surface) with the snow's
	/// <paramref name="normal"/> there, its toe toward <paramref name="facing"/>; <paramref name="left"/> mirrors it;
	/// <paramref name="depth"/> 0..1 how deep (darker, a touch bigger: deep snow, or a leap's push-off).</summary>
	public void Lay(Vector3 ground, Vector3 normal, Vector3 facing, bool left, bool wendigo, float depth = 0.5f)
	{
		var mm = wendigo ? _claws : _boots;
		int count = wendigo ? ClawCount : BootCount;
		ref int next = ref wendigo ? ref _nextClaw : ref _nextBoot;
		var up = normal.Normalized();
		var fwd = (facing - up * facing.Dot(up)).Normalized();
		if (fwd.LengthSquared() < 0.5f) fwd = Vector3.Forward;
		var side = fwd.Cross(up).Normalized();   // (the card's +X)
		float s = 1f + depth * 0.15f;
		// the card's local: X across, Y up (its face), Z along: its top (the toe, v = 0) toward fwd
		// (not mirrored for the other foot: mirrored, its back face drew, lit wrongly; a slight toe-out instead)
		var turn = new Basis(up, left ? 0.08f : -0.08f);
		var basis = turn * new Basis(side * s, up, -fwd * s);
		// (a few centimetres up: the road's ribbon is sampled every 1.5 m and its ruts dip between the samples)
		var local = GlobalTransform.AffineInverse() * new Transform3D(basis, ground + up * 0.035f);
		if (!wendigo) LastBoot = ground;
		mm.SetInstanceTransform(next, local);
		mm.SetInstanceColor(next, new Color(1f, 1f, 1f).Lerp(new Color(0.86f, 0.88f, 0.92f), depth) with { A = 1f });
		next = (next + 1) % count;
		if (wendigo) _clawsLaid++; else _bootsLaid++;
		mm.VisibleInstanceCount = Mathf.Min(wendigo ? _clawsLaid : _bootsLaid, count);
		// the oldest few fade before they're reused
		for (int k = 0; k < 24; k++)
		{
			int i = (next + k) % count;
			if (i >= mm.VisibleInstanceCount) break;
			// (fading: toward white, which multiplies to nothing)
			var c = mm.GetInstanceColor(i);
			mm.SetInstanceColor(i, c.Lerp(Colors.White, 1f - k / 24f));
		}
	}

	// ------------------------------------------------------------------ the prints, drawn

	/// <summary>Draws a print from its shape (0..1 depth across the card): blurred soft, a hollow's shading laid over it
	/// (the floor a cold shade a little darker than the snow, the wall toward the toe catching light, a faint lip of
	/// pushed-up snow round it), the alpha following the depth so its edge feathers into the snow.</summary>
	private static ImageTexture Draw(int w, int h, System.Func<float, float, float> shape)
	{
		var img = Image.CreateEmpty(w, h, true, Image.Format.Rgba8);
		var m = new float[w, h];
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
				m[x, y] = Mathf.Clamp(shape((x + 0.5f) / w * 2f - 1f, (y + 0.5f) / h * 2f - 1f), 0f, 1f);
		// soften (two box blurs)
		for (int pass = 0; pass < 2; pass++)
		{
			var b = new float[w, h];
			for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
				{
					float sum = 0f; int n = 0;
					for (int dy = -2; dy <= 2; dy++)
						for (int dx = -2; dx <= 2; dx++)
						{
							int xx = x + dx, yy = y + dy;
							if (xx < 0 || yy < 0 || xx >= w || yy >= h) { n++; continue; }
							sum += m[xx, yy]; n++;
						}
					b[x, y] = sum / n;
				}
			m = b;
		}
		// what the snow is multiplied by: 1 outside, a cold shade in the hollow (deepest where it's pressed deepest), the
		// wall facing the toe a little less dark (it catches the light), a faint brightening for the lip round it
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float v = m[x, y];
				float gy = m[x, Mathf.Clamp(y + 2, 0, h - 1)] - m[x, Mathf.Clamp(y - 2, 0, h - 1)];
				float dark = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(v * 1.1f, 0f, 1f)) * (1f - Mathf.Clamp(-gy * 6f, 0f, 0.6f));
				var c = new Color(1f, 1f, 1f).Lerp(new Color(0.55f, 0.59f, 0.68f), dark);
				img.SetPixel(x, y, c with { A = 1f });
			}
		img.GenerateMipmaps();
		return ImageTexture.CreateFromImage(img);
	}

	private static ImageTexture _boot, _claw;

	/// <summary>A boot's print: one sole, broad at the ball and narrowing at the waist to the heel, pressed deepest at
	/// the ball and the heel, the tread's bars faint across it. v -1 is the toe.</summary>
	public static ImageTexture BootTexture() => _boot ??= Draw(64, 144, (u, v) =>
	{
		float halfW = v < 0.2f ? Mathf.Lerp(0.88f, 0.62f, Mathf.Clamp((v + 0.2f) / 0.6f, 0f, 1f)) : Mathf.Lerp(0.6f, 0.7f, Mathf.Clamp((v - 0.2f) / 0.5f, 0f, 1f));
		float round = (v < -0.6f) ? Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((v + 0.6f) / 0.38f, 2f))) : (v > 0.7f ? Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((v - 0.7f) / 0.26f, 2f))) : 1f);
		float inside = 1f - Mathf.SmoothStep(0.8f, 1f, Mathf.Abs(u) / Mathf.Max(halfW * round, 0.01f));
		if (v < -0.98f || v > 0.96f) inside = 0f;
		float press = 0.75f + 0.25f * Mathf.Max(Mathf.Exp(-Mathf.Pow((v + 0.45f) / 0.3f, 2f)), Mathf.Exp(-Mathf.Pow((v - 0.7f) / 0.2f, 2f)));
		float tread = 0.88f + 0.12f * Mathf.Abs(Mathf.Sin(v * 22f));
		return inside * press * tread;
	});

	/// <summary>The wendigo's print: a long narrow pad (a heel dragged long behind it), three thick toes splayed forward
	/// from it and tapering, each ending in a short deep gouge where the claw went in. v -1 is the toe.</summary>
	public static ImageTexture ClawTexture() => _claw ??= Draw(96, 208, (u, v) =>
	{
		float pad = 1f - Mathf.SmoothStep(0.75f, 1f, Mathf.Pow(u / 0.34f, 2f) + Mathf.Pow((v - 0.38f) / 0.55f, 2f));
		float best = pad * 0.9f;
		foreach (float a in new[] { -0.42f, 0f, 0.42f })
		{
			// a toe: from the pad's front (v 0) out along its angle to its tip at v -0.72, thick and tapering
			float t = Mathf.Clamp((-v + 0.05f) / 0.77f, 0f, 1f);
			float cx = Mathf.Sin(a) * t * 0.75f;
			float width = Mathf.Lerp(0.2f, 0.09f, t);
			float toe = (v < 0.05f && v > -0.74f) ? 1f - Mathf.SmoothStep(0.7f, 1f, Mathf.Abs(u - cx) / width) : 0f;
			// the claw's gouge beyond the tip: narrow, deep
			float gx = Mathf.Sin(a) * 0.62f;
			float g = (v < -0.74f && v > -0.96f) ? (1f - Mathf.SmoothStep(0.6f, 1f, Mathf.Abs(u - gx) / Mathf.Lerp(0.06f, 0.02f, (-v - 0.74f) / 0.22f))) : 0f;
			best = Mathf.Max(best, Mathf.Max(toe * 0.95f, g));
		}
		return best;
	});
}
