using Godot;

namespace ProjectDS.UI;

/// <summary>
/// The view through the camera while it's raised (right mouse held with the camera
/// in hand): dimmed edges, the 3:2 frame's corner brackets, a small focus square
/// that turns eye-yellow when something is in focus, a film counter and quiet
/// exposure readouts. Drawn at the game's 640x360; purely presentational, so
/// CameraTool tells it what to show.
///
/// (2026-10-29, the playtest: focus as a held button, "the image racks from soft to sharp on the subject and the
/// viewfinder shows an aperture animation in the lens") The frame is as soft as the lens is (<see cref="FocusLevel"/>: a
/// blur of the picture behind it, inside the frame only), and while the focus button is held (<see cref="Racking"/>) an
/// iris shows in the lens round the focus square: its blades swing in, close down while the focus hunts, and open again
/// to the taking aperture as the picture comes sharp; let go and they fold back out of sight.
/// </summary>
public partial class CameraViewfinder : CanvasLayer
{
	/// <summary>0 = lowered, 1 = fully raised (eased by CameraTool). Raised, the low hint lines move up out of its readout.</summary>
	public float Raise { get => _raise; set { _raise = value; HintStyle.ViewfinderUp = value > 0.5f; } }
	private float _raise;
	public bool FocusLocked { get; set; }
	/// <summary>How sharp the lens is (0..1): a bar beside the focus square fills as it comes in, and the frame clears.</summary>
	public float FocusLevel { get; set; }
	/// <summary>The focus button is held: the iris shows and works.</summary>
	public bool Racking { get; set; }
	public float Zoom { get; set; } = 1f;
	public int FramesLeft { get; set; } = 36;
	/// <summary>CameraTool sets this for the one frame it reads back for the print: no brackets, no focus square, no readouts.</summary>
	public bool HideMarks { get; set; }

	/// <summary>The 3:2 frame rectangle in viewport pixels (valid whether or not the overlay is drawing).</summary>
	public Rect2 FrameRect => FrameFor(_draw?.Size ?? new Vector2(640, 360));

	private static Rect2 FrameFor(Vector2 size)
	{
		// The 3:2 frame, centred, as large as fits with a margin.
		float fh = size.Y * 0.78f, fw = fh * 1.5f;
		if (fw > size.X * 0.86f) { fw = size.X * 0.86f; fh = fw / 1.5f; }
		return new Rect2((size - new Vector2(fw, fh)) * 0.5f, new Vector2(fw, fh));
	}

	private static readonly Color Bone = new(0.81f, 0.80f, 0.75f);
	private static readonly Color Eye = new(0.90f, 0.76f, 0.35f);
	private Control _draw;
	private Font _mono;
	/// <summary>The lens's softness: a blur of what's behind the frame (first in the layer, so under the marks).</summary>
	private ColorRect _soft;
	private ShaderMaterial _softMat;
	/// <summary>The iris: 0 folded away .. 1 in view; and how far its blades are closed (0 wide open .. 1 shut).</summary>
	private float _iris, _close, _irisTurn;

	// a blur of the screen behind the frame, by mip level: a ring of taps round each pixel at a radius that grows with the
	// softness, so an out-of-focus picture smears into soft discs, not a bilinear haze
	private const string SoftShader = @"
shader_type canvas_item;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float amount = 0.0;
void fragment() {
	// (in the game's own pixels, of its 360 lines, whatever the window: the CRT setting draws at the window's size)
	vec2 px = vec2(SCREEN_PIXEL_SIZE.x / SCREEN_PIXEL_SIZE.y, 1.0) / 360.0;
	float lod = amount * (log2(1.0 / (SCREEN_PIXEL_SIZE.y * 360.0)) + 1.6);
	vec3 sum = textureLod(screen_tex, SCREEN_UV, lod).rgb;
	float r = amount * 3.5;
	for (int i = 0; i < 8; i++) {
		float a = float(i) * 0.785398;
		sum += textureLod(screen_tex, SCREEN_UV + vec2(cos(a), sin(a)) * px * r, lod).rgb;
	}
	COLOR = vec4(sum / 9.0, 1.0);
}";

	/// <summary>The viewfinder in play, if any (the letterbox bars step aside while it is raised).</summary>
	public static CameraViewfinder Current { get; private set; }

	public override void _ExitTree() { if (Current == this) Current = null; HintStyle.ViewfinderUp = false; }

