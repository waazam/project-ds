using System.Collections.Generic;
using Godot;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 1's beats along the walks (2026-10-24, the owner: "do all 12 changes" - the opening's pacing). Something to aim
/// for, something to feel and something visibly wrong on every walk:
/// <list type="bullet">
/// <item><b>The poster</b>: a missing hiker pinned over the trailhead board's map from the start (R.H., the name run in
/// the rain: STORY's "a missing hiker, 41, last seen at the Blackfern trailhead on October 25 ... a green pack and a
/// camera round his neck").</item>
/// <item><b>The shot list</b>: pencilled under the assignment, one shot per walk (the valley from the overlook, the falls,
/// the trail at last light), ticked off as they're taken; the pinning reads them back, the board's light swelling.</item>
/// <item><b>Walk 1, wrong</b>: a doe on the Ridge Path that doesn't run (<see cref="Deer.Stares"/>); R.H.'s thermos on the
/// overlook's platform, still warm; on the way back two sets of boot prints on the trail below the bridge, his own and
/// another right beside them, step for step; and the register at the lot with its last page torn out, the page lying on
/// the cabin's table, a line added in the same hand: Cameron's name.</item>
/// <item><b>Walk 1, felt</b>: a plank cracks under him on the way back over the footbridge (nothing gives); and the climb
/// beside the falls is wet rock, edged along slowly, a foot going once.</item>
/// <item><b>Between</b>: out of the cabin after the first pinning without pinning again, the thought sends him back.</item>
/// </list>
/// Each is saved (a flag), so a Continue puts back what had happened.
/// </summary>
public partial class Act1Walks
{
	public const string ShotOverlookFlag = "act1_shot_overlook", ShotDuskFlag = "act1_shot_dusk";
	public const string PrintsFlag = "act1_boot_prints", RegisterTornFlag = "act1_register_torn";
	public const string PlankFlag = "act1_plank_cracked", SlipFlag = "act1_falls_slip";
	private const string OverlookFlag = "act1_overlook_reached";
	public const string StrangerFlag = "act1_board_stranger", KnockFlag = "act1_cabin_knock";

	/// <summary>For tests: what's out there, and what has happened.</summary>
	public Readable Poster { get; private set; }
	public Readable Thermos { get; private set; }
	public Deer StaringDeer { get; private set; }
	public int BootPrints { get; private set; }
	public bool PrintsThought { get; private set; }
	public Readable TornPage { get; private set; }
	public bool OverlookReached { get; private set; }
	public bool PlankCracked { get; private set; }
	public bool Slipped { get; private set; }
	public float WetRockDrag { get; private set; } = 1f;
	public bool BoardNudged { get; private set; }
	public string LastPinLine { get; private set; } = "";
	/// <summary>For tests: the print nobody pinned is up (and its thought), the knock, the porch prints and their thought.</summary>
	public bool StrangerThought { get; private set; }
	public bool Knocked { get; private set; }
	public int PorchPrints { get; private set; }
	public bool PorchThought { get; private set; }
	/// <summary>For tests: where the boot prints run (trail metres) and where the wet rock is (falls path metres).
	/// (2026-10-29: the cabin up the trail now, at about 140 m: the prints between it and the bridge, the way he walks out)</summary>
	public const float PrintsFrom = 146f, PrintsTo = 178f;
	public float WetFrom => FallsSplitAt + 2f;
	public float WetTo => FallsSplitAt + 16f;
	/// <summary>For tests: the whole falls path (metres from its trail end), as the wet rock's metres count.</summary>
	public Polyline2 FallsLine => _falls;

	private bool _beatsReady;
	private Cabin _hub;
	private Readable _register;
	private Node3D _bridge;
	private double _plankT = -1, _slipT = -1;
	private float _wetSway;
	private bool _capturing;
	private double _stage2At = -1;
	private bool _swaying;

	private static bool Flag(string f) => StoryManager.Instance?.HasFlag(f) ?? false;
	private static void SetStoryFlag(string f) => StoryManager.Instance?.SetFlag(f);

	/// <summary>Once the level's things are in (the cabin's story props, the register): the beats that stand from the start.</summary>
	private void SetupBeats()
	{
		_beatsReady = true;
		_hub = GetTree().GetFirstNodeInGroup(Cabin.HubGroup) as Cabin;
		_bridge = GetTree().GetFirstNodeInGroup("bridge_marker") as Node3D;
		OverlookReached = Flag(OverlookFlag);
		PlankCracked = Flag(PlankFlag);
		Slipped = Flag(SlipFlag);
		// (the level may sit under a host scene: found by name)
		var trailhead = GetTree().CurrentScene?.FindChild("Trailhead", true, false);
		if (trailhead?.GetNodeOrNull<Node3D>("InfoBoard") is { } board) BuildPoster(board);
		if (trailhead?.GetNodeOrNull<Node3D>("TrailRegister") is { } reg)
			foreach (var n in reg.FindChildren("*", "", true, false))
				if (n is Readable r) { _register = r; break; }
		if (_hub?.AssignmentSheet is { } sheet) sheet.Text = ShotListText();
		if (_hub?.Board is { } b) b.Pinned += OnPinned;
		CameraTool.PhotoTaken += OnBeatPhoto;
		if (Stage == 1 && !Flag(Deer.StaredFlag)) PlaceStaringDeer();
		if (Flag(PrintsFlag)) LayBootPrints();
		if (Flag(RegisterTornFlag)) TearRegister();
		if (Flag(KnockFlag)) { Knocked = true; LayPorchPrints(); }
		BuildWetRocks();
		SetupAlarm();
	}

