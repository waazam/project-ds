using Godot;
using ProjectDS.Player;

namespace ProjectDS.World;

/// <summary>
/// One of R.H.'s four notes (Acts 3-7, along the Hollow path): a small sheet nailed at eye height to
/// a bare trunk right beside the path, one digit of the bunker door's code written big on it in his
/// hand. Dan, 2026-09-22: notes on trees, not survey stakes, with the actual number on them.
/// The trunk is built here (a dead snag, root flare, a few stubs) so the note always has a tree
/// exactly where the path needs it; a ClearZone keeps the scatter off it.
///
/// Reading is easy: within <see cref="ReadDistance"/> and looking roughly at the sheet reads it,
/// lantern or no lantern (Dan, 2026-09-22); the paper is faintly self-lit and winks as the player
/// nears, from <see cref="NoticeDistance"/>, so it is seen from the path in the dark. <see cref="FirstRead"/> fires the
/// first time; <see cref="SurveyLot"/> keeps the tally, saves the flag and shows the digit big.
/// </summary>
public partial class TreeNote : Node3D
{
	[Export] public string Label = "1";
	[Export] public int Digit;
	[Export] public float TrunkHeight = 7f;
	[Export] public float TrunkRadius = 0.3f;
	/// <summary>Eye height of the sheet on the trunk.</summary>
	[Export] public float NoteHeight = 1.55f;
	[Export] public float WakeDistance = 45f;
	/// <summary>Within this, the sheet in view reads it (no lantern needed).</summary>
	[Export] public float ReadDistance = 4.5f;
	/// <summary>Half-angle (degrees) of "looking at it".</summary>
	[Export] public float LookDegrees = 45f;
	/// <summary>The paper winks from this far, so the note is noticed from the path.</summary>
	[Export] public float NoticeDistance = 20f;

	/// <summary>Glow 0..1 of the read (for tests and the reveal state).</summary>
	public float Glow { get; private set; }
	/// <summary>The digit can be read right now.</summary>
	public bool Revealed => Glow > 0.6f;
	/// <summary>It has been read at least once (the lot counts these and saves the flag).</summary>
	public bool Read { get; private set; }
	/// <summary>Where the sheet is (world), for aiming at it.</summary>
	public Vector3 PlateWorld => ToGlobal(_sheetLocal);
	/// <summary>Raised once, the first time the digit reads.</summary>
	public event System.Action FirstRead;

	private Vector3 _sheetLocal;
	private StandardMaterial3D _paper;
	private StaticBody3D _trunkBody;
	private bool _missPrinted;
	private PlayerController _player;
	private Godot.Collections.Array<Rid> _exclude;
	private double _t;

	/// <summary>Restore: already read in the saved story (no event).</summary>
	public void MarkRead() => Read = true;

	private static readonly string[][] Font5x7 =
	{
		new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" },
		new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
		new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" },
		new[] { "11111", "00010", "00100", "00010", "00001", "10001", "01110" },
		new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" },
		new[] { "11111", "10000", "11110", "00001", "00001", "10001", "01110" },
		new[] { "00110", "01000", "10000", "11110", "10001", "10001", "01110" },
		new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
		new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" },
		new[] { "01110", "10001", "10001", "01111", "00001", "00010", "01100" },
	};

	/// <summary>The sheet's ink: the digit big in the middle (5x7 glyph at 4 px), a scrawled line under it.</summary>
	private static bool Inked(int digit, int x, int y, int w, int h)
	{
		const int scale = 4;
		int gw = 5 * scale, gh = 7 * scale, gx = (w - gw) / 2, gy = (h - gh) / 2 - 3;
		if (x >= gx && x < gx + gw && y >= gy && y < gy + gh)
		{
			int px = (x - gx) / scale, py = (y - gy) / scale;
			if (Font5x7[Mathf.Clamp(digit, 0, 9)][py][px] == '1') return true;
		}
		// the underline he scratched under every number
		int uy = gy + gh + 4;
		return y >= uy && y < uy + 2 && x >= gx - 2 && x < gx + gw + 2 && ((x + y) % 7 != 0);
	}

