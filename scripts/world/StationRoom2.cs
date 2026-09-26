using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;
using ProjectDS.World.StationParts;

namespace ProjectDS.World;

/// <summary>
/// Act 13, Room 2 (right of the lobby's desk): a strikingly red room with nothing in it but a
/// black-and-white diamond marble floor, a single table in the middle, and on the table a brass
/// cryptex (<see cref="Cryptex"/>). The door slams shut behind the player; five seconds later,
/// someone knocks on it, softly, from the lobby side, and keeps knocking.
///
/// The first touch of the cryptex breaks the window on the right: it cracks, then bursts, and the lake
/// comes in — the player is thrown to the floor and gets up to water rising round them. From then on it
/// is a race: the room fills to the ceiling in <see cref="FloodSeconds"/>; once it is at their knees
/// the water turns to blood. Solve the cryptex (<see cref="CryptexOverlay"/>: the word is STAIRS) and
/// the blood is sucked back out through the window, the glass flies back into its frame, the room is as
/// it was, and the cryptex opens on an old lighter. Fail and they drown: back to the checkpoint.
///
/// Later, for the iron door's CLIMB: once the player has seen the door, the table is gone next time
/// they come in — a short wooden staircase stands in its place, going up to nothing. Climbing it is
/// the point. At the top, dark, a knock right behind them, and they come to on the floor, the stairs
/// gone, holding one of the steps.
///
/// Local space: floor y=0, x/z in [-Half, Half]; its doorway is the +X wall's gap at z=0 (the lobby's
/// -X wall); the window is in the -Z wall (on the right as the player comes in).
/// </summary>
public partial class StationRoom2 : Node3D
{
	public const float Half = 3.4f, Height = 3.0f;
	[Export] public float FloodSeconds = 80f;
	[Export] public float KneeHeight = 0.5f;

	public bool Solved { get; private set; }
	public bool Flooding { get; private set; }
	public bool WindowBroken { get; private set; }
	public float Level { get; private set; }
	public bool Blood { get; private set; }
	public bool DoorShut { get; private set; }
	public int Knocks { get; private set; }
	public Cryptex Box { get; private set; }
	public Interactable BoxUse => _boxUse;
	/// <summary>The scavenger staircase (for tests), and whether it is standing.</summary>
	public Node3D Stairs => _stairs;
	public bool StairsUp => _stairs is { Visible: true };
	public Vector3 StairTopWorld => ToGlobal(new Vector3(0, 1.75f, -1.5f));
	public Vector3 StairFootWorld => ToGlobal(new Vector3(0, 0.05f, 1.2f));

	private Node3D _table, _stairs, _windowPane, _shards;
	private Interactable _boxUse;
	private MeshInstance3D _water, _torrent;
	private ShaderMaterial _waterMat, _torrentMat;
	private StandardMaterial3D _wallMat;
	private AudioStreamPlayer3D _rush;
	private readonly List<Node3D> _drapes = new();
	private float _billow, _billowTarget;
	private bool _drapesStill = true;
	private double _knockTimer = -1;
	private bool _dying, _winning, _stepSequence;
	private Area3D _entry, _stairTop;
	private readonly RandomNumberGenerator _rng = new() { Seed = 2222 };
	private static readonly Color Lake = new(0.07f, 0.1f, 0.08f), BloodC = new(0.28f, 0.01f, 0.01f);

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private void Build()
	{
		var s = StoryManager.Instance;
		BuildShell();
		BuildWindow();
		BuildTable();
		BuildWater();
		BuildStairs();
		_entry = StoryBeat.MakeTrigger(this, new BoxShape3D { Size = new Vector3(1.4f, 2.4f, 2.4f) }, new Vector3(Half - 1.6f, 1.2f, 0), OnEntered, "Room2Entry");

		if (s != null && s.HasFlag(StoryManager.Flag.StationRoom2Solved))
		{
			Solved = true;
			Box.ForceLetters(Cryptex.Word);
			Box.SlideOpen();
			_boxUse.Enabled = false;
			PlaceLighter();
		}
		SetProcess(true);
	}

	// ------------------------------------------------------------------ the room