	private void ExitBeats()
	{
		CameraTool.PhotoTaken -= OnBeatPhoto;
		if (_hub != null && IsInstanceValid(_hub) && _hub.Board != null) _hub.Board.Pinned -= OnPinned;
	}

	private void UpdateBeats(PlayerController player, float dt)
	{
		if (!_beatsReady) { if (_clock > 0.5) SetupBeats(); else return; }
		if (Thermos == null && Platform != null) PlaceThermos();
		if (_builtStage >= 3 && StaringDeer != null && IsInstanceValid(StaringDeer)) { StaringDeer.QueueFree(); StaringDeer = null; }
		var pp = player.GlobalPosition;
		var q = new Vector2(pp.X, pp.Z);
		float s = TrailS(q, out float offT);
		bool away = ReachedFalls || OverlookReached;
		UpdateAlarm(player, s, offT);
		// the overlook reached (its platform, or the spur's far end)
		if (!OverlookReached && Platform != null && pp.DistanceTo(Platform.GlobalPosition) < 9f)
		{
			OverlookReached = true;
			SetStoryFlag(OverlookFlag);
			GD.Print("[story] Act 1 walk 1: at the overlook");
		}
		if (_builtStage <= 2)
		{
			// while he's well up the trail or off at the falls: the prints laid below the bridge, the register's page gone
			bool farFromLot = (s > PrintsTo + 45f && offT < 60f) || offT > 40f;
			if (away && farFromLot && !Flag(PrintsFlag)) { SetStoryFlag(PrintsFlag); LayBootPrints(); }
			if (away && farFromLot && !Flag(RegisterTornFlag)) { SetStoryFlag(RegisterTornFlag); TearRegister(); }
			// coming back down over them: the thought, once
			if (!PrintsThought && BootPrints > 0 && offT < 3f && s > PrintsFrom + 6f && s < PrintsTo - 4f && player.GroundSpeed > 0.5f && UI.Subtitle.Instance is { CurrentlyShowing: false } sub)
			{
				PrintsThought = true;
				_ = sub.Show("Two sets. One of them's mine.", 0.6f, 3.2f, 1.2f);
				GD.Print("[story] Act 1: the boot prints - two sets, step for step");
			}
			UpdatePlank(player);
			UpdateWetRocks(player, dt);
		}
		// (2026-10-24, the openings' pass) the board while he's out: a print nobody pinned, the cabin from the trees
		if (!_capturing && _hub?.Board is { HasStrangerPrint: false } && Stage <= 2 && InAct1
			&& ((Flag(StrangerFlag) && _clock > 3.0) || (away && pp.DistanceTo(_hub.GlobalPosition) > 90f)))
			CaptureCabin();
		if (!StrangerThought && _hub?.Board is { HasStrangerPrint: true, InView: true } && UI.Subtitle.Instance is { CurrentlyShowing: false } ssub)
		{
			StrangerThought = true;
			_ = ssub.Show("I didn't take that.", 0.5f, 3.0f, 1.0f);
			GD.Print("[story] Act 1: the print nobody pinned - seen");
		}
		// pinned once: a while later, three knocks on the front wall, and outside only boot prints going off into the trees
		if (_builtStage == 2 && _stage2At < 0) _stage2At = _clock;
		if (!Knocked && _builtStage == 2 && _clock - _stage2At > 8.0 && InHub(pp, out var lp) && lp.Z < _hub.Depth * 0.5f - 1.2f) Knock();
		if (!PorchThought && PorchPrints > 0 && _hub != null)
		{
			var l = _hub.ToLocal(pp);
			if (l.Z > _hub.Depth * 0.5f + _hub.PorchDepth + 0.2f && Mathf.Abs(l.X) < 6f && UI.Subtitle.Instance is { CurrentlyShowing: false } psub)
			{
				PorchThought = true;
				_ = psub.Show("The same boots.", 0.4f, 2.6f, 1.0f);
			}
		}
		// pinned once and out again without a second look: sent back
		if (_builtStage == 2 && !Flag(PhotoBoard.RepinFlag) && !BoardNudged && Mathf.Abs(s - CabinS) > 25f && UI.Subtitle.Instance is { CurrentlyShowing: false } nsub)
		{
			BoardNudged = true;
			_ = nsub.Show("I should give those prints another look.", 0.6f, 3.4f, 1.2f);
			GD.Print("[story] Act 1: out of the cabin before the board's done - the thought");
		}
	}

