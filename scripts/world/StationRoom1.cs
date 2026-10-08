using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;
using ProjectDS.World.StationParts;

namespace ProjectDS.World;

/// <summary>
/// Act 13, Room 1 (left of the lobby's desk; the clock's key opens it). Three walls of hurried,
/// scrawled red writing, huge, over the whole of each wall, one sentence to a wall — DO NOT LOOK AT THEM / DO NOT TOUCH THEM / NEVER GO UP
/// THEM — running with drips, and a duct-taped cigar box on the floor. The knife cuts its three sides
/// (left, right, then the front: <see cref="TapeCutOverlay"/>), the lid comes up on a button, and
/// pressing it (E) melts the writing down the walls into a blood puddle at each wall's foot. That is
/// Act 13's middle checkpoint (<see cref="Checkpoint.Act13Room1Solved"/>) and it opens Room 2.
///
/// Later, for the iron door's TOUCH: one of the three puddles has something in it. The writing is
/// gone, but a ghost of each line is still just legible on its wall; the hand is in the puddle under
/// DO NOT TOUCH THEM. Reach into either of the others and something in the blood takes the player's
/// wrist, holds it a moment, and lets go.
///
/// Local space: floor y=0, the room x/z in [-Half, Half]; its doorway is the -X wall's gap at z=0,
/// which lines up with the lobby's +X wall.
/// </summary>
public partial class StationRoom1 : Node3D
{
	public const float Half = 2.8f, Height = 3.0f;

	public const string Wall1 = "DO NOT LOOK\nAT THEM";
	public const string Wall2 = "DO NOT TOUCH\nTHEM";
	public const string Wall3 = "NEVER GO\nUP THEM";

	public int BoxCutsDone { get; private set; }
	public bool BoxOpen { get; private set; }
	public bool Solved { get; private set; }
	/// <summary>For tests: the three puddles' reach points (0: LOOK wall, 1: TOUCH wall, 2: UP wall).</summary>
	public Interactable[] Puddles { get; } = new Interactable[3];
	public int WrongReaches { get; private set; }

	/// <summary>Per wall: the writing (a node holding its scrawled lines), its lines, and its faint ghost.</summary>
	private readonly List<Node3D> _writing = new();
	private readonly List<List<Label3D>> _lines = new();
	private readonly List<Node3D> _ghosts = new();
	/// <summary>Per wall: its drips, each a run of blood from under a letter and the bead at its end.</summary>
	private readonly List<List<Drip>> _drips = new();
	private readonly List<Node3D> _puddleDecals = new();

	private sealed class Drip
	{
		public MeshInstance3D Run, Bead;
		public Vector3 Top, N;
		public float Len, Width;
	}

	/// <summary>How each wall's words are broken over it: three lines, filling the wall.</summary>
	private static readonly string[][] Scrawls =
	{
		new[] { "DO NOT", "LOOK AT", "THEM" }, new[] { "DO NOT", "TOUCH", "THEM" }, new[] { "NEVER", "GO UP", "THEM" },
	};
	private Node3D _box, _lid, _button;
	private Interactable _boxUse, _buttonUse;
	private Transform3D _leftCam, _rightCam, _frontCam;
	private MeshInstance3D[] _tapes;
	private static Font _scrawl;

	/// <summary>The three walls' feet (local) and their inward normals: 0 = +Z (LOOK), 1 = +X (TOUCH), 2 = -Z (UP).</summary>
	private static readonly (Vector3 foot, Vector3 n)[] Walls =
	{
		(new Vector3(0, 0, Half), Vector3.Forward), (new Vector3(Half, 0, 0), Vector3.Left), (new Vector3(0, 0, -Half), Vector3.Back),
	};

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private static Font Scrawl => _scrawl ??= new SystemFont
	{
		FontNames = new[] { "Chiller", "Ink Free", "Segoe Print", "Comic Sans MS", "serif" },
		FontWeight = 700,
	};