	public override void _Ready()
	{
		Current = this;
		Layer = 25;
		_softMat = new ShaderMaterial { Shader = new Shader { Code = SoftShader } };
		_soft = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Material = _softMat, Visible = false };
		AddChild(_soft);
		_draw = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		_draw.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_draw.Draw += OnDraw;
		AddChild(_draw);
		_mono = new SystemFont { FontNames = new[] { "Consolas", "Courier New", "DejaVu Sans Mono", "monospace" } };
	}

	public override void _Process(double delta)
	{
		Visible = Raise > 0.01f;
		float dt = (float)delta;
		// the iris: in while the button's held; its blades close down as the focus hunts and open to the taking aperture
		// as it comes sharp (a lens stopping down to check its focus, then opening back up)
		_iris = Mathf.MoveToward(_iris, Racking && Visible ? 1f : 0f, dt / (Racking ? 0.12f : 0.2f));
		float closeTo = Racking ? Mathf.Lerp(0.78f, 0.32f, Mathf.SmoothStep(0.2f, 0.95f, FocusLevel)) : 0f;
		_close = Mathf.Lerp(_close, closeTo, 1f - Mathf.Exp(-9f * dt));
		if (Racking) _irisTurn += dt * (1f - FocusLevel) * 1.6f;
		if (!Visible) { _soft.Visible = false; return; }
		// the lens's softness over the frame (not in the frame the print is read from: the print is softened itself)
		var frame = FrameFor(_draw.Size);
		float soft = (1f - Mathf.Clamp(FocusLevel, 0f, 1f)) * Mathf.SmoothStep(0.3f, 1f, Raise);
		_soft.Visible = soft > 0.02f && !HideMarks;
		if (_soft.Visible)
		{
			_soft.Position = frame.Position;
			_soft.Size = frame.Size;
			_softMat.SetShaderParameter("amount", soft);
		}
		_draw.QueueRedraw();
	}

	private void OnDraw()
	{
		float a = Mathf.SmoothStep(0f, 1f, Raise);
		var size = _draw.Size;

		var frame = FrameFor(size);
		float fh = frame.Size.Y;

		// Dim everything outside the frame (the camera body around the eyepiece).
		var dim = new Color(0.03f, 0.035f, 0.045f, 0.72f * a);
		_draw.DrawRect(new Rect2(0, 0, size.X, frame.Position.Y), dim);
		_draw.DrawRect(new Rect2(0, frame.End.Y, size.X, size.Y - frame.End.Y), dim);
		_draw.DrawRect(new Rect2(0, frame.Position.Y, frame.Position.X, fh), dim);
		_draw.DrawRect(new Rect2(frame.End.X, frame.Position.Y, size.X - frame.End.X, fh), dim);
		if (HideMarks) return;

		// Corner brackets.
		var line = new Color(Bone, 0.55f * a);
		float arm = Mathf.Round(fh * 0.09f);
		foreach (var (corner, dx, dy) in new[]
		{
			(frame.Position, 1f, 1f), (new Vector2(frame.End.X, frame.Position.Y), -1f, 1f),
			(new Vector2(frame.Position.X, frame.End.Y), 1f, -1f), (frame.End, -1f, -1f),
		})
		{
			_draw.DrawLine(corner, corner + new Vector2(arm * dx, 0), line, 1f);
			_draw.DrawLine(corner, corner + new Vector2(0, arm * dy), line, 1f);
		}

		// Focus square in the centre: bone while hunting, eye-yellow when locked.
		var c = size * 0.5f;
		float s = Mathf.Round(fh * 0.07f);
		var focus = FocusLocked ? new Color(Eye, 0.85f * a) : new Color(Bone, 0.45f * a);
		var box = new Rect2(c - new Vector2(s, s * 0.75f), new Vector2(s * 2, s * 1.5f));
		DrawCornerBox(box, s * 0.45f, focus);
		// the focus bar: a thin strip right of the square, filling as the lens comes in
		var bar = new Rect2(box.End.X + 6f, box.Position.Y, 2f, box.Size.Y);
		_draw.DrawRect(bar, new Color(Bone, 0.18f * a));
		float fill = Mathf.Clamp(FocusLevel, 0f, 1f) * bar.Size.Y;
		_draw.DrawRect(new Rect2(bar.Position.X, bar.End.Y - fill, bar.Size.X, fill), FocusLocked ? new Color(Eye, 0.8f * a) : new Color(Bone, 0.5f * a));

		// Readouts under the frame: exposure on the left, frames left on the right.
		var text = new Color(Bone, 0.5f * a);
		float y = frame.End.Y + 11f;
		_draw.DrawString(_mono, new Vector2(frame.Position.X, y), $"{Mathf.RoundToInt(35f * Zoom),3}mm   {(Racking ? $"F{Mathf.Lerp(2.8f, 11f, _close):0.#}" : "F2.8")}   1/60   ISO 800", HorizontalAlignment.Left, -1, 8, text);
		// the zoom's steps, top right inside the frame: the wheel moves along them
		float zx = frame.End.X - 44f, zy = frame.Position.Y + 9f;
		_draw.DrawString(_mono, new Vector2(zx - 18f, zy + 3f), $"x{Zoom:0.#}", HorizontalAlignment.Right, 16, 7, text);
		for (int i = 0; i < Player.CameraTool.ZoomSteps.Length; i++)
		{
			bool on = Mathf.IsEqualApprox(Player.CameraTool.ZoomSteps[i], Zoom);
			_draw.DrawRect(new Rect2(zx + i * 8f, zy - (on ? 2f : 1f), 5f, on ? 4f : 2f), on ? new Color(Eye, 0.7f * a) : new Color(Bone, 0.35f * a));
		}
		_draw.DrawString(_mono, new Vector2(frame.End.X - 60, y), $"{FramesLeft,2} EXP", HorizontalAlignment.Right, 60, 8, FramesLeft <= 3 ? new Color(Eye, 0.6f * a) : text);
		// (2026-10-29) the focus button, named while the picture's soft and it isn't held (as the player has it bound)
		if (!Racking && FocusLevel < 0.6f)
			_draw.DrawString(_mono, new Vector2(frame.Position.X, frame.Position.Y - 5f), $"[{KeyHint.Of("focus_lens", "Space")}] FOCUS", HorizontalAlignment.Left, -1, 8, new Color(Eye, 0.55f * a));
		if (_iris > 0.01f) DrawIris(c, fh * 0.2f, a * _iris);
	}

	/// <summary>The iris round the focus square: seven blades, their inner edges making the opening (a heptagon of radius
	/// set by <see cref="_close"/>), each edge run on out to the barrel as the blade's line, the blades smoky so the subject
	/// still shows through them; the barrel's ring round it all.</summary>
	private void DrawIris(Vector2 c, float r, float a)
	{
		const int n = 7;
		// folding in: the blades come in from the barrel as the iris shows
		float open = Mathf.Lerp(1f, 1f - _close * 0.85f, Mathf.SmoothStep(0f, 1f, _iris));
		float ro = r * open;
		float turn = _irisTurn + (1f - open) * 1.3f;
		var v = new Vector2[n];
		for (int i = 0; i < n; i++)
		{
			float ang = turn + i * Mathf.Tau / n;
			v[i] = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * ro;
		}
		var smoke = new Color(0.05f, 0.05f, 0.06f, 0.42f * a);
		var edge = new Color(Bone, 0.55f * a);
		for (int i = 0; i < n; i++)
		{
			Vector2 p0 = v[i], p1 = v[(i + 1) % n];
			// the blade's straight edge, on past the opening's corner to the barrel
			Vector2 d = (p1 - p0).Normalized();
			Vector2 end = RayToCircle(p1, d, c, r);
			// the blade: the opening's edge, out along its line to the barrel, round the barrel back to above p0
			float a0 = (p0 - c).Angle(), a1 = (end - c).Angle();
			while (a1 < a0) a1 += Mathf.Tau;
			var poly = new System.Collections.Generic.List<Vector2> { p0, p1, end };
			for (int k = 1; k <= 6; k++)
			{
				float t = Mathf.Lerp(a1, a0, k / 6f);
				poly.Add(c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r);
			}
			if (ro < r * 0.98f) _draw.DrawColoredPolygon(poly.ToArray(), smoke);
			_draw.DrawLine(p0, end, edge, 1f, true);
		}
		_draw.DrawArc(c, r, 0f, Mathf.Tau, 48, new Color(Bone, 0.4f * a), 1f, true);
		_draw.DrawArc(c, r + 3f, 0f, Mathf.Tau, 48, new Color(Bone, 0.14f * a), 1f, true);
		// the aperture's figure on the barrel: it reads the stop the blades are at
		float fstop = Mathf.Lerp(2.8f, 11f, _close);
		_draw.DrawString(_mono, c + new Vector2(r + 5f, -r * 0.6f), $"f/{fstop:0.#}", HorizontalAlignment.Left, -1, 7, new Color(Bone, 0.5f * a));
	}

	/// <summary>Where a ray from <paramref name="p"/> (inside the circle) along <paramref name="d"/> leaves it.</summary>
	private static Vector2 RayToCircle(Vector2 p, Vector2 d, Vector2 c, float r)
	{
		Vector2 f = p - c;
		float b = f.Dot(d), cc = f.LengthSquared() - r * r;
		float t = -b + Mathf.Sqrt(Mathf.Max(0f, b * b - cc));
		return p + d * t;
	}

	private void DrawCornerBox(Rect2 r, float arm, Color col)
	{
		var p = r.Position; var e = r.End;
		_draw.DrawLine(p, p + new Vector2(arm, 0), col); _draw.DrawLine(p, p + new Vector2(0, arm), col);
		_draw.DrawLine(new Vector2(e.X, p.Y), new Vector2(e.X - arm, p.Y), col); _draw.DrawLine(new Vector2(e.X, p.Y), new Vector2(e.X, p.Y + arm), col);
		_draw.DrawLine(new Vector2(p.X, e.Y), new Vector2(p.X + arm, e.Y), col); _draw.DrawLine(new Vector2(p.X, e.Y), new Vector2(p.X, e.Y - arm), col);
		_draw.DrawLine(e, e - new Vector2(arm, 0), col); _draw.DrawLine(e, e - new Vector2(0, arm), col);
	}
}
