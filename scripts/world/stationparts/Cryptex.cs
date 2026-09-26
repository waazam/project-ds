using System.Collections.Generic;
using Godot;

namespace ProjectDS.World.StationParts;

/// <summary>
/// Room 2's puzzle box: a brass-and-bronze cryptex lying on the table — six letter rings between two
/// ornate end caps, the whole alphabet round each ring, a reading line along the top. Turn the rings
/// until the top row spells the word and the end cap slides free.
///
/// It is old and it sticks: every turn clacks round with a small overshoot and settles, and now and
/// then a ring catches, grinds a little way and springs back, and has to be turned again (never twice
/// running). <see cref="Select"/> / <see cref="Turn"/> are driven by <c>CryptexOverlay</c>.
///
/// Local space: its long axis is X, centred on the origin; the reading line faces +Y.
/// </summary>
public partial class Cryptex : Node3D
{
	public const string Word = "STAIRS";
	public const int Rings = 6;
	private const float RingR = 0.065f, RingW = 0.052f, Gap = 0.006f;
	private const float Step = Mathf.Tau / 26f;

	/// <summary>The letter each ring shows on the reading line (0 = A).</summary>
	public int[] Letters { get; } = { 16, 23, 4, 12, 11, 1 };   // Q X E M L B
	public int Selected { get; private set; }
	public bool Solved { get; private set; }
	public bool Open { get; private set; }
	public string Reading
	{
		get { var c = new char[Rings]; for (int i = 0; i < Rings; i++) c[i] = (char)('A' + Letters[i]); return new string(c); }
	}
	public event System.Action SolvedEvent;
	/// <summary>A ring clacked into place (true) or stuck (false).</summary>
	public event System.Action<bool> Clicked;

	private readonly Node3D[] _rings = new Node3D[Rings];
	private readonly float[] _angle = new float[Rings];
	private readonly float[] _vel = new float[Rings];
	private readonly float[] _stickT = new float[Rings];
	private readonly MeshInstance3D[] _glow = new MeshInstance3D[Rings];
	private Node3D _capR;
	private bool _lastStuck;
	private readonly RandomNumberGenerator _rng = new() { Seed = 606 };

	public static float RingX(int i) => (i - (Rings - 1) * 0.5f) * (RingW + Gap);

