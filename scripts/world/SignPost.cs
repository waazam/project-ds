using Godot;

namespace ProjectDS.World;

/// <summary>
/// Routed wooden trail sign, painted the park's chocolate brown, its routed letters and arrows filled white (the
/// owner's photo of a state park's sign, 2026-10-03). Directional: a thick square post with arrow boards stacked on
/// it. Low: a short two-post board under a header beam with cut tails, leaning a little, like the ones left at forks
/// and bridges.
/// Each entry in Boards is one board; end it with " &gt;" or " &lt;" to point
/// the board (and its routed arrow) right or left. Front faces +Z.
/// </summary>
[Tool]
[GlobalClass]
public partial class SignPost : Node3D
{
	public enum SignStyle { Directional, Low }

	[Export] public SignStyle Style = SignStyle.Directional;
	[Export] public string[] Boards = { "Blackfern Trail >", "Cabins >", "Ranger Station <" };
	/// <summary>Degrees the whole sign leans back / sideways (weathered, frost-heaved).</summary>
	[Export] public Vector2 LeanDegrees = new(0f, 0f);
	/// <summary>If &gt; 0, keeps procedural trees/rocks/foliage out of this radius.</summary>
	[Export] public float ClearRadius = 1.4f;
	[Export] public int Seed = 3;

	private MeshKit _k;
	private Node3D _gen;
	private StaticBody3D _body;

	public override void _Ready() => Build();

	public void Build()
	{
		var old = GetNodeOrNull("Generated");
		if (old != null) { RemoveChild(old); old.QueueFree(); }
		_gen = new Node3D { Name = "Generated" };
		AddChild(_gen);
		_gen.Rotation = new Vector3(Mathf.DegToRad(-LeanDegrees.X), 0, Mathf.DegToRad(-LeanDegrees.Y));
		_k = new MeshKit();
		_body = null;
		if (Style == SignStyle.Directional) Directional(); else Low();
		if (!_k.IsEmpty) _k.CommitTo(_gen, "Mesh");
		if (ClearRadius > 0f && !Engine.IsEditorHint())
			AddChild(new ClearZone { Name = "ClearZone", Radius = ClearRadius, ClearFoliage = true });
	}

	private static (string text, int dir) Parse(string s)
	{
		s = s.Trim();
		if (s.EndsWith(">")) return (s[..^1].TrimEnd(), 1);
		if (s.EndsWith("<")) return (s[..^1].TrimEnd(), -1);
		return (s, 0);
	}

	private int Longest()
	{
		int m = 1;
		if (Boards != null) foreach (var b in Boards) m = Mathf.Max(m, Parse(b).text.Length);
		return m;
	}

	private void Col(Vector3 center, Vector3 size)
	{
		if (Engine.IsEditorHint()) return;
		if (_body == null)
		{
			_body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
			_gen.AddChild(_body);
		}
		_body.AddChild(new CollisionShape3D { Position = center, Shape = new BoxShape3D { Size = size } });
	}

	private float Rand(int i, float lo, float hi)
	{
		uint h = (uint)(Seed * 7919 + i * 104729);
		h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
		return lo + (hi - lo) * ((h & 0xFFFF) / 65535f);
	}