	public override void _Ready()
	{
		var terrain = GroundSnap.FindTerrain(this);
		if (terrain != null) GlobalPosition = GlobalPosition with { Y = terrain.HeightAt(GlobalPosition.X, GlobalPosition.Z) };
		Build();
	}

	private void Build()
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)(Label.GetHashCode() & 0x7fffffff) + 31 };
		float lean = Mathf.DegToRad(rng.RandfRange(-3f, 3f)), leanZ = Mathf.DegToRad(rng.RandfRange(-2.5f, 2.5f));
		var post = Basis.FromEuler(new Vector3(lean, 0, leanZ));

		// The trunk: a dead snag, flared at the root, tapering, a few broken stubs, bark like the forest's.
		var k = new MeshKit();
		k.Mat(ProcTextures.BarkMat);
		k.Color = new Color(0.62f, 0.58f, 0.52f);
		k.Cylinder(post * new Vector3(0, -0.5f, 0), post * new Vector3(0, 0.6f, 0), TrunkRadius * 1.45f, TrunkRadius * 1.02f, 8, false, 1f);
		float[] ys = { 0.6f, TrunkHeight * 0.45f, TrunkHeight * 0.8f, TrunkHeight };
		for (int s = 0; s < 3; s++)
		{
			float r0 = TrunkRadius * Mathf.Lerp(1.02f, 0.18f, ys[s] / TrunkHeight), r1 = TrunkRadius * Mathf.Lerp(1.02f, 0.18f, ys[s + 1] / TrunkHeight);
			Vector3 wobble = new(rng.RandfRange(-0.08f, 0.08f), 0, rng.RandfRange(-0.08f, 0.08f));
			k.Cylinder(post * new Vector3(0, ys[s], 0), post * (new Vector3(0, ys[s + 1], 0) + wobble * s), r0, r1, 8, s == 2, 1f);
		}
		k.Color = new Color(0.5f, 0.46f, 0.4f);
		for (int i = 0; i < 4; i++)
		{
			float y = rng.RandfRange(2.6f, TrunkHeight - 0.8f), a = rng.RandfRange(0f, Mathf.Tau);
			Vector3 dir = new(Mathf.Cos(a), rng.RandfRange(-0.1f, 0.35f), Mathf.Sin(a));
			Vector3 root = post * new Vector3(0, y, 0) + dir * TrunkRadius * 0.6f;
			k.Cylinder(root, root + dir.Normalized() * rng.RandfRange(0.4f, 0.9f), 0.05f, 0.02f, 5);
		}
		k.CommitTo(this, "Trunk");
		_trunkBody = new StaticBody3D { Name = "TrunkBody", CollisionLayer = 1, CollisionMask = 0 };
		var body = _trunkBody;
		body.SetMeta("surface", "wood");
		AddChild(body);
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, TrunkHeight * 0.5f, 0), Shape = new CylinderShape3D { Radius = TrunkRadius * 0.95f, Height = TrunkHeight } });

		// The sheet: pale paper on the trunk's path side (+Z), the digit big on it, a nail through the top.
		int w = 32, h = 40;
		string key = $"treenote_{Label}_{Digit}";
		var tex = ItemTextures.Make(key, w, h, (x, y) =>
		{
			float n = ItemTextures.Fbm(x, y, w, h, 3, 3, 2, 733) * 0.08f;
			bool edge = x == 0 || y == 0 || x == w - 1 || y == h - 1;
			float v = (edge ? 0.62f : 0.86f) + n - (y < 3 ? 0.06f : 0f);
			if (Inked(Digit, x, y, w, h)) return new Color(0.12f, 0.1f, 0.09f);
			return new Color(v, v * 0.97f, v * 0.88f);
		});
		_paper = new StandardMaterial3D
		{
			AlbedoTexture = tex,
			EmissionEnabled = true,
			EmissionTexture = tex,
			Emission = Colors.White,
			EmissionEnergyMultiplier = 0f,
			Roughness = 0.9f,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
		};
		var pk = new MeshKit();
		pk.Mat(_paper);
		pk.Color = Colors.White;
		Vector3 c = post * new Vector3(0, NoteHeight, TrunkRadius * Mathf.Lerp(1.02f, 0.18f, NoteHeight / TrunkHeight) + 0.012f);
		_sheetLocal = c;
		float pw = 0.16f, ph = 0.2f;
		float tilt = Mathf.DegToRad(rng.RandfRange(-5f, 5f));
		var sheet = post * Basis.FromEuler(new Vector3(0, 0, tilt));
		Vector3 ex = sheet * new Vector3(pw * 0.5f, 0, 0), ey = sheet * new Vector3(0, ph * 0.5f, 0);
		pk.Quad(c - ex - ey, c + ex - ey, c + ex + ey, c - ex + ey, post * Vector3.Back,
			new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
		var plate = pk.CommitTo(this, "Sheet", false);
		plate.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		var nk = new MeshKit();
		nk.Mat(ProcTextures.MetalMat);
		nk.Color = new Color(0.4f, 0.4f, 0.38f);
		nk.Cylinder(c + ey * 0.85f + post * Vector3.Forward * 0.02f, c + ey * 0.85f + post * Vector3.Back * 0.012f, 0.006f, 0.005f, 5);
		nk.CommitTo(this, "Nail", false);
	}

	public override void _Process(double delta)
	{
		if (_player == null || !IsInstanceValid(_player))
		{
			_player = GetTree().GetFirstNodeInGroup("player") as PlayerController;
			if (_player == null) return;
			_exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
			if (_trunkBody != null) _exclude.Add(_trunkBody.GetRid());
		}
		float dt = (float)delta;
		_t += delta;
		float target = 0f;
		Vector3 sheet = PlateWorld;
		float playerDist = sheet.DistanceTo(_player.GlobalPosition);
		// No lantern condition at all: close enough and looking at it is the read.
		if (playerDist < WakeDistance && _player.CameraRig?.Camera is { } cam)
		{
			Vector3 eye = cam.GlobalPosition;
			Vector3 to = sheet - eye;
			float dist = to.Length();
			float angle = dist > 0.1f ? Mathf.RadToDeg(Mathf.Acos(Mathf.Clamp((-cam.GlobalBasis.Z).Dot(to / dist), -1f, 1f))) : 0f;
			string miss = null;
			if (dist > 0.1f && dist <= ReadDistance)
			{
				if (angle <= LookDegrees)
				{
					// Line of sight to the paper, ignoring the player and the note's own trunk (the sheet sits inside the
					// trunk collider's radius, so the ray must not be allowed to hit it); the ray stops just short of the paper.
					var q = PhysicsRayQueryParameters3D.Create(eye, sheet - to / dist * 0.05f, 1u);
					q.Exclude = _exclude;
					var hit = GetWorld3D().DirectSpaceState.IntersectRay(q);
					if (hit.Count == 0) target = 1f;
					else miss = $"ray hit {(hit["collider"].AsGodotObject() as Node)?.Name} at {((Vector3)hit["position"]).DistanceTo(eye):0.00} m";
				}
				else miss = $"angle {angle:0} deg (limit {LookDegrees})";
			}
			else if (dist <= ReadDistance + 2f) miss = $"dist {dist:0.00} m (limit {ReadDistance})";
			if (miss != null && !Read && !_missPrinted) { _missPrinted = true; GD.Print($"[note {Label}] near miss: {miss}"); }
		}
		Glow = Mathf.MoveToward(Glow, target, dt * (target > Glow ? 3f : 1.8f));
		if (Revealed && !Read) { Read = true; FirstRead?.Invoke(); }
		// The paper is faintly self-lit so the digit reads in the dark, winks as the player nears, and lights up as it is read.
		if (_paper != null)
		{
			float near = Mathf.Clamp(1f - playerDist / NoticeDistance, 0f, 1f);
			float wink = 0.5f + 0.5f * Mathf.Sin((float)_t * 2.2f);
			_paper.EmissionEnergyMultiplier = 0.35f + near * (0.25f + 0.5f * wink * wink) + 1.4f * Glow;
		}
	}
}