	private void Build()
	{
		using var __timer = Systems.BuildTimer.Time("StationRoom1");
		var s = StoryManager.Instance;
		var k = new MeshKit();
		var floorK = new MeshKit();
		var ceilK = new MeshKit();
		var body = new StaticBody3D { Name = "Walls", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		AddChild(body);
		k.Mat(StationTextures.WallpaperStainedMat);
		k.Color = new Color(0.85f, 0.8f, 0.75f);
		// the wall shared with the lobby: a thin skin just inside the lobby's own wall (never in the same
		// plane, or the two wallpapers fight), no collision of its own (the lobby's wall has it)
		StationKit.WallAlongZ(k, null, -Half + 0.085f, -Half, Half, Height, 0, (0f, 2.2f), 2.2f, 0.02f, trimSide: 1);
		StationKit.WallAlongX(k, body, Half, -Half, Half, Height, 0, null, trimSide: -1);
		StationKit.WallAlongX(k, body, -Half, -Half, Half, Height, 0, null, trimSide: 1);
		StationKit.WallAlongZ(k, body, Half, -Half, Half, Height, 0, null, trimSide: -1);
		floorK.Mat(BuildingTextures.FloorMat);
		floorK.Color = new Color(0.45f, 0.4f, 0.34f);
		ceilK.Mat(BuildingTextures.BoardsMat);
		ceilK.Color = new Color(0.3f, 0.28f, 0.25f);
		StationKit.FloorAndCeiling(floorK, ceilK, body, Half, Half, Height, 0);
		k.Color = Colors.White;
		k.CommitTo(this, "Walls");
		// webs up in its corners (the interiors pass, 2026-10-03)
		WebKit.DressRoom(this, new RandomNumberGenerator { Seed = 1311 }, -Half + 0.1f, Half - 0.07f, -Half + 0.07f, Half - 0.07f, 0f, Height, 0.9f, 0.75f);
		DebrisKit.Scatter(this, new RandomNumberGenerator { Seed = 1312 }, -Half + 0.1f, Half - 0.07f, -Half + 0.07f, Half - 0.07f, 0f, new Color(0.62f, 0.58f, 0.5f), 50, 3,
			new[] { (new Vector3(-Half, 0, 0), 1.1f), (Vector3.Zero, 1.3f) });
		floorK.Color = Colors.White;
		floorK.CommitTo(this, "Floor");
		ceilK.Color = Colors.White;
		ceilK.CommitTo(this, "Ceiling", false);
		AddChild(new OmniLight3D
		{
			Name = "RoomLamp", LightColor = new Color(0.9f, 0.62f, 0.5f), LightEnergy = 1.3f,
			OmniRange = 7f, OmniAttenuation = 1.2f, Position = new Vector3(0, Height - 0.3f, 0),
		});

		// the writing (the owner: scrawled over the whole wall): three huge, hurried lines to a wall, each a
		// little crooked, with blood running from under the letters
		var red = new Color(0.62f, 0.03f, 0.02f);
		var rng = new RandomNumberGenerator { Seed = 1331 };
		float[] lineY = { 2.42f, 1.52f, 0.66f };
		for (int i = 0; i < 3; i++)
		{
			var (foot, n) = Walls[i];
			Vector3 right = Vector3.Up.Cross(n).Normalized();
			var wall = new Node3D { Name = $"Writing{i}" };
			var ghost = new Node3D { Name = $"Ghost{i}", Visible = false };
			AddChild(wall);
			AddChild(ghost);
			var labels = new List<Label3D>();
			var drips = new List<Drip>();
			for (int li = 0; li < 3; li++)
			{
				string line = Scrawls[i][li];
				float target = line.Length <= 4 ? rng.RandfRange(3.0f, 3.8f) : rng.RandfRange(4.4f, 5.1f);
				float px = Mathf.Max(1f, Scrawl.GetStringSize(line, HorizontalAlignment.Left, -1, 96).X);
				float em = Mathf.Min(target * 96f / px, 1.15f);
				float width = px * em / 96f;
				float shift = rng.RandfRange(-0.25f, 0.25f);
				var b = Basis.LookingAt(-n, Vector3.Up) * new Basis(Vector3.Back, rng.RandfRange(-0.07f, 0.07f));
				Vector3 at = foot + n * 0.075f + right * shift + Vector3.Up * lineY[li];
				labels.Add(Scrawled(wall, line, at, b, em, red));
				// a second pass of the brush, a hair off the first: thick, smeared strokes
				labels.Add(Scrawled(wall, line, at + n * 0.001f + right * em * 0.02f + Vector3.Down * em * 0.025f, b, em, new Color(0.45f, 0.02f, 0.015f, 0.6f)));
				Scrawled(ghost, line, at - n * 0.002f, b, em, new Color(0.35f, 0.08f, 0.06f, 0.14f));
				// drips from under the letters
				int count = 3 + (int)(width * 1.4f);
				for (int d = 0; d < count; d++)
				{
					Vector3 top = at + right * rng.RandfRange(-width * 0.45f, width * 0.45f) + Vector3.Down * em * rng.RandfRange(0.22f, 0.34f);
					top.Y = Mathf.Max(top.Y, 0.2f);
					float len = Mathf.Min(rng.RandfRange(0.06f, 0.42f), top.Y - 0.05f);
					var drip = new Drip { Top = top, N = n, Len = len, Width = rng.RandfRange(0.03f, 0.06f) };
					drip.Run = StationProps.Decal(this, StationTextures.BloodDripMat, top, n, new Vector2(drip.Width, 1f), 0f, $"Drip{i}_{li}_{d}");
					drip.Bead = StationProps.Decal(this, StationTextures.BloodMat, top, n, Vector2.One * drip.Width * 1.5f, rng.RandfRange(0, 3f), "Bead");
					SetDrip(drip, len);
					drips.Add(drip);
				}
			}
			_writing.Add(wall);
			_lines.Add(labels);
			_ghosts.Add(ghost);
			_drips.Add(drips);
		}

		BuildBox(new Vector3(0.3f, 0, 0.2f));
		BuildPuddles();

		// Continue
		if (s != null && s.HasFlag(StoryManager.Flag.StationRoom1Solved))
		{
			Solved = true;
			BoxOpen = true;
			BoxCutsDone = 3;
			foreach (var t in _tapes) t.Visible = false;
			_lid.Position += Vector3.Up * 0.16f;
			_lid.Rotation = new Vector3(Mathf.DegToRad(-70f), 0, 0);
			_boxUse.Enabled = false;
			foreach (var w in _writing) w.Visible = false;
			// the runs stay on the walls, all the way down to the pools
			foreach (var wall in _drips) foreach (var d in wall) { SetDrip(d, d.Top.Y - 0.01f); d.Bead.Visible = false; }
			foreach (var g in _ghosts) g.Visible = true;
			foreach (var p in _puddleDecals) { p.Visible = true; p.Scale = Vector3.One; }
		}
		SetProcess(true);
	}

	/// <summary>A drip's run from its top down <paramref name="len"/> metres, the bead at its foot.</summary>
	private static void SetDrip(Drip d, float len)
	{
		len = Mathf.Max(len, 0.01f);
		d.Run.Scale = new Vector3(1f, len, 1f);
		d.Run.Position = d.Top + d.N * 0.006f + Vector3.Down * len * 0.5f;
		d.Bead.Position = d.Top + d.N * 0.007f + Vector3.Down * len;
	}

	private Label3D Scrawled(Node3D parent, string text, Vector3 at, Basis b, float em, Color c)
	{
		var l = new Label3D
		{
			Text = text, Font = Scrawl, FontSize = 96, PixelSize = em / 96f, Modulate = c, OutlineSize = 0,
			Shaded = true, AlphaCut = c.A < 0.99f ? Label3D.AlphaCutMode.Disabled : Label3D.AlphaCutMode.Discard,
			Transform = new Transform3D(b, at), DoubleSided = false, LineSpacing = -10f,
		};
		parent.AddChild(l);
		return l;
	}

	public override void _Process(double delta)
	{
		var s = StoryManager.Instance;
		bool hunt = Solved && s != null && s.HasFlag(StoryManager.Flag.StationDoor3Seen) && !s.HasFlag(StoryManager.Flag.StationHandTaken);
		foreach (var p in Puddles) if (p != null) p.Enabled = hunt;
	}

	// ------------------------------------------------------------------ the cigar box

	private void BuildBox(Vector3 at)
	{
		_box = new Node3D { Name = "CigarBox", Position = at, Rotation = new Vector3(0, 0.35f, 0) };
		AddChild(_box);
		const float hw = 0.22f, hd = 0.14f, hh = 0.075f;
		var k = new MeshKit();
		k.Mat(BuildingTextures.BoardsMat);
		k.Color = new Color(0.4f, 0.26f, 0.14f);
		BuildKit.Box(k, new Vector3(0, hh, 0), new Vector3(hw * 2f, hh * 2f, hd * 2f), 3f, BuildKit.Face.PY);
		// a paper band round it, gold and red, the brand long gone
		k.Mat(StationTextures.Flat("st_cigarband", new Color(0.7f, 0.5f, 0.2f), 0.6f, 0.4f));
		k.Color = Colors.White;
		BuildKit.Box(k, new Vector3(0, hh * 1.2f, hd + 0.002f), new Vector3(hw * 1.2f, hh * 0.5f, 0.004f));
		k.CommitTo(_box, "BoxMesh", true);

		_lid = new Node3D { Name = "Lid", Position = new Vector3(0, hh * 2f, -hd) };
		_box.AddChild(_lid);
		var lk = new MeshKit();
		lk.Mat(BuildingTextures.BoardsMat);
		lk.Color = new Color(0.42f, 0.27f, 0.15f);
		BuildKit.Box(lk, new Vector3(0, 0, hd), new Vector3(hw * 2f + 0.01f, 0.015f, hd * 2f + 0.01f), 3f);
		lk.Color = Colors.White;
		lk.CommitTo(_lid, "LidMesh", true);

		var tapeMat = BuildingTextures.Plain("s_tape2", new Color(0.62f, 0.6f, 0.56f), 0.6f);
		_tapes = new MeshInstance3D[3];
		void Tape(int i, Vector3 pos, Vector3 size)
		{
			var tk = new MeshKit();
			tk.Mat(tapeMat);
			tk.Color = Colors.White;
			tk.Box(pos, size, 3f);
			_tapes[i] = tk.CommitTo(_box, $"Tape{i}", false);
		}
		Tape(0, new Vector3(-hw - 0.004f, hh * 1.4f, 0), new Vector3(0.012f, hh * 1.4f, hd * 1.9f));
		Tape(1, new Vector3(hw + 0.004f, hh * 1.4f, 0), new Vector3(0.012f, hh * 1.4f, hd * 1.9f));
		Tape(2, new Vector3(0, hh * 1.4f, hd + 0.006f), new Vector3(hw * 1.9f, hh * 1.4f, 0.012f));

		var body = new StaticBody3D { Name = "BoxBody", CollisionLayer = 1, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, hh, 0), Shape = new BoxShape3D { Size = new Vector3(hw * 2f, hh * 2f, hd * 2f) } });
		_box.AddChild(body);