	private void Directional()
	{
		const float postW = 0.17f, postH = 2.25f;
		const float len = 1.55f, h = 0.27f, t = 0.05f, pitch = 0.32f;
		// (2026-10-15) the post and boards modelled in Blender (park_props.py), when they're there; the lettering stays here
		bool model = PropModels.Has("sign_post_tall") && PropModels.Has("dir_board_r");
		if (model) PropModels.Place(_gen, "sign_post_tall", Transform3D.Identity);
		else
		{
			_k.Color = new Color(0.9f, 0.88f, 0.85f);
			_k.Mat(BuildingTextures.SignPostBrownMat);
			SignKit.FlatPost(_k, new Vector3(0, -0.4f, 0), postH + 0.4f, postW, 1.5f);
		}
		Col(new Vector3(0, postH * 0.5f, 0), new Vector3(postW + 0.02f, postH, postW + 0.02f));

		int n = Boards?.Length ?? 0;
		float top = postH - 0.2f;
		for (int i = 0; i < n; i++)
		{
			var (text, dir) = Parse(Boards[i]);
			float y = top - i * pitch;
			// the square end of each board is bolted over the post; the point sticks out
			float x = dir * (len * 0.5f - 0.2f);
			float z = postW * 0.5f + t * 0.5f + 0.003f;
			var b = Basis.FromEuler(new Vector3(0, 0, Mathf.DegToRad(Rand(i, -2.5f, 2.5f))));
			var c = new Vector3(x, y, z);
			Col(c, new Vector3(len, h, t + 0.02f));
			float face0 = t * 0.5f + 0.002f;
			float usable0 = len - 0.22f - 0.22f - 0.08f;
			if (model)
			{
				// the board (its routed arrow and bolt in the model), then only the lettering
				PropModels.Place(_gen, dir > 0 ? "dir_board_r" : dir < 0 ? "dir_board_l" : "dir_board_0", new Transform3D(b, c));
				float emM = Mathf.Min(0.135f, usable0 / Mathf.Max(4, Longest()) * 1.75f);
				float txM = dir == 0 ? 0f : -dir * (len * 0.5f - 0.06f - usable0 * 0.5f);
				SignKit.Text(_gen, text, c + b * new Vector3(txM, -emM * 0.04f, face0), b, emM, SignKit.Paint);
				continue;
			}
			float shade = Rand(i + 20, 0.8f, 1.05f);
			_k.Color = new Color(shade, shade * 0.98f, shade * 0.95f);
			_k.Mat(BuildingTextures.SignBrownMat);
			SignKit.ArrowBoard(_k, c, b, len, h, t, dir);
			// (2026-10-14) the weather on its face (over the square part: the film can't follow the point), and a split
			// running in from its square end on some
			{
				float tip = dir == 0 ? 0f : Mathf.Min(h * 0.55f, len * 0.3f);
				var faceC = c + b * new Vector3(0, 0, t * 0.5f);
				SignKit.Weather(_k, faceC + b * new Vector3(-dir * tip * 0.5f, 0, 0), b, len - tip - 0.01f, h - 0.01f, Seed * 31 + i);
				if (Rand(i + 40, 0f, 1f) > 0.35f)
					SignKit.Split(_k, faceC, b, len * Rand(i + 41, 0.25f, 0.55f), Rand(i + 42, -h * 0.3f, h * 0.3f), dir == 0 ? -len * 0.5f : -dir * len * 0.5f, dir == 0 ? 1 : dir);
			}
			// bolt heads on the post
			_k.Color = new Color(0.25f, 0.24f, 0.22f);
			_k.Mat(ProcTextures.MetalMat).Box(c + b * new Vector3(-x, 0, t * 0.5f + 0.005f), new Vector3(0.025f, 0.025f, 0.012f));

			// routed arrow near the point, lettering filling the rest
			_k.Color = Colors.White;
			float face = t * 0.5f + 0.002f;
			float arrowLen = 0.22f;
			float usable = len - 0.22f - arrowLen - 0.08f;  // minus the tip and the arrow
			float textCx = dir == 0 ? 0f : -dir * (len * 0.5f - 0.06f - usable * 0.5f);
			if (dir != 0)
			{
				float ax = dir * (len * 0.5f - 0.22f - arrowLen * 0.5f);
				_k.Mat(PropTextures.WhitePaintMat);
				SignKit.RoutedArrow(_k, c + b * new Vector3(ax, 0, face), b, arrowLen, h * 0.42f, dir);
			}
			float em = Mathf.Min(0.135f, usable / Mathf.Max(4, Longest()) * 1.75f);
			SignKit.Text(_gen, text, c + b * new Vector3(textCx, -em * 0.04f, face), b, em, SignKit.Paint);
		}
		_k.Color = Colors.White;
	}