	public override void _Ready()
	{
		var bronze = StationTextures.Flat("st_bronze", new Color(0.46f, 0.32f, 0.16f), 0.35f, 0.8f);
		var brass = ItemTextures.BrassMat;
		// the core the rings turn on, and the end caps
		var k = new MeshKit();
		k.Mat(bronze);
		k.Color = Colors.White;
		float half = Rings * 0.5f * (RingW + Gap);
		k.Cylinder(new Vector3(-half - 0.01f, 0, 0), new Vector3(half + 0.01f, 0, 0), RingR * 0.8f, RingR * 0.8f, 16, true);
		k.CommitTo(this, "Core", true);
		_capR = BuildCap(bronze, brass, 1, half);
		BuildCap(bronze, brass, -1, half);
		// the reading guides: two thin raised bars along the top
		var g = new MeshKit();
		g.Mat(brass);
		g.Color = new Color(0.9f, 0.75f, 0.45f);
		foreach (int s in new[] { -1, 1 })
			g.Box(new Vector3(0, RingR + 0.004f, s * 0.018f), new Vector3(half * 2f + 0.03f, 0.004f, 0.004f), 4f);
		g.CommitTo(this, "Guides", false);

		for (int i = 0; i < Rings; i++)
		{
			var ring = new Node3D { Name = $"Ring{i}", Position = new Vector3(RingX(i), 0, 0) };
			AddChild(ring);
			_rings[i] = ring;
			var rk = new MeshKit();
			rk.Mat(brass);
			rk.Color = new Color(0.75f + 0.05f * (i % 2), 0.62f, 0.4f);
			rk.Cylinder(new Vector3(-RingW * 0.5f, 0, 0), new Vector3(RingW * 0.5f, 0, 0), RingR, RingR, 26, true);
			// raised lips on the edges
			rk.Color = new Color(0.55f, 0.42f, 0.24f);
			foreach (int s in new[] { -1, 1 })
				rk.Cylinder(new Vector3(s * RingW * 0.5f - s * 0.003f, 0, 0), new Vector3(s * RingW * 0.5f, 0, 0), RingR + 0.003f, RingR + 0.003f, 26, true);
			rk.CommitTo(ring, "Ring", true);
			// the alphabet round it, each letter facing out
			for (int L = 0; L < 26; L++)
			{
				float a = L * Step;
				var b = new Basis(Vector3.Right, -a);
				var label = new Label3D
				{
					Text = ((char)('A' + L)).ToString(), Font = SignKit.RoutedFont, FontSize = 48, PixelSize = 0.00032f,   // one letter per 1/26 of the ring, never overlapping its neighbours
					Modulate = new Color(0.14f, 0.1f, 0.05f), OutlineSize = 0, Shaded = true,
					AlphaCut = Label3D.AlphaCutMode.Discard, DoubleSided = false,
					// on the surface, facing out along its radius (+Y rotated back by the letter's angle)
					Transform = new Transform3D(b * Basis.LookingAt(Vector3.Down, Vector3.Forward), b * new Vector3(0, RingR + 0.0012f, 0)),
				};
				ring.AddChild(label);
			}
			// a faint glow round the selected ring
			var glow = new MeshInstance3D
			{
				Mesh = new CylinderMesh { TopRadius = RingR + 0.006f, BottomRadius = RingR + 0.006f, Height = RingW + 0.004f, RadialSegments = 20 },
				Rotation = new Vector3(0, 0, Mathf.Pi * 0.5f),
				MaterialOverride = new StandardMaterial3D
				{
					AlbedoColor = new Color(1f, 0.8f, 0.4f, 0.12f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Front,
				},
				Position = ring.Position, Visible = false,
			};
			AddChild(glow);
			_glow[i] = glow;
			_angle[i] = Letters[i] * Step;
			ring.Rotation = new Vector3(_angle[i], 0, 0);
		}
		Select(0);
	}

	private Node3D BuildCap(Material bronze, Material brass, int side, float half)
	{
		var cap = new Node3D { Name = side > 0 ? "CapRight" : "CapLeft", Position = new Vector3(side * (half + 0.035f), 0, 0) };
		AddChild(cap);
		var k = new MeshKit();
		k.Mat(bronze);
		k.Color = Colors.White;
		k.Cylinder(new Vector3(-side * 0.03f, 0, 0), new Vector3(side * 0.03f, 0, 0), RingR + 0.012f, RingR + 0.008f, 20, true);
		k.Mat(brass);
		k.Color = new Color(0.85f, 0.7f, 0.42f);
		k.Cylinder(new Vector3(side * 0.03f, 0, 0), new Vector3(side * 0.05f, 0, 0), RingR * 0.7f, RingR * 0.35f, 16, true);
		ItemMeshes.Torus(k, new Vector3(side * 0.012f, 0, 0), Vector3.Right, RingR + 0.012f, 0.005f, 20, 5);
		k.CommitTo(cap, "Cap", true);
		return cap;
	}

	public void Select(int ring)
	{
		Selected = Mathf.Clamp(ring, 0, Rings - 1);
		for (int i = 0; i < Rings; i++) _glow[i].Visible = i == Selected && !Solved;
	}

	/// <summary>Turns the selected ring one letter (+1 = on through the alphabet). Returns false if it stuck.</summary>
	public bool Turn(int dir)
	{
		if (Solved || _stickT[Selected] > 0f) return false;
		int i = Selected;
		// now and then it catches: a short grind, a spring back, and it hasn't moved
		if (!_lastStuck && _rng.Randf() < 0.12f)
		{
			_lastStuck = true;
			_stickT[i] = 0.32f;
			_vel[i] = dir * 3.5f;
			Clicked?.Invoke(false);
			return false;
		}
		_lastStuck = false;
		Letters[i] = ((Letters[i] + dir) % 26 + 26) % 26;
		Clicked?.Invoke(true);
		if (Reading == Word) { Solved = true; Select(Selected); SolvedEvent?.Invoke(); }
		return true;
	}

	/// <summary>For tests (and the flood's end): set a ring straight to a letter.</summary>
	public void ForceLetter(int ring, char c)
	{
		Letters[ring] = c - 'A';
		if (Reading == Word && !Solved) { Solved = true; Select(Selected); SolvedEvent?.Invoke(); }
	}

	/// <summary>Sets every ring at once without a sound (Continue, after it was solved).</summary>
	public void ForceLetters(string word)
	{
		for (int i = 0; i < Rings && i < word.Length; i++) Letters[i] = word[i] - 'A';
		Solved = Reading == Word;
		if (_glow[0] != null) Select(Selected);
	}

	/// <summary>The right-hand end cap slides free.</summary>
	public void SlideOpen()
	{
		if (Open) return;
		Open = true;
		var tw = CreateTween();
		tw.TweenProperty(_capR, "position:x", _capR.Position.X + 0.14f, 1.1f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		for (int i = 0; i < Rings; i++)
		{
			float target = Letters[i] * Step;
			if (_stickT[i] > 0f)
			{
				// the grind: it tries, gets a little way, and springs back
				_stickT[i] -= dt;
				float u = 1f - _stickT[i] / 0.32f;
				float nudge = Mathf.Sin(u * Mathf.Pi) * Step * 0.35f * Mathf.Sign(_vel[i]);
				_rings[i].Rotation = new Vector3(_angle[i] + nudge, 0, 0);
				continue;
			}
			// clunky: a stiff spring with a little overshoot, heavier than it should be
			float diff = Mathf.AngleDifference(_angle[i], target);
			_vel[i] += (diff * 90f - _vel[i] * 11f) * dt;
			_angle[i] += _vel[i] * dt;
			_rings[i].Rotation = new Vector3(_angle[i], 0, 0);
		}
	}
}