		_boxUse = new PickupInteractable
		{
			Name = "Use", Prompt = "A cigar box, taped shut", PickRadius = 0.45f, MaxDistance = 2.2f, Position = new Vector3(0, hh, 0),
			PromptFor = p => p?.Inventory is { } inv && inv.HasTool(ToolKind.Knife) ? "Cut the tape" : "A cigar box, taped shut",
		};
		_boxUse.Interacted += OnBoxUsed;
		_box.AddChild(_boxUse);

		Transform3D Look(Vector3 focusLocal, Vector3 eyeOffsetLocal)
		{
			Vector3 focus = _box.ToGlobal(focusLocal), eye = _box.ToGlobal(focusLocal + eyeOffsetLocal);
			return new Transform3D(Basis.LookingAt(focus - eye, Vector3.Up), eye);
		}
		_leftCam = Look(new Vector3(-hw, hh, 0), new Vector3(-0.38f, 0.18f, 0.12f));
		_rightCam = Look(new Vector3(hw, hh, 0), new Vector3(0.38f, 0.18f, 0.12f));
		_frontCam = Look(new Vector3(0, hh, hd), new Vector3(0, 0.22f, 0.45f));
	}

	private void OnBoxUsed(PlayerController player)
	{
		if (BoxOpen || TapeCutOverlay.Instance == null) return;
		if (player?.Inventory is not { } inv || !inv.HasTool(ToolKind.Knife)) return;
		var cuts = new List<TapeCutOverlay.CutSpec>
		{
			new() { CameraView = _leftCam, Direction = Vector2.Down, PixelsNeeded = 220f,
				OnProgress = p => { if (IsInstanceValid(_tapes[0])) _tapes[0].Scale = new Vector3(1f, 1f, 1f - p); }, OnCut = () => CutSide(0) },
			new() { CameraView = _rightCam, Direction = Vector2.Down, PixelsNeeded = 220f,
				OnProgress = p => { if (IsInstanceValid(_tapes[1])) _tapes[1].Scale = new Vector3(1f, 1f, 1f - p); }, OnCut = () => CutSide(1) },
			new() { CameraView = _frontCam, Direction = Vector2.Right, PixelsNeeded = 220f,
				OnProgress = p => { if (IsInstanceValid(_tapes[2])) _tapes[2].Scale = new Vector3(1f - p, 1f, 1f); }, OnCut = () => CutSide(2) },
		};
		TapeCutOverlay.Instance.Open(player, cuts, OpenBox);
	}

	private void CutSide(int i)
	{
		BoxCutsDone++;
		if (IsInstanceValid(_tapes[i])) _tapes[i].Visible = false;
	}

	private void OpenBox()
	{
		BoxOpen = true;
		_boxUse.Enabled = false;
		var tween = CreateTween();
		tween.TweenProperty(_lid, "rotation:x", Mathf.DegToRad(-100f), 0.9f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		PlayOneShot("res://assets/audio/sfx/door_creak_01.wav", 0f, 1.8f);

		_button = new Node3D { Name = "Button", Position = new Vector3(0, 0.06f, 0) };
		_box.AddChild(_button);
		var bk = new MeshKit();
		bk.Mat(StationTextures.Flat("st_velvet", new Color(0.25f, 0.03f, 0.05f), 1f, 0.05f));
		bk.Color = Colors.White;
		BuildKit.Box(bk, new Vector3(0, -0.02f, 0), new Vector3(0.4f, 0.02f, 0.24f));
		bk.Mat(BuildingTextures.IronMat);
		bk.Color = new Color(0.25f, 0.24f, 0.22f);
		bk.Cylinder(new Vector3(0, -0.01f, 0), new Vector3(0, 0.012f, 0), 0.05f, 0.05f, 12, true);
		bk.Mat(StationTextures.Glow("st_button_red", new Color(0.6f, 0.04f, 0.03f), 0.4f));
		bk.Color = Colors.White;
		bk.Cylinder(new Vector3(0, 0.012f, 0), new Vector3(0, 0.03f, 0), 0.036f, 0.036f, 12, true);
		bk.CommitTo(_button, "ButtonMesh", true);
		_buttonUse = new Interactable { Name = "Press", Prompt = "Press the button", PickRadius = 0.3f, MaxDistance = 2f, Position = new Vector3(0, 0.03f, 0) };
		_buttonUse.Interacted += OnButtonPressed;
		_button.AddChild(_buttonUse);
		GD.Print("[story] Act 13, room 1: the box is open on a button");
	}

	private void OnButtonPressed(PlayerController player)
	{
		if (Solved) return;
		Solved = true;
		_buttonUse.Enabled = false;
		_button.Position = _button.Position with { Y = _button.Position.Y - 0.012f };
		PlayOneShot("res://assets/audio/sfx/radio_tick_01.wav", 2f, 0.6f);
		MeltWalls();
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationRoom1Solved);
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act13Room1Solved);
		GD.Print("[story] Act 13, room 1: solved - the writing melts");
	}

	/// <summary>The writing drips off the walls (the owner): every run of blood lets go and races down to the
	/// floor, the words sag, stretch and slide down after them and thin to nothing, and the blood pools
	/// wide at each wall's foot. The runs stay as stains; a faint ghost of the words stays on each wall.</summary>
	private void MeltWalls()
	{
		PlayOneShot("res://assets/audio/sfx/squelch_open_01.wav", 0f, 0.6f);
		var rng = new RandomNumberGenerator { Seed = 1333 };
		for (int i = 0; i < 3; i++)
		{
			var wall = _writing[i];
			var puddle = _puddleDecals[i];
			float delay = i * 0.5f;
			var (foot, n) = Walls[i];
			PlayOneShotAt("res://assets/audio/sfx/blood_drain_01.wav", foot + n * 0.3f + Vector3.Up * 1.2f, -3f, 0.8f + i * 0.08f, delay + 0.3f);
			var tw = CreateTween().SetParallel();
			// each run lets go and runs down to the floor, faster as it goes, its bead at its foot
			foreach (var d in _drips[i])
			{
				var drip = d;
				float from = drip.Len, to = drip.Top.Y - 0.01f;
				float start = delay + rng.RandfRange(0f, 1.8f), time = rng.RandfRange(1.6f, 3.6f) * Mathf.Sqrt(Mathf.Max(to, 0.1f) / 2f);
				tw.TweenMethod(Callable.From<float>(len => SetDrip(drip, len)), from, to, time).SetDelay(start).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
				tw.TweenCallback(Callable.From(() => drip.Bead.Visible = false)).SetDelay(start + time);
			}
			// the words sag and slide down after them, stretching, and thin away
			tw.TweenProperty(wall, "position:y", -0.9f, 5.5f).SetDelay(delay + 0.6f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			tw.TweenProperty(wall, "scale", new Vector3(1.03f, 1.5f, 1f), 5.5f).SetDelay(delay + 0.6f);
			foreach (var l in _lines[i])
				tw.TweenProperty(l, "modulate:a", 0f, 5.5f).SetDelay(delay + 0.6f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.In);
			puddle.Visible = true;
			puddle.Scale = Vector3.One * 0.05f;
			tw.TweenProperty(puddle, "scale", Vector3.One, 4.5f).SetDelay(delay + 1.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
			var ghost = _ghosts[i];
			tw.TweenCallback(Callable.From(() => { ghost.Visible = true; wall.Visible = false; })).SetDelay(delay + 6.2f);
		}
	}

	// ------------------------------------------------------------------ the puddles (the iron door's TOUCH)

	private void BuildPuddles()
	{
		var rng = new RandomNumberGenerator { Seed = 1332 };
		for (int i = 0; i < 3; i++)
		{
			var (foot, n) = Walls[i];
			// wide pools along each wall's foot (the owner: larger), a few smaller splashes round them
			var root = new Node3D { Name = $"Puddle{i}", Position = foot + n * 0.72f, Visible = false };
			AddChild(root);
			float along = i == 1 ? Mathf.Pi * 0.5f : 0f;
			StationProps.Decal(root, StationTextures.BloodPoolMat, new Vector3(0, 0.008f, 0), Vector3.Up, new Vector2(rng.RandfRange(3.2f, 3.8f), 1.45f), along + rng.RandfRange(-0.15f, 0.15f));
			Vector3 right = Vector3.Up.Cross(n).Normalized();
			for (int b = 0; b < 4; b++)
			{
				Vector3 off = right * rng.RandfRange(-2.0f, 2.0f) + n * rng.RandfRange(-0.3f, 0.9f);
				StationProps.Decal(root, StationTextures.BloodMat, off + new Vector3(0, 0.009f + b * 0.0008f, 0), Vector3.Up, Vector2.One * rng.RandfRange(0.45f, 0.95f), rng.RandfRange(0, 3f));
			}
			_puddleDecals.Add(root);
			int which = i;
			var reach = new Interactable
			{
				Name = "Reach", Prompt = "Reach into the blood", HoldSeconds = 1.2f, PickRadius = 0.45f, MaxDistance = 2.4f,
				Position = foot + n * 0.55f + Vector3.Up * 0.05f, Enabled = false,
			};
			reach.Interacted += p => OnReach(p, which);
			AddChild(reach);
			Puddles[i] = reach;
		}
	}

	private void OnReach(PlayerController player, int which)
	{
		var s = StoryManager.Instance;
		if (s == null || s.HasFlag(StoryManager.Flag.StationHandTaken)) return;
		if (which == 1)
		{
			// DO NOT TOUCH THEM: here it is
			PlayOneShot("res://assets/audio/sfx/squelch_open_02.wav", 2f, 0.8f);
			player?.Inventory?.TryPickup(ToolKind.PaleHand);
			s.SetFlag(StoryManager.Flag.StationHandTaken);
			_ = StoryBeat.Caption(this, "A hand. Cold. Its fingers close round yours.", 0.4f, 2.6f, 1f);
			GD.Print("[story] Act 13: the pale hand is taken from the blood");
			return;
		}
		WrongReaches++;
		_ = Cutscene.Run(this, async ct =>
		{
			PlayOneShot("res://assets/audio/sfx/squelch_close_02.wav", 3f, 0.7f);
			PlayOneShot($"res://assets/audio/sfx/whisper_voice_0{1 + WrongReaches % 3}.wav", -2f, 0.8f);
			var rig = player.CameraRig;
			float from = rig.Pitch;
			// yanked down toward it, held, let go
			double t = 0;
			while (t < 0.9)
			{
				await Cutscene.Frame(this, ct);
				t += GetProcessDeltaTime();
				float u = (float)(t / 0.9);
				rig.SetPitch(Mathf.Lerp(from, Mathf.DegToRad(-70f), Mathf.Sin(Mathf.Min(1f, u * 2.2f) * Mathf.Pi * 0.5f)));
				rig.Shake = new Vector3(Mathf.Sin(u * 40f), Mathf.Cos(u * 33f), 0) * 0.015f * (1f - u);
			}
			rig.Shake = Vector3.Zero;
			await StoryBeat.Caption(this, "Something in it takes your wrist - and lets go.", 0.3f, 1.8f, 0.8f, ct);
		}, lockInput: true);
	}

	private void PlayOneShotAt(string path, Vector3 local, float volumeDb, float pitch, float delay)
	{
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = volumeDb, PitchScale = pitch, UnitSize = 2f, MaxDistance = 15f, Position = local };
		AddChild(s);
		s.Finished += s.QueueFree;
		GetTree().CreateTimer(delay).Timeout += () => { if (IsInstanceValid(s)) s.Play(); };
	}

	private void PlayOneShot(string path, float volumeDb, float pitch)
	{
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = volumeDb, PitchScale = pitch, UnitSize = 2f, MaxDistance = 15f };
		AddChild(s);
		s.Finished += s.QueueFree;
		s.Play();
	}
}