	private void BuildShell()
	{
		var body = new StaticBody3D { Name = "Walls", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "stone");
		AddChild(body);
		_wallMat = StationTextures.RedWallpaperMat;
		var k = new MeshKit();
		k.Mat(_wallMat);
		k.Color = Colors.White;
		// the wall shared with the lobby: a thin skin just inside the lobby's own wall (never in the same
		// plane, or the two wallpapers fight), no collision of its own (the lobby's wall has it)
		StationKit.WallAlongZ(k, null, Half - 0.085f, -Half, Half, Height, 0, (0f, 2.2f), 2.2f, 0.02f);
		StationKit.WallAlongX(k, body, Half, -Half, Half, Height, 0, null);
		// the window wall: solid round a 1.6 x 1.4 opening
		float wx0 = -1.2f, wx1 = 0.4f, wy0 = 1.0f, wy1 = 2.4f;
		k.Box(new Vector3((-Half + wx0) * 0.5f, Height * 0.5f, -Half), new Vector3(wx0 + Half, Height, 0.14f), 1.1f);
		k.Box(new Vector3((wx1 + Half) * 0.5f, Height * 0.5f, -Half), new Vector3(Half - wx1, Height, 0.14f), 1.1f);
		k.Box(new Vector3((wx0 + wx1) * 0.5f, wy0 * 0.5f, -Half), new Vector3(wx1 - wx0, wy0, 0.14f), 1.1f);
		k.Box(new Vector3((wx0 + wx1) * 0.5f, (wy1 + Height) * 0.5f, -Half), new Vector3(wx1 - wx0, Height - wy1, 0.14f), 1.1f);
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, Height * 0.5f, -Half), Shape = new BoxShape3D { Size = new Vector3(Half * 2f, Height, 0.14f) } });
		StationKit.WallAlongZ(k, body, -Half, -Half, Half, Height, 0, null);
		k.CommitTo(this, "Walls", true);
		// the floor: black and white marble, on the diagonal
		var f = new MeshKit();
		f.Mat(StationTextures.MarbleMat);
		f.Color = Colors.White;
		BuildKit.Box(f, new Vector3(0, -0.05f, 0), new Vector3(Half * 2f, 0.1f, Half * 2f), 0.5f);
		f.CommitTo(this, "Floor", true);
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, -0.05f, 0), Shape = new BoxShape3D { Size = new Vector3(Half * 2f, 0.1f, Half * 2f) } });
		var c = new MeshKit();
		c.Mat(StationTextures.Flat("st_offwhite_ceil", new Color(0.86f, 0.83f, 0.76f), 0.85f, 0.15f));
		c.Color = Colors.White;
		BuildKit.Box(c, new Vector3(0, Height + 0.05f, 0), new Vector3(Half * 2f, 0.1f, Half * 2f), 1f, BuildKit.Face.PY);
		c.CommitTo(this, "Ceiling", false);
		// one bare bulb on a flex over the table
		var bulb = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.05f, Height = 0.12f }, Position = new Vector3(0, Height - 0.7f, 0),
			MaterialOverride = StationTextures.Glow("st_bulb2", new Color(1f, 0.85f, 0.65f), 2f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(bulb);
		var flex = new MeshKit();
		flex.Mat(StationTextures.Flat("st_flex", new Color(0.05f, 0.05f, 0.05f)));
		flex.Color = Colors.White;
		flex.Cylinder(new Vector3(0, Height, 0), new Vector3(0, Height - 0.65f, 0), 0.006f, 0.006f, 4, false);
		flex.CommitTo(this, "Flex", false);
		AddChild(new OmniLight3D
		{
			Name = "Bulb", LightColor = new Color(1f, 0.82f, 0.62f), LightEnergy = 1.8f, OmniRange = 8f,
			OmniAttenuation = 1.1f, Position = new Vector3(0, Height - 0.8f, 0), ShadowEnabled = true,
		});
	}

	private void BuildWindow()
	{
		float cx = -0.4f, cy = 1.7f;
		var k = new MeshKit();
		k.Mat(PropTextures.PostMat);
		k.Color = new Color(0.3f, 0.26f, 0.2f);
		BuildKit.Box(k, new Vector3(cx, 1.0f, -Half + 0.1f), new Vector3(1.8f, 0.08f, 0.2f));
		BuildKit.Box(k, new Vector3(cx, 2.42f, -Half + 0.05f), new Vector3(1.8f, 0.08f, 0.12f));
		foreach (int s in new[] { -1, 1 }) BuildKit.Box(k, new Vector3(cx + s * 0.84f, cy, -Half + 0.05f), new Vector3(0.08f, 1.5f, 0.12f));
		BuildKit.Box(k, new Vector3(cx, cy, -Half + 0.05f), new Vector3(0.05f, 1.4f, 0.06f));   // the glazing bar
		k.CommitTo(this, "WindowFrame", true);
		// outside, pressing against the glass: dark lake water, a faint green light far off in it
		AddChild(new MeshInstance3D
		{
			Name = "Outside", Mesh = new QuadMesh { Size = new Vector2(1.7f, 1.45f) }, Position = new Vector3(cx, cy, -Half - 0.12f),
			MaterialOverride = StationTextures.Glow("st_underlake", new Color(0.05f, 0.12f, 0.1f), 0.5f),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		_windowPane = new Node3D { Name = "Pane", Position = new Vector3(cx, cy, -Half + 0.02f) };
		AddChild(_windowPane);
		_windowPane.AddChild(new MeshInstance3D
		{
			Mesh = new QuadMesh { Size = new Vector2(1.6f, 1.4f) },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.6f, 0.6f, 0.28f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f, MetallicSpecular = 0.9f },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
		// the shards it breaks into (hidden until then): they fly in, and later fly back
		_shards = new Node3D { Name = "Shards", Visible = false };
		AddChild(_shards);
		var glass = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.7f, 0.7f, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f, MetallicSpecular = 0.9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		for (int i = 0; i < 14; i++)
		{
			var sk = new MeshKit();
			sk.Mat(glass);
			sk.Color = Colors.White;
			float w = _rng.RandfRange(0.12f, 0.35f), h = _rng.RandfRange(0.12f, 0.35f);
			sk.Tri(new Vector3(-w, -h, 0), new Vector3(w, -h * 0.3f, 0), new Vector3(0, h, 0), Vector3.Back, Vector2.Zero, Vector2.Right, Vector2.Down);
			var shard = sk.CommitTo(_shards, $"Shard{i}", false);
			shard.SetMeta("home", new Vector3(cx + _rng.RandfRange(-0.7f, 0.7f), cy + _rng.RandfRange(-0.6f, 0.6f), -Half + 0.02f));
			shard.SetMeta("rest", new Vector3(_rng.RandfRange(-2.4f, 1.8f), 0.02f, _rng.RandfRange(-Half + 0.5f, 1.2f)));
		}
		// the torrent (hidden until then)
		_torrentMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/torrent.gdshader") };
		_torrentMat.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		var tk = new MeshKit();
		tk.Mat(_torrentMat);
		tk.Color = Colors.White;
		// a sheet out over the sill and curving down onto the floor
		Vector3 prevL = new(cx - 0.75f, 1.02f, -Half + 0.05f), prevR = new(cx + 0.75f, 1.02f, -Half + 0.05f);
		for (int i = 1; i <= 8; i++)
		{
			float t = i / 8f;
			float z = -Half + 0.05f + t * 1.4f, y = Mathf.Lerp(1.02f, 0f, t * t);
			Vector3 l = new(cx - 0.75f - t * 0.2f, y, z), r = new(cx + 0.75f + t * 0.2f, y, z);
			tk.Quad(prevL, prevR, r, l, Vector3.Back, new Vector2(0, (i - 1) / 8f), new Vector2(1, (i - 1) / 8f), new Vector2(1, t), new Vector2(0, t));
			prevL = l; prevR = r;
		}
		_torrent = tk.CommitTo(this, "Torrent", false);
		_torrent.Visible = false;
		BuildCurtains(cx);
	}

	/// <summary>Heavy crimson velvet drapes either side of the window on a brass rod, with a swagged
	/// valance and gold tie-backs. Each drape hangs from a pivot on the rod so the torrent can push it
	/// into the room.</summary>
	private void BuildCurtains(float cx)
	{
		const float rodY = 2.66f;
		float rodZ = -Half + 0.17f;
		var hw = new MeshKit();
		hw.Mat(ItemTextures.BrassMat);
		hw.Color = new Color(0.8f, 0.62f, 0.34f);
		hw.Cylinder(new Vector3(cx - 1.38f, rodY, rodZ), new Vector3(cx + 1.38f, rodY, rodZ), 0.018f, 0.018f, 8, true);
		foreach (int s in new[] { -1, 1 })
		{
			float fx = cx + s * 1.38f;
			hw.Cylinder(new Vector3(fx, rodY, rodZ), new Vector3(fx + s * 0.07f, rodY, rodZ), 0.035f, 0.012f, 8, true);   // finial
			hw.Cylinder(new Vector3(fx - s * 0.1f, rodY, -Half + 0.07f), new Vector3(fx - s * 0.1f, rodY, rodZ), 0.01f, 0.01f, 6, true);   // bracket
		}
		hw.CommitTo(this, "CurtainRod", false);
		// the valance: a pleated band across the top, its lower edge hanging in three swags, gold fringe along it
		var v = new MeshKit();
		v.Mat(StationTextures.VelvetMat);
		v.Color = Colors.White;
		const int vn = 36;
		float vx0 = cx - 1.32f, vx1 = cx + 1.32f;
		Vector3 VTop(int i) { float u = i / (float)vn; return new Vector3(Mathf.Lerp(vx0, vx1, u), rodY + 0.1f, rodZ + 0.04f + 0.02f * Mathf.Sin(u * Mathf.Pi * 24f)); }
		Vector3 VBot(int i) { float u = i / (float)vn; return VTop(i) with { Y = rodY - 0.14f - 0.1f * Mathf.Abs(Mathf.Sin(u * Mathf.Pi * 3f)) }; }
		for (int i = 0; i < vn; i++)
			v.Quad(VBot(i), VBot(i + 1), VTop(i + 1), VTop(i), Vector3.Back,
				new Vector2(i / (float)vn * 2f, 0.4f), new Vector2((i + 1) / (float)vn * 2f, 0.4f), new Vector2((i + 1) / (float)vn * 2f, 0), new Vector2(i / (float)vn * 2f, 0));
		v.Mat(ItemTextures.BrassMat);
		v.Color = new Color(0.85f, 0.66f, 0.3f);
		for (int i = 0; i < vn; i++)
		{
			Vector3 a = VBot(i), b = VBot(i + 1);
			v.Quad(a + Vector3.Down * 0.035f, b + Vector3.Down * 0.035f, b, a, Vector3.Back);
		}
		v.CommitTo(this, "Valance", false);

		foreach (int s in new[] { -1, 1 })
		{
			var pivot = new Node3D { Name = s < 0 ? "DrapeL" : "DrapeR", Position = new Vector3(cx + s * 0.92f, rodY - 0.02f, rodZ) };
			AddChild(pivot);
			BuildDrape(pivot, s);
			_drapes.Add(pivot);
		}
	}

	/// <summary>One drape, in its pivot's space: outer edge straight down at x = s*0.34, inner edge swept
	/// in by the tie-back at waist height and flaring out again below it, deep pleats all the way down.</summary>
	private static void BuildDrape(Node3D pivot, int s)
	{
		const int cols = 18, rows = 16;
		const float len = 2.58f, tieY = -1.5f;
		float xo = s * 0.34f, xi = -s * 0.3f;
		float Gather(float y)
		{
			if (y > tieY) return Mathf.Lerp(0.36f, 1f, Mathf.SmoothStep(tieY, 0f, y));
			return Mathf.Lerp(0.36f, 0.8f, Mathf.SmoothStep(tieY, -len, y));
		}
		Vector3 P(int c, int r)
		{
			float u = c / (float)cols, y = -len * r / rows;
			float g = Gather(y);
			float x = Mathf.Lerp(xo, Mathf.Lerp(xo, xi, g), u);
			float deep = 0.03f + 0.03f * (1f - g);   // bunched tighter where it's gathered
			float z = deep * Mathf.Sin(u * Mathf.Pi * 2f * 5f) + (y < -len + 0.12f ? 0.02f : 0f);
			return new Vector3(x, y, z);
		}
		var k = new MeshKit();
		k.Mat(StationTextures.VelvetMat);
		k.Color = Colors.White;
		for (int r = 0; r < rows; r++)
			for (int c = 0; c < cols; c++)
			{
				Vector3 a = P(c, r + 1), b = P(c + 1, r + 1), d = P(c + 1, r), e = P(c, r);
				Vector3 n = (b - a).Cross(e - a).Normalized();
				if (n.Z < 0) n = -n;
				k.Quad(a, b, d, e, n,
					new Vector2(c / (float)cols, (r + 1) / (float)rows * 2f), new Vector2((c + 1) / (float)cols, (r + 1) / (float)rows * 2f),
					new Vector2((c + 1) / (float)cols, r / (float)rows * 2f), new Vector2(c / (float)cols, r / (float)rows * 2f));
			}
		k.CommitTo(pivot, "Velvet", false);
		// the gold tie-back round the waist, and its tassel
		float gx = Mathf.Lerp(xo, xi, 0.36f);
		var t = new MeshKit();
		t.Mat(ItemTextures.BrassMat);
		t.Color = new Color(0.9f, 0.7f, 0.3f);
		t.Cylinder(new Vector3(xo, tieY, 0.07f), new Vector3(gx, tieY, 0.07f), 0.016f, 0.016f, 6, true);
		t.Cylinder(new Vector3(gx * 0.5f + xo * 0.5f, tieY, 0.075f), new Vector3(gx * 0.5f + xo * 0.5f, tieY - 0.16f, 0.075f), 0.012f, 0.03f, 8, true);
		t.CommitTo(pivot, "TieBack", false);
	}

	/// <summary>The drapes stir in the torrent while it runs, and hang still otherwise.</summary>
	private void MoveDrapes(float dt)
	{
		_billow = Mathf.MoveToward(_billow, _billowTarget, dt * (_billowTarget > _billow ? 1.6f : 0.5f));
		if (_billow <= 0.001f && _drapesStill) return;
		_drapesStill = _billow <= 0.001f;
		float time = (float)Time.GetTicksMsec() * 0.001f;
		for (int i = 0; i < _drapes.Count; i++)
		{
			float sway = 0.06f * Mathf.Sin(time * 1.3f + i * 1.9f) + 0.03f * Mathf.Sin(time * 2.1f + i);
			// negative X rotation swings the hem out into the room
			_drapes[i].Rotation = new Vector3(-_billow * (0.5f + sway), 0, (i == 0 ? -1 : 1) * _billow * 0.08f);
		}
	}

	private void BuildTable()
	{
		_table = new Node3D { Name = "Table" };
		AddChild(_table);
		var k = new MeshKit();
		k.Mat(PropTextures.DeckMat);
		k.Color = new Color(0.16f, 0.1f, 0.07f);
		BuildKit.Box(k, new Vector3(0, 0.86f, 0), new Vector3(1.2f, 0.06f, 0.7f), 1.2f);
		foreach (var (x, z) in new[] { (-0.52f, -0.28f), (0.52f, -0.28f), (-0.52f, 0.28f), (0.52f, 0.28f) })
			k.Cylinder(new Vector3(x, 0, z), new Vector3(x, 0.83f, z), 0.035f, 0.03f, 6, true);
		// the brass plate in front of the box
		k.Mat(ItemTextures.BrassMat);
		k.Color = new Color(0.85f, 0.7f, 0.42f);
		BuildKit.Box(k, new Vector3(0, 0.892f, 0.22f), new Vector3(0.36f, 0.004f, 0.08f), 4f);
		k.Color = Colors.White;
		k.CommitTo(_table, "Table", true);
		SignKit.Text(_table, "WHAT YOU WERE TOLD\nNEVER TO GO UP", new Vector3(0, 0.896f, 0.22f), new Basis(Vector3.Right, -Mathf.Pi * 0.5f), 0.022f, new Color(0.18f, 0.12f, 0.05f), shadow: false);
		var body = new StaticBody3D { Name = "TableBody", CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, 0.45f, 0), Shape = new BoxShape3D { Size = new Vector3(1.2f, 0.9f, 0.7f) } });
		_table.AddChild(body);

		Box = new Cryptex { Name = "Cryptex", Position = new Vector3(0, 0.89f + 0.075f, -0.02f) };
		_table.AddChild(Box);
		Box.Clicked += ok => PlaySfx(ok ? "cryptex_click" : "cryptex_stick", ok ? 4 : 2, Box.GlobalPosition, ok ? -4f : -2f);
		Box.SolvedEvent += OnSolved;
		_boxUse = new Interactable { Name = "Use", Prompt = "A puzzle box", PickRadius = 0.35f, MaxDistance = 2.2f, Position = new Vector3(0, 0.98f, 0) };
		_boxUse.Interacted += OnBoxUsed;
		_table.AddChild(_boxUse);
	}

	// ------------------------------------------------------------------ the door shuts; the knocking

	private void OnEntered(PlayerController player)
	{
		if (DoorShut || Solved || _dying) return;
		DoorShut = true;
		StationInterior.Instance?.Room2Door?.SlamShut();
		PlaySfx("door_slam", 2, ToGlobal(new Vector3(Half, 1.2f, 0)), 2f);
		_knockTimer = 5.0;
		GD.Print("[story] Act 13, room 2: the door slams shut");
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		var s = StoryManager.Instance;
		if (_knockTimer >= 0 && !Solved && !_dying)
		{
			_knockTimer -= dt;
			if (_knockTimer <= 0)
			{
				Knocks++;
				// soft, three at a time, patient
				for (int i = 0; i < 3; i++)
				{
					int n = i;
					GetTree().CreateTimer(n * 0.42).Timeout += () => PlaySfx("wall_knock", 3, ToGlobal(new Vector3(Half + 0.2f, 1.3f, 0.3f)), -14f);
				}
				_knockTimer = _rng.RandfRange(4f, 7f);
			}
		}
		if (Flooding && !_winning) Flood(dt);
		MoveDrapes(dt);
		// the scavenger staircase: only ever appears (or goes) while the player isn't in here to see it
		if (s != null && Solved && !_stepSequence)
		{
			bool want = s.HasFlag(StoryManager.Flag.StationDoor3Seen) && !s.HasFlag(StoryManager.Flag.StationStepTaken);
			bool inside = StoryBeat.Player(this) is { } p && InRoom(p.GlobalPosition);
			if (want != _stairs.Visible && !inside) { _stairs.Visible = want; _table.Visible = !want; SetStairCollision(want); }
		}
	}

	public bool InRoom(Vector3 world)
	{
		Vector3 l = ToLocal(world);
		return Mathf.Abs(l.X) < Half && Mathf.Abs(l.Z) < Half && l.Y > -1f && l.Y < Height + 1f;
	}

	// ------------------------------------------------------------------ the cryptex, the window, the flood

	private void OnBoxUsed(PlayerController player)
	{
		if (Solved || _dying || _winning) return;
		if (!WindowBroken) { _ = Cutscene.Run(this, ct => BreakWindow(player, ct), lockInput: true, freezeBody: true); return; }
		if (CryptexOverlay.Instance != null && !CryptexOverlay.Instance.IsOpen) CryptexOverlay.Instance.Open(player, Box);
	}

	/// <summary>The window cracks, then bursts: glass everywhere, the lake pouring in, the player knocked
	/// flat, and up again with the water already round their ankles.</summary>
	private async Task BreakWindow(PlayerController player, CancellationToken ct)
	{
		var rig = player.CameraRig;
		Vector3 win = ToGlobal(new Vector3(-0.4f, 1.7f, -Half));
		await Cutscene.Wait(this, 0.5, ct);
		PlaySfx("glass_crack", 1, win, 0f);
		await LookAt(player, win, 0.7f, ct);
		await Cutscene.Wait(this, 0.5, ct);
		WindowBroken = true;
		PlaySfx("glass_shatter", 1, win, 4f);
		_windowPane.Visible = false;
		_shards.Visible = true;
		foreach (var n in _shards.GetChildren())
		{
			if (n is not Node3D shard) continue;
			shard.Position = (Vector3)shard.GetMeta("home");
			var tw = CreateTween().SetParallel();
			tw.TweenProperty(shard, "position", (Vector3)shard.GetMeta("rest"), _rng.RandfRange(0.35f, 0.7f)).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			tw.TweenProperty(shard, "rotation", new Vector3(Mathf.Pi * 0.5f, _rng.RandfRange(0, 6f), 0), 0.6f);
		}
		_torrent.Visible = true;
		_torrentMat.SetShaderParameter("tint", Lake);
		_torrentMat.SetShaderParameter("flow", 1f);
		_billowTarget = 1f;
		_rush = Loop("res://assets/audio/ambient/water_torrent_loop.wav", ToGlobal(new Vector3(-0.4f, 0.6f, -Half + 0.8f)), -2f);
		LakeParts.LakeFx.Splash(Cutscene.SceneRoot(this), ToGlobal(new Vector3(-0.4f, 0.3f, -Half + 1.3f)), 3f, 40, Vector3.Back);
		Flooding = true;
		// knocked flat by it
		PlaySfx("body_thump", 2, player.GlobalPosition, 2f);
		var down = player.CreateTween().SetParallel();
		down.TweenProperty(rig, "EyeHeight", 0.35f, 0.4f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		down.TweenProperty(player, "global_position", player.GlobalPosition + ToGlobal(Vector3.Back) - GlobalPosition, 0.4f);
		double t = 0;
		while (t < 0.5)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			rig.RollSwim = Mathf.Lerp(0f, 0.55f, (float)(t / 0.5));
			rig.Shake = new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1), 0) * 0.03f * (float)(1 - t / 0.5);
		}
		rig.Shake = Vector3.Zero;
		await Cutscene.Wait(this, 1.4, ct);
		PlaySfx("breath_in", 5, player.GlobalPosition, -2f);
		var up = player.CreateTween();
		up.TweenProperty(rig, "EyeHeight", 1.62f, 1.6f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		t = 0;
		while (t < 1.6)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			rig.RollSwim = Mathf.Lerp(0.55f, 0f, Mathf.SmoothStep(0f, 1f, (float)(t / 1.6)));
		}
		rig.RollSwim = 0f;
		_boxUse.Prompt = "Work the cryptex";
		GD.Print("[story] Act 13, room 2: the window breaks - the room is flooding");
	}

	private void Flood(float dt)
	{
		Level = Mathf.Min(Height, Level + Height / FloodSeconds * dt);
		_water.Visible = Level > 0.01f;
		_water.Position = _water.Position with { Y = Level };
		if (!Blood && Level >= KneeHeight)
		{
			Blood = true;
			PlaySfx("whisper_voice", 3, ToGlobal(new Vector3(0, 1.5f, 0)), -4f);
			var tw = CreateTween();
			tw.TweenMethod(Callable.From<float>(u =>
			{
				Color c = Lake.Lerp(BloodC, u);
				_waterMat.SetShaderParameter("water_color", c);
				_waterMat.SetShaderParameter("scum_color", c.Lerp(new Color(0.5f, 0.06f, 0.05f), 0.5f));
				_torrentMat.SetShaderParameter("tint", c);
			}), 0f, 1f, 3f);
			GD.Print("[story] Act 13, room 2: the water turns to blood");
		}
		var player = StoryBeat.Player(this);
		if (player == null) return;
		// under it: the view goes red and muffled
		float eye = player.CameraRig.Camera.GlobalPosition.Y - GlobalPosition.Y;
		float under = Mathf.Clamp((Level - eye + 0.05f) / 0.15f, 0f, 1f);
		// bent over the cryptex, keep the murk light enough to read the rings through
		if (CryptexOverlay.Instance is { IsOpen: true }) under = Mathf.Min(under, 0.3f);
		UnderwaterView.For(this).Set(under, Blood ? new Color(0.3f, 0.02f, 0.02f) : Lake);
		if (Level >= Height - 0.12f && !_dying) _ = Cutscene.Run(this, ct => Drown(player, ct), lockInput: true, freezeBody: true);
	}

	private void BuildWater()
	{
		// see-through (the owner: the cryptex must stay readable under it)
		_waterMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/flood_water.gdshader") };
		_waterMat.SetShaderParameter("opacity", 0.5f);
		_waterMat.SetShaderParameter("noise_tex", ProcTextures.WaterNoise());
		_waterMat.SetShaderParameter("drain", new Vector2(99f, 99f));
		_waterMat.SetShaderParameter("water_color", Lake);
		_water = new MeshInstance3D
		{
			Name = "Flood", Mesh = new PlaneMesh { Size = new Vector2(Half * 2f - 0.1f, Half * 2f - 0.1f), SubdivideWidth = 4, SubdivideDepth = 4 },
			MaterialOverride = _waterMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false,
		};
		AddChild(_water);
	}

	/// <summary>It fills to the top. The last air goes, the red goes dark, and it's back to the checkpoint.</summary>
	private async Task Drown(PlayerController player, CancellationToken ct)
	{
		_dying = true;
		PlayerDeath.Begin();
		CryptexOverlay.Instance?.Close();
		var rig = player.CameraRig;
		var under = UnderwaterView.For(this);
		PlaySfx("underwater_thoom", 1, player.GlobalPosition, 0f);
		double t = 0;
		while (t < 3.2)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			float u = (float)(t / 3.2);
			rig.SetPitch(Mathf.Lerp(rig.Pitch, Mathf.DegToRad(60f), dt * 1.2f));   // looking up for air that isn't there
			under.Set(1f, new Color(0.3f, 0.02f, 0.02f), u * 0.9f);
		}
		await PlayerDeath.Reload(this, "You drowned.", ct);
	}

	/// <summary>STAIRS. The blood goes back the way it came, up and out through the window as if something
	/// outside drew in its breath; the glass flies back into the frame; the room is as it was; the cap
	/// slides off the cryptex.</summary>
	private void OnSolved()
	{
		if (_winning || _dying) return;
		_winning = true;
		_ = Cutscene.Run(this, WinSequence, lockInput: true, freezeBody: true);
	}

	private async Task WinSequence(CancellationToken ct)
	{
		await Cutscene.Wait(this, 0.4, ct);
		CryptexOverlay.Instance?.Close();
		var player = StoryBeat.Player(this);
		Vector3 win = ToGlobal(new Vector3(-0.4f, 1.7f, -Half));
		_knockTimer = -1;
		PlaySfx("blood_suck", 1, win, 4f);
		_rush?.Stop();
		_billowTarget = 0f;
		_torrentMat.SetShaderParameter("flow", -2.5f);
		if (player != null) _ = LookAt(player, win + Vector3.Down * 0.4f, 1.2f, ct);
		float from = Level;
		double t = 0;
		const double back = 3.6;
		while (t < back)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			float u = (float)(t / back);
			Level = Mathf.Lerp(from, 0f, Mathf.SmoothStep(0f, 1f, u));
			_water.Position = _water.Position with { Y = Level };
			_torrentMat.SetShaderParameter("opacity", 1f - Mathf.SmoothStep(0.6f, 1f, u));
			if (player != null)
			{
				float eye = player.CameraRig.Camera.GlobalPosition.Y - GlobalPosition.Y;
				UnderwaterView.For(this).Set(Mathf.Clamp((Level - eye + 0.05f) / 0.15f, 0f, 1f), new Color(0.3f, 0.02f, 0.02f));
			}
			// the glass rises off the floor and flies back into the frame
			if (u > 0.55f && _shards.Visible && !_shardsReturning) ReturnShards();
		}
		_water.Visible = false;
		_torrent.Visible = false;
		Flooding = false;
		UnderwaterView.For(this).Set(0f, Lake);
		await Cutscene.Wait(this, 0.6, ct);
		_shards.Visible = false;
		_windowPane.Visible = true;
		PlaySfx("cryptex_open", 1, Box.GlobalPosition, 0f);
		Box.SlideOpen();
		_boxUse.Enabled = false;
		await Cutscene.Wait(this, 1.0, ct);
		PlaceLighter();
		Solved = true;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationRoom2Solved);
		StationInterior.Instance?.Room2Door?.Unlock();
		GD.Print("[story] Act 13, room 2: solved - the blood goes back out of the window; the cryptex opens");
	}

	private bool _shardsReturning;

	private void ReturnShards()
	{
		_shardsReturning = true;
		foreach (var n in _shards.GetChildren())
		{
			if (n is not Node3D shard) continue;
			var tw = CreateTween().SetParallel();
			tw.TweenProperty(shard, "position", (Vector3)shard.GetMeta("home"), _rng.RandfRange(0.9f, 1.4f)).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
			tw.TweenProperty(shard, "rotation", Vector3.Zero, 1.2f);
		}
	}

	private void PlaceLighter()
	{
		var at = Box.GlobalPosition + Box.GlobalBasis.X * 0.24f + Vector3.Down * 0.06f;
		var lighter = new Pickup { Name = "Lighter", Kind = ToolKind.Lighter, UseSpot = false, SnapToSurface = false, TakenLine = "An old lighter. It still sparks." };
		AddChild(lighter);
		lighter.GlobalPosition = at;
	}

	// ------------------------------------------------------------------ the staircase (the iron door's CLIMB)

	private void BuildStairs()
	{
		_stairs = new Node3D { Name = "Staircase", Visible = false };
		AddChild(_stairs);
		var k = new MeshKit();
		k.Mat(PropTextures.DeckMat);
		const int steps = 7;
		const float rise = 0.25f, run = 0.38f, w = 1.0f;
		for (int i = 0; i < steps; i++)
		{
			float top = (i + 1) * rise, z = 1.2f - (i + 0.5f) * run;
			k.Color = new Color(0.5f, 0.48f, 0.44f) * (0.9f + 0.05f * (i % 3));
			BuildKit.Box(k, new Vector3(0, top - 0.03f, z), new Vector3(w, 0.05f, run + 0.02f), 1.4f);
			// open risers: just the stringers under each tread
		}
		k.Mat(PropTextures.PostMat);
		k.Color = new Color(0.42f, 0.4f, 0.36f);
		foreach (int s in new[] { -1, 1 })
			k.Cylinder(new Vector3(s * w * 0.5f, 0, 1.2f), new Vector3(s * w * 0.5f, steps * rise, 1.2f - steps * run), 0.05f, 0.05f, 4, true);
		// a newel post at the foot, like the one they carried through the woods
		k.Cylinder(new Vector3(-w * 0.5f - 0.05f, 0, 1.3f), new Vector3(-w * 0.5f - 0.05f, 1.1f, 1.3f), 0.06f, 0.06f, 4, true);
		k.Cylinder(new Vector3(-w * 0.5f - 0.05f, 1.1f, 1.3f), new Vector3(-w * 0.5f - 0.05f, 1.2f, 1.3f), 0.09f, 0.03f, 4, true);
		k.CommitTo(_stairs, "Stairs", true);
		var body = new StaticBody3D { Name = "StairBody", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		float len = steps * run, h = steps * rise;
		body.AddChild(new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = new Vector3(w, 0.1f, Mathf.Sqrt(len * len + h * h)) },
			Transform = new Transform3D(new Basis(Vector3.Right, Mathf.Atan2(h, len)), new Vector3(0, h * 0.5f - 0.05f, 1.2f - len * 0.5f)),
		});
		_stairs.AddChild(body);
		SetStairCollision(false);
		_stairTop = StoryBeat.MakeTrigger(_stairs, new BoxShape3D { Size = new Vector3(w, 1.2f, 0.5f) }, new Vector3(0, h + 0.6f, 1.2f - len + 0.2f), OnClimbed, "StairTop");
	}

	private void SetStairCollision(bool on)
	{
		foreach (var c in _stairs.GetNode("StairBody").GetChildren())
			if (c is CollisionShape3D cs) cs.SetDeferred(CollisionShape3D.PropertyName.Disabled, !on);
		foreach (var c in _table.GetNode("TableBody").GetChildren())
			if (c is CollisionShape3D cs) cs.SetDeferred(CollisionShape3D.PropertyName.Disabled, on);
	}

	private void OnClimbed(PlayerController player)
	{
		if (_stepSequence || !_stairs.Visible || StoryManager.Instance?.HasFlag(StoryManager.Flag.StationStepTaken) == true) return;
		_stepSequence = true;
		_ = Cutscene.Run(this, ct => StepSequence(player, ct), lockInput: true, freezeBody: true);
	}

	private async Task StepSequence(PlayerController player, CancellationToken ct)
	{
		var fader = StoryBeat.Fader(this);
		PlaySfx("trunk_creak", 3, player.GlobalPosition, 0f);
		await Cutscene.Wait(this, 0.5, ct);
		if (fader != null) await fader.Fade(1f, 0.6f, ct);
		await Cutscene.Wait(this, 0.8, ct);
		// a knock, right behind their head
		Vector3 behind = player.GlobalPosition + player.CameraRig.Camera.GlobalBasis.Z * 0.6f + Vector3.Up * 1.6f;
		PlaySfx("wall_knock", 3, behind, 6f);
		await Cutscene.Wait(this, 1.3, ct);
		_stairs.Visible = false;
		SetStairCollision(false);
		_table.Visible = false;
		player.Teleport(StairFootWorld, player.CameraRig.Yaw);
		player.CameraRig.SetPitch(Mathf.DegToRad(-35f));
		player.Inventory?.TryPickup(ToolKind.StairTread);
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationStepTaken);
		PlaySfx("body_thump", 2, player.GlobalPosition, -4f);
		if (fader != null) await fader.Fade(0f, 1.4f, ct);
		await StoryBeat.Caption(this, "You're on the floor. In your hands, one of the steps.", 0.4f, 2.6f, 1f, ct);
		GD.Print("[story] Act 13: the step is taken from the staircase in room 2");
		_stepSequence = false;
	}

	// ------------------------------------------------------------------ helpers

	private async Task LookAt(PlayerController player, Vector3 target, float seconds, CancellationToken ct)
	{
		var rig = player.CameraRig;
		double t = 0;
		while (t < seconds)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			Vector3 to = target - rig.Camera.GlobalPosition;
			float k = 1f - Mathf.Exp(-4f * dt);
			player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.AngleDifference(rig.Yaw, Mathf.Atan2(-to.X, -to.Z)) * k, 0));
			rig.SetPitch(Mathf.Lerp(rig.Pitch, Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length()), k));
		}
	}

	private void PlaySfx(string name, int variants, Vector3 at, float db)
	{
		string path = $"res://assets/audio/sfx/{name}_{_rng.RandiRange(1, variants):00}.wav";
		if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}.wav";
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = db, UnitSize = 3f, MaxDistance = 25f, PitchScale = _rng.RandfRange(0.95f, 1.05f) };
		Cutscene.SceneRoot(this).AddChild(s);
		s.GlobalPosition = at;
		s.Finished += s.QueueFree;
		s.Play();
	}

	private AudioStreamPlayer3D Loop(string path, Vector3 at, float db)
	{
		if (!ResourceLoader.Exists(path)) return null;
		var stream = GD.Load<AudioStream>(path);
		if (stream is AudioStreamWav wav)
		{
			wav = (AudioStreamWav)wav.Duplicate();
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
			stream = wav;
		}
		var p = new AudioStreamPlayer3D { Stream = stream, Bus = "Events", VolumeDb = db, UnitSize = 5f, MaxDistance = 30f };
		AddChild(p);
		p.GlobalPosition = at;
		p.Play();
		return p;
	}
}