	// ------------------------------------------------------------------ the cabin, watched

	private bool InHub(Vector3 world, out Vector3 local)
	{
		local = Vector3.Zero;
		if (_hub == null || !IsInstanceValid(_hub)) return false;
		local = _hub.ToLocal(world);
		var room = _hub.InteriorBounds;
		return local.X > room.Position.X && local.X < room.End.X && local.Z > room.Position.Z && local.Z < room.End.Z && local.Y > -0.5f && local.Y < 2.5f;
	}

	/// <summary>The cabin photographed from the trees off its porch (a camera of its own on the same world, one frame),
	/// and pinned to the board.</summary>
	private async void CaptureCabin()
	{
		_capturing = true;
		if (!Flag(StrangerFlag)) SetStoryFlag(StrangerFlag);
		var cabin = _hub;
		var front = cabin.GlobalBasis.Z.Normalized();
		var side = cabin.GlobalBasis.X.Normalized();
		var from = cabin.GlobalPosition + front * 15f + side * 7.5f;
		from.Y = _terrain.HeightAt(from.X, from.Z) + 1.45f;
		var vp = new SubViewport { Name = "StrangerCamera", Size = new Vector2I(480, 360), World3D = GetViewport().World3D, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
		var cam = new Camera3D { Fov = 48f, Current = true, Far = 400f };
		vp.AddChild(cam);
		AddChild(vp);
		cam.GlobalPosition = from;
		cam.LookAt(cabin.GlobalPosition + Vector3.Up * 1.2f - side * 0.6f, Vector3.Up);
		cam.RotateObjectLocal(Vector3.Back, Mathf.DegToRad(-3f));   // (held by hand, a little crooked)
		for (int i = 0; i < 4; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		if (!IsInstanceValid(this) || !IsInstanceValid(vp)) return;
		var img = vp.GetTexture().GetImage();
		vp.QueueFree();
		if (img == null || cabin == null || !IsInstanceValid(cabin) || cabin.Board == null) return;
		img.Convert(Image.Format.Rgba8);
		cabin.Board.SetStrangerPrint(img);
		GD.Print("[story] Act 1: a print on the board nobody pinned - the cabin, from the trees");
	}

	private void Knock()
	{
		Knocked = true;
		SetStoryFlag(KnockFlag);
		var door = new Vector3(0.9f, 1.3f, _hub.Depth * 0.5f + 0.2f);
		var at = _hub.ToGlobal(door);
		_ = Cutscene.Run(this, async ct =>
		{
			for (int i = 0; i < 3; i++)
			{
				Audio.AudioDirector.OneShot(this, "wall_knock", 3, at, -2f, "Events", 4f, 0.04f);
				await Cutscene.Wait(this, 0.55, ct);
			}
		});
		LayPorchPrints();
		GD.Print("[story] Act 1: three knocks on the cabin's front wall");
	}

	/// <summary>The other boots: off the bottom of the porch steps and away round the cabin into the trees.</summary>
	private void LayPorchPrints()
	{
		if (PorchPrints > 0 || _hub == null) return;
		var root = new Node3D { Name = "PorchPrints" };
		AddChild(root);
		var tex = BootTexture();
		// from the bottom step, across the pale stone of the path (wet mud shows on it), then off over the dirt into the trees
		float z0 = _hub.Depth * 0.5f + _hub.PorchDepth + 0.45f;
		bool left = true;
		for (int i = 0; i < 16; i++)
		{
			float u = i / 15f;
			// out from the steps, then turning off to the side and away
			var local = new Vector3(0.05f + 7.5f * u * u, 0f, z0 + 3.2f * u - 1.4f * u * u);
			var ahead = new Vector3(15f * u + 0.05f, 0f, 3.2f - 2.8f * u).Normalized();
			var side = ahead.Cross(Vector3.Up).Normalized();
			var w = _hub.ToGlobal(local + side * (left ? -0.14f : 0.14f));
			var dir = _hub.GlobalBasis * ahead;
			var d = new Decal
			{
				Name = $"Porch{i}", TextureAlbedo = tex, Size = new Vector3(0.17f, 0.9f, 0.4f), CullMask = 1, Modulate = new Color(0.35f, 0.3f, 0.26f),
				Position = new Vector3(w.X, _terrain.HeightAt(w.X, w.Z) + 0.15f, w.Z), Rotation = new Vector3(0, Mathf.Atan2(dir.X, dir.Z) + Mathf.Pi, 0),
				UpperFade = 0.3f, LowerFade = 0.3f,
			};
			root.AddChild(d);
			PorchPrints++;
			left = !left;
		}
	}

	// ------------------------------------------------------------------ the poster

	private void BuildPoster(Node3D board)
	{
		const string text =
			"MISSING\n\n" +
			"R______  H______\n(the name has run in the rain: an R, an H)\n\n" +
			"Male, 41.\n" +
			"Last seen October 25 at the Blackfern Trailhead.\n" +
			"Green pack. A camera on a strap around his neck.\n\n" +
			"Anyone with information please call the County Sheriff's Office.";
		Poster = PaperKit.Pinned(board, new Vector3(-0.24f, 1.43f, 0.03f), Vector3.Back, new Vector2(0.3f, 0.42f), PaperKit.Look.Note,
			"A poster stapled over the map", text, Readable.NoteStyle.Printed, 2.5f, "Read the poster", 611);
		if (Poster.GetParent() is not MeshInstance3D paper) return;
		paper.Name = "MissingPoster";
		paper.MaterialOverride = new StandardMaterial3D { AlbedoTexture = ImageTexture.CreateFromImage(PosterImage()), Roughness = 0.85f };
		var ink = new Color(0.08f, 0.08f, 0.08f);
		Label(paper, "MISSING", new Vector3(0, 0.168f, 0.001f), 56, 0.0011f, new Color(0.55f, 0.06f, 0.05f));
		Label(paper, "R. . . . .   H. . . . . .", new Vector3(0, -0.018f, 0.001f), 30, 0.0008f, new Color(0.3f, 0.3f, 0.32f));
		Label(paper, "Male, 41", new Vector3(0, -0.05f, 0.001f), 24, 0.0008f, ink);
		Label(paper, "Last seen Oct. 25\nBlackfern Trailhead", new Vector3(0, -0.088f, 0.001f), 22, 0.0008f, ink);
		Label(paper, "Green pack. Camera on a\nstrap around his neck.", new Vector3(0, -0.137f, 0.001f), 20, 0.0008f, ink);
		Label(paper, "CALL COUNTY SHERIFF", new Vector3(0, -0.186f, 0.001f), 20, 0.0008f, ink);
	}

	private static Label3D Label(Node3D parent, string text, Vector3 at, int size, float pixel, Color c)
	{
		var l = new Label3D
		{
			Text = text, Position = at, FontSize = size, PixelSize = pixel, Modulate = c, Shaded = true, OutlineSize = 0,
			AlphaCut = Label3D.AlphaCutMode.Discard, DoubleSided = false, HorizontalAlignment = HorizontalAlignment.Center,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		parent.AddChild(l);
		return l;
	}

	/// <summary>The poster's paper: weathered, a photocopied photo of a man with a camera strap, the rain run down it.</summary>
	private static Image PosterImage()
	{
		const int w = 240, h = 336;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		var noise = new FastNoiseLite { Seed = 611, Frequency = 0.03f };
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float n = noise.GetNoise2D(x, y);
				float edge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
				float yellow = 0.06f + 0.05f * n + (edge < 10 ? 0.06f * (1f - edge / 10f) : 0f);
				img.SetPixel(x, y, new Color(0.9f - yellow * 0.3f, 0.88f - yellow * 0.7f, 0.82f - yellow * 1.6f));
			}
		// the photo: grey photocopy, head and shoulders, the strap across his chest
		int px0 = 62, px1 = 178, py0 = 52, py1 = 166;
		for (int y = py0; y < py1; y++)
			for (int x = px0; x < px1; x++)
			{
				float u = (x - px0) / (float)(px1 - px0), v = (y - py0) / (float)(py1 - py0);
				float g = 0.62f - 0.18f * v + 0.06f * noise.GetNoise2D(x * 4f, y * 4f);
				float head = new Vector2((u - 0.5f) / 0.17f, (v - 0.36f) / 0.22f).Length();
				float body = v > 0.62f ? Mathf.Abs(u - 0.5f) / (0.2f + (v - 0.62f) * 0.9f) : 9f;
				if (head < 1f) g = 0.3f + 0.08f * head;
				if (body < 1f) g = 0.18f;
				if (body < 1f && Mathf.Abs((u - 0.5f) + (v - 0.8f) * 0.9f) < 0.02f) g = 0.05f;   // (the strap)
				if (head < 0.55f && v > 0.32f && v < 0.36f && Mathf.Abs(u - 0.5f) > 0.03f) g = 0.18f;   // (the eyes' shadow)
				img.SetPixel(x, y, new Color(g, g, g * 0.98f));
			}
		// the rain: run streaks down from the name line
		var rng = new RandomNumberGenerator { Seed = 612 };
		for (int k = 0; k < 9; k++)
		{
			int x = rng.RandiRange(40, 200), y0 = rng.RandiRange(176, 190), len = rng.RandiRange(30, 110);
			for (int y = y0; y < Mathf.Min(h, y0 + len); y++)
				for (int dx = -1; dx <= 1; dx++)
				{
					var c = img.GetPixel(Mathf.Clamp(x + dx, 0, w - 1), y);
					float a = 0.22f * (1f - (y - y0) / (float)len) * (dx == 0 ? 1f : 0.5f);
					img.SetPixel(Mathf.Clamp(x + dx, 0, w - 1), y, c.Lerp(new Color(0.42f, 0.42f, 0.46f), a));
				}
		}
		// staples
		foreach (var (x, y) in new[] { (12, 10), (226, 12), (14, 322), (224, 320) })
			for (int i = -4; i <= 4; i++) img.SetPixel(x + i, y, new Color(0.55f, 0.56f, 0.58f));
		img.GenerateMipmaps();
		return img;
	}

	// ------------------------------------------------------------------ the shot list

	private static string Tick(bool done) => done ? "[x]" : "[  ]";

	private static string ShotListText()
	{
		bool overlook = Flag(ShotOverlookFlag), falls = Flag(StoryManager.Flag.Photo("waterfall")), dusk = Flag(ShotDuskFlag);
		return "October feature: Autumnal Delights\n\n" +
			"A nature photographer's trip into the woods, at the turn of the season.\n\n" +
			"Shoot what the woods give you. Prints back to the office by the first.\n\n" +
			"Shot list (pencilled in):\n" +
			$"   {Tick(overlook)}  the valley from the overlook, in the afternoon light\n" +
			$"   {Tick(falls)}  the falls, up close\n" +
			$"   {Tick(dusk)}  the trail at last light\n\n" +
			"Pin them on the board as you go.";
	}

	private void OnBeatPhoto(Camera3D cam)
	{
		if (cam == null || !IsInsideTree() || !InAct1) return;
		if (!Flag(ShotOverlookFlag) && Platform != null && IsInstanceValid(Platform) && cam.GlobalPosition.DistanceTo(Platform.GlobalPosition) < 16f)
		{
			SetStoryFlag(ShotOverlookFlag);
			GD.Print("[story] Act 1: the shot list - the overlook");
		}
		if (!Flag(ShotDuskFlag) && _builtStage >= 3)
		{
			SetStoryFlag(ShotDuskFlag);
			GD.Print("[story] Act 1: the shot list - the trail at last light");
		}
		// (the falls' tick is the waterfall's own photo flag, set as the shot is scored: read on the next frame)
		Callable.From(() => { if (_hub?.AssignmentSheet is { } sh && IsInstanceValid(sh)) sh.Text = ShotListText(); }).CallDeferred();
	}

	/// <summary>The pinning read back: what was on the list and is up now.</summary>
	private void OnPinned(int n)
	{
		if (_hub?.AssignmentSheet is { } sheet) sheet.Text = ShotListText();
		bool overlook = Flag(ShotOverlookFlag), falls = Flag(StoryManager.Flag.Photo("waterfall"));
		LastPinLine = overlook && falls ? "The overlook. The falls. Two off the list."
			: overlook ? "The overlook's up. No falls yet."
			: falls ? "The falls are up. Nothing from the overlook."
			: "Not one off the list.";
		Audio.AudioDirector.OneShot(this, "brush", 8, _hub?.Board?.GlobalPosition, -14f, "Events", 2f, 0.06f);   // (a pencil tick)
		if (UI.Subtitle.Instance is { } sub) _ = sub.Show(LastPinLine, 0.4f, 3.0f, 1.0f);
		GD.Print($"[story] Act 1: pinned {n} - {LastPinLine}");
	}

	// ------------------------------------------------------------------ walk 1's wrong things

	/// <summary>A doe standing in the Ridge Path, the way the loop comes down it, that turns to watch him and doesn't run.</summary>
	private void PlaceStaringDeer()
	{
		if (_ridge == null || _ridge.Length < 20f) return;
		var at = _ridge.At(_ridge.Length * 0.5f, out Vector2 dir);
		StaringDeer = new Deer { Name = "StaringDeer", Stares = true, Seed = 9 };
		AddChild(StaringDeer);
		StaringDeer.GlobalPosition = new Vector3(at.X, _terrain.HeightAt(at.X, at.Y), at.Y);
		StaringDeer.GlobalRotation = new Vector3(0, Mathf.Atan2(dir.X, dir.Y), 0);
		GD.Print($"[story] Act 1: a doe in the Ridge Path at {StaringDeer.GlobalPosition}");
	}

	/// <summary>R.H.'s thermos stood against a post on the overlook's platform: still warm.</summary>
	private void PlaceThermos()
	{
		var root = new Node3D { Name = "Thermos", Position = new Vector3(-1.45f, 0.8f, -1.35f) };
		Platform.AddChild(root);
		var k = new MeshKit();
		k.Color = Colors.White;
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.3f, 0.17f), Roughness = 0.35f, Metallic = 0.4f });
		k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, 0.28f, 0), 0.042f, 0.042f, 14);
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.5f, 0.5f), Roughness = 0.3f, Metallic = 0.8f });
		k.Cylinder(new Vector3(0, 0.28f, 0), new Vector3(0, 0.33f, 0), 0.044f, 0.04f, 14);
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.82f, 0.78f, 0.62f), Roughness = 0.9f });
		k.Cylinder(new Vector3(0, 0.13f, 0), new Vector3(0, 0.16f, 0), 0.0435f, 0.0435f, 14, false);
		k.CommitTo(root, "Mesh", true);
		Label(root, "R.H.", new Vector3(0, 0.145f, 0.0445f), 22, 0.0006f, new Color(0.1f, 0.1f, 0.12f));
		// the warm breath off it: a faint steam
		var steam = new CpuParticles3D
		{
			Name = "Steam", Amount = 10, Lifetime = 2.2f, Position = new Vector3(0, 0.34f, 0), EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
			EmissionSphereRadius = 0.01f, Direction = Vector3.Up, Spread = 12f, InitialVelocityMin = 0.05f, InitialVelocityMax = 0.1f, Gravity = new Vector3(0, 0.03f, 0),
			ScaleAmountMin = 0.6f, ScaleAmountMax = 1.4f, Mesh = new QuadMesh { Size = new Vector2(0.05f, 0.05f) },
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = new Color(1, 1, 1, 0.08f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, AlbedoTexture = SoftPuff.Texture, VertexColorUseAsAlbedo = true,
			},
		};
		root.AddChild(steam);
		Thermos = new Readable
		{
			Name = "Readable", Title = "A thermos", Style = Readable.NoteStyle.Printed, Prompt = "Look at the thermos", PickRadius = 0.25f, MaxDistance = 2.6f,
			Position = new Vector3(0, 0.16f, 0),
			Text = "A green thermos, dented, stood against the post.\n\n\"R.H.\" on a strip of tape round it.\n\nHalf full. Still warm.",
		};
		root.AddChild(Thermos);
	}

	private static Texture2D _bootTex;

	/// <summary>A muddy boot print: a lugged sole, the toe at the top (-Z of the decal).</summary>
	private static Texture2D BootTexture()
	{
		if (_bootTex != null) return _bootTex;
		const int w = 48, h = 128;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float u = (x + 0.5f) / w - 0.5f, v = (y + 0.5f) / h;
				// the sole: a broad forefoot, a waist, the heel
				float half = v < 0.6f ? 0.46f * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((v - 0.33f) / 0.34f, 2f))) : v < 0.7f ? 0.3f : 0.38f * Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((v - 0.84f) / 0.16f, 2f)));
				float inside = half - Mathf.Abs(u);
				if (inside <= 0f) { img.SetPixel(x, y, new Color(0, 0, 0, 0)); continue; }
				bool lug = (Mathf.PosMod(y, 9) < 5) && Mathf.Abs(u) < half - 0.06f && !(v > 0.6f && v < 0.7f);
				float a = Mathf.Clamp(inside / 0.05f, 0f, 1f) * (lug ? 0.95f : 0.55f);
				img.SetPixel(x, y, new Color(lug ? 0.12f : 0.2f, lug ? 0.09f : 0.15f, lug ? 0.06f : 0.1f, a));
			}
		img.GenerateMipmaps();
		return _bootTex = ImageTexture.CreateFromImage(img);
	}

	/// <summary>Two sets of prints up the trail below the bridge, the way he walked out: his own, and another set right
	/// beside them, bigger, step for step.</summary>
	private void LayBootPrints()
	{
		if (BootPrints > 0) return;
		var root = new Node3D { Name = "BootPrints" };
		AddChild(root);
		var tex = BootTexture();
		var rng = new RandomNumberGenerator { Seed = 6406 };
		bool left = true;
		for (float s = PrintsFrom; s < PrintsTo; s += 0.74f * rng.RandfRange(0.92f, 1.08f))
		{
			var p = _trail.At(s, out Vector2 dir);
			var side = new Vector2(dir.Y, -dir.X);
			float yaw = Mathf.Atan2(dir.X, dir.Y) + Mathf.Pi;
			// his: a little left of the middle; the other: a stride's width to his right, the same pace
			foreach (var (off, size, set) in new[] { (-0.35f, 1f, 0), (0.42f, 1.22f, 1) })
			{
				var at = p + side * (off + (left ? -0.11f : 0.11f) * size);
				float y = _terrain.HeightAt(at.X, at.Y);
				var d = new Decal
				{
					Name = $"Print{set}_{BootPrints}", TextureAlbedo = tex, Size = new Vector3(0.13f * size, 0.6f, 0.31f * size),
					Position = new Vector3(at.X, y + 0.15f, at.Y), Rotation = new Vector3(0, yaw + rng.RandfRange(-0.08f, 0.08f) + (left ? 0.06f : -0.06f), 0),
					AlbedoMix = 1f, UpperFade = 0.3f, LowerFade = 0.3f, CullMask = 1,
				};
				root.AddChild(d);
				BootPrints++;
			}
			left = !left;
		}
		GD.Print($"[story] Act 1: {BootPrints} boot prints laid below the bridge (two sets)");
	}

	/// <summary>The register's last page gone (only the ragged stub in the binding), and the page itself on the cabin's table.</summary>
	private void TearRegister()
	{
		if (TornPage != null) return;
		if (_register != null && IsInstanceValid(_register))
		{
			_register.Text =
				"DATE   PARTY   NAME   DESTINATION   RETURN\n\n" +
				"10/2   2   Hendry   Blackfern + overlook   4:30\n" +
				"10/5   4   Ostrowski family   overlook   3 pm\n" +
				"10/11  1   D. Paulk   Blackfern Trail\n" +
				"10/12  2   M. & T.   overlook   by lunch\n" +
				"10/18  3   Farris   Blackfern, back to lot   5\n\n" +
				"(The next page has been torn out. A ragged edge is left in the binding.)";
			// the right-hand page gone: the sheet cut to its left half
			if (_register.GetParent() is MeshInstance3D paper && paper.Mesh is QuadMesh qm)
			{
				paper.Mesh = new QuadMesh { Size = new Vector2(qm.Size.X * 0.52f, qm.Size.Y), CenterOffset = new Vector3(-qm.Size.X * 0.24f, 0, 0) };
			}
		}
		if (_hub == null) return;
		// on the table by the assignment, as if set down there: R.H.'s two lines, and one more in the same hand
		TornPage = PaperKit.Flat(_hub, new Vector3(0.62f, 0.745f, -0.78f), -21f, new Vector2(0.17f, 0.24f), PaperKit.Look.Ledger,
			"A page torn out of the trail register",
			"10/24  1  R.H.  Blackfern, the old steps  back by dark\n\n" +
			"10/25  1  R.H.  the steps\n\n" +
			"           1  Cameron  the steps",
			Readable.NoteStyle.Handwritten, "Read the page", 613);
		TornPage.ReadFlag = "act1_torn_page_read";
		GD.Print("[story] Act 1: the register's last page torn out; it lies on the cabin's table");
	}

	// ------------------------------------------------------------------ walk 1, felt

	/// <summary>Back over the footbridge after the overlook: a plank cracks under him and sags (it holds).</summary>
	private void UpdatePlank(PlayerController player)
	{
		if (_plankT >= 0)
		{
			_plankT += GetProcessDeltaTime();
			player.WadeScale = _plankT < 0.75 ? 0.25f : Mathf.Min(1f, player.WadeScale + (float)GetProcessDeltaTime() * 2f);
			if (_plankT > 1.4) { _plankT = -1; player.WadeScale = 1f; }
			return;
		}
		if (PlankCracked || !OverlookReached || _bridge == null || !IsInstanceValid(_bridge)) return;
		var local = _bridge.ToLocal(player.GlobalPosition);
		if (Mathf.Abs(local.Z) > 1.2f || Mathf.Abs(local.X) > 1.2f || Mathf.Abs(local.Y) > 1.5f) return;
		PlankCracked = true;
		SetStoryFlag(PlankFlag);
		_plankT = 0;
		Audio.AudioDirector.OneShot(this, "stair_break", 1, player.GlobalPosition, -5f, "Events", 4f, 0.04f);
		Audio.AudioDirector.OneShot(this, "trunk_creak", 4, player.GlobalPosition, -10f, "Events", 3f, 0.08f);
		if (player.CameraRig is { } rig)
		{
			var tw = rig.CreateTween();
			tw.TweenMethod(Callable.From<float>(v => { if (IsInstanceValid(rig)) rig.Shake = new Vector3(0.01f * v, -0.11f * v, 0f); }), 0f, 1f, 0.07f);
			tw.TweenMethod(Callable.From<float>(v => { if (IsInstanceValid(rig)) rig.Shake = new Vector3(0.01f * v, -0.11f * v, 0f); }), 1f, 0f, 0.6f).SetTrans(Tween.TransitionType.Sine);
		}
		BrokenPlank(local.Z);
		GD.Print("[story] Act 1: a plank cracks under him on the footbridge");
	}

	/// <summary>The cracked plank: two halves sunk into a V where it split, on top of the deck at metres <paramref name="z"/>
	/// along the bridge.</summary>
	private void BrokenPlank(float z)
	{
		var k = new MeshKit();
		k.Color = new Color(0.72f, 0.7f, 0.68f);
		k.Mat(BuildingTextures.BridgePlankMat);
		float half = 0.75f;
		k.Box(new Vector3(-half * 0.5f, 0.035f, z), new Vector3(half, 0.05f, 0.21f), 1.3f, new Basis(Vector3.Back, Mathf.DegToRad(-6f)));
		k.Box(new Vector3(half * 0.5f, 0.035f, z), new Vector3(half, 0.05f, 0.21f), 1.3f, new Basis(Vector3.Back, Mathf.DegToRad(6f)));
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.74f, 0.55f), Roughness = 0.9f });
		for (int i = 0; i < 5; i++)   // (the pale splinters along the break)
			k.Box(new Vector3((i - 2) * 0.006f, 0.04f + i % 2 * 0.006f, z + (i - 2) * 0.035f), new Vector3(0.01f, 0.012f, 0.03f), 1f, new Basis(Vector3.Back, (i - 2) * 0.4f));
		k.CommitTo(_bridge, "BrokenPlank", true);
	}

	/// <summary>Wet rocks round the climb beside the falls (dark, glossy with the spray).</summary>
	private void BuildWetRocks()
	{
		if (_falls == null || _falls.Length < WetTo + 2f) return;
		var k = new MeshKit();
		k.Color = Colors.White;
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.2f, 0.21f, 0.2f), Roughness = 0.08f, MetallicSpecular = 0.75f, ClearcoatEnabled = true, Clearcoat = 0.6f });
		var rng = new RandomNumberGenerator { Seed = 6420 };
		for (float s = WetFrom - 2f; s < WetTo + 2f; s += 0.9f)
		{
			var p = _falls.At(s, out Vector2 dir);
			var side = new Vector2(dir.Y, -dir.X);
			// slabs under foot (low, set in the path) and the rock either side of it
			foreach (float off in new[] { rng.RandfRange(-0.6f, 0.6f), rng.RandfRange(1.3f, 2.2f), -rng.RandfRange(1.3f, 2.2f) })
			{
				var at = p + side * off + dir * rng.RandfRange(-0.3f, 0.3f);
				bool slab = Mathf.Abs(off) < 1f;
				var size = slab ? new Vector3(rng.RandfRange(0.5f, 0.9f), 0.12f, rng.RandfRange(0.4f, 0.7f)) : new Vector3(rng.RandfRange(0.6f, 1.2f), rng.RandfRange(0.35f, 0.8f), rng.RandfRange(0.6f, 1.1f));
				float y = _terrain.HeightAt(at.X, at.Y) + (slab ? -0.03f : size.Y * 0.25f);
				k.BevelBox(new Vector3(at.X, y, at.Y), size, slab ? 0.04f : 0.12f, 1f, Basis.FromEuler(new Vector3(rng.RandfRange(-0.1f, 0.1f), rng.RandfRange(0f, Mathf.Tau), rng.RandfRange(-0.1f, 0.1f))));
			}
		}
		var mi = k.CommitTo(this, "WetRocks", true);
		mi.TopLevel = true;
	}

	/// <summary>On the wet rock: slow going, the view swaying a little; once, halfway up, a foot goes.</summary>
	private void UpdateWetRocks(PlayerController player, float dt)
	{
		if (_falls == null) return;
		var pp = player.GlobalPosition;
		float d = _falls.Closest(new Vector2(pp.X, pp.Z), out float sf);
		bool on = d < 3.5f && sf > WetFrom && sf < WetTo;
		float want = on ? 0.5f : 1f;
		if (_slipT >= 0)
		{
			_slipT += dt;
			want = _slipT < 0.5 ? 0f : 0.5f;
			if (_slipT > 1.2) _slipT = -1;
		}
		if (_plankT < 0 && (on || WetRockDrag < 1f))
		{
			WetRockDrag = Mathf.MoveToward(WetRockDrag, want, dt * 2.5f);
			player.WadeScale = WetRockDrag;
		}
		if (on && player.CameraRig is { } rig && _slipT < 0)
		{
			_swaying = true;
			_wetSway += dt * (0.8f + player.GroundSpeed);
			rig.Shake = new Vector3(0.006f * Mathf.Sin(_wetSway * 1.3f), 0.004f * Mathf.Sin(_wetSway * 2.6f), 0f);
		}
		else if (_swaying && !on && _slipT < 0 && player.CameraRig is { } r2) { _swaying = false; r2.Shake = Vector3.Zero; }
		// the slip: halfway up, moving
		if (on && !Slipped && sf > (WetFrom + WetTo) * 0.5f && player.GroundSpeed > 0.3f)
		{
			Slipped = true;
			SetStoryFlag(SlipFlag);
			_slipT = 0;
			Audio.AudioDirector.OneShot(this, "step_stone", 6, pp, -4f, "Player", 2f, 0.1f);
			Audio.AudioDirector.OneShot(this, "dust_trickle", 3, pp + Vector3.Down * 0.5f, -8f, "Events", 3f, 0.08f);
			Audio.AudioDirector.OneShot(this, "pbreath_gasp", 8, null, -10f, "Player", 3f, 0.05f);
			if (player.CameraRig is { } rig2)
			{
				var tw = rig2.CreateTween();
				tw.TweenMethod(Callable.From<float>(v => { if (IsInstanceValid(rig2)) rig2.Shake = new Vector3(0.05f * v, -0.13f * v, 0f); }), 0f, 1f, 0.09f);
				tw.TweenMethod(Callable.From<float>(v => { if (IsInstanceValid(rig2)) rig2.Shake = new Vector3(0.05f * v, -0.13f * v, 0f); }), 1f, 0f, 0.7f).SetTrans(Tween.TransitionType.Sine);
			}
			GD.Print("[story] Act 1: a foot goes on the wet rock by the falls");
		}
	}
}