	private void Low()
	{
		const float postW = 0.11f, postH = 0.95f;
		const float len = 1.3f, h = 0.28f, t = 0.05f;
		// (2026-10-15) the frame and boards modelled in Blender (park_props.py), when they're there
		bool model = PropModels.Has("sign_low_frame") && PropModels.Has("low_board_0");
		if (model) PropModels.Place(_gen, "sign_low_frame", Transform3D.Identity);
		_k.Color = new Color(0.88f, 0.86f, 0.83f);
		_k.Mat(BuildingTextures.SignPostBrownMat);
		foreach (float px in new[] { -0.48f, 0.48f })
		{
			if (!model) SignKit.FlatPost(_k, new Vector3(px, -0.35f, 0), postH + 0.35f, postW, 1.8f);
			Col(new Vector3(px, postH * 0.5f, 0), new Vector3(postW + 0.02f, postH, postW + 0.02f));
		}
		// the header across the posts' tops, its tails cut away underneath
		if (!model) SignKit.Header(_k, new Vector3(0, postH + 0.065f, 0), Basis.Identity, 0.96f + postW, 0.2f, 0.11f, postW + 0.03f, 1.8f);
		int n = Mathf.Max(1, Boards?.Length ?? 0);
		for (int i = 0; i < n; i++)
		{
			var (text, dir) = Boards != null && i < Boards.Length ? Parse(Boards[i]) : ("", 0);
			float y = postH - 0.2f - i * (h + 0.02f);
			var b = Basis.FromEuler(new Vector3(0, 0, Mathf.DegToRad(Rand(i, -3f, 3f))));
			var c = new Vector3(0, y, postW * 0.5f + t * 0.5f + 0.003f);
			Col(c, new Vector3(len, h, t + 0.02f));
			if (model)
			{
				// the board (its crack and routed arrow in the model), then only the lettering
				PropModels.Place(_gen, dir > 0 ? "low_board_r" : dir < 0 ? "low_board_l" : "low_board_0", new Transform3D(b, c));
				if (text.Length == 0) continue;
				float emM = Mathf.Min(0.15f, (len - 0.2f - 0.28f) / Mathf.Max(4, Longest()) * 1.8f);
				float txM = dir == 0 ? 0f : -dir * 0.28f * 0.5f;
				SignKit.Text(_gen, text, c + b * new Vector3(txM, -emM * 0.04f, t * 0.5f + 0.002f), b, emM, SignKit.Paint);
				continue;
			}
			_k.Color = new Color(0.95f, 0.93f, 0.9f);
			_k.Mat(BuildingTextures.SignBrownMat);
			SignKit.ArrowBoard(_k, c, b, len, h, t, 0);
			SignKit.Weather(_k, c + b * new Vector3(0, 0, t * 0.5f), b, len - 0.01f, h - 0.01f, Seed * 37 + i);   // (2026-10-14)
			// the long crack down one board
			_k.Color = new Color(0.02f, 0.02f, 0.02f);
			_k.Mat(ProcTextures.Flat("sign_crack", new Color(0.03f, 0.025f, 0.02f)))
				.Box(c + b * new Vector3(0.1f, -h * 0.33f, t * 0.5f + 0.001f), new Vector3(len * 0.7f, 0.006f, 0.002f), 1f,
					b * Basis.FromEuler(new Vector3(0, 0, 0.02f)));
			_k.Color = Colors.White;
			float face = t * 0.5f + 0.002f;
			if (text.Length == 0) continue;
			float arrowLen = 0.2f;
			float em = Mathf.Min(0.15f, (len - 0.2f - (arrowLen + 0.08f)) / Mathf.Max(4, Longest()) * 1.8f);
			if (dir != 0)
			{
				float ax = dir * (len * 0.5f - 0.1f - arrowLen * 0.5f);
				_k.Mat(PropTextures.WhitePaintMat);
				SignKit.RoutedArrow(_k, c + b * new Vector3(ax, 0, face), b, arrowLen, h * 0.34f, dir);
			}
			float tx = dir == 0 ? 0f : -dir * (arrowLen + 0.08f) * 0.5f;
			SignKit.Text(_gen, text, c + b * new Vector3(tx, -em * 0.04f, face), b, em, SignKit.Paint);
		}
	}
}
