using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// The backlit photo board on the cabin's wall (2026-10-16, STORY.md Rev 5: "a backlit photo board, empty and glowing"). The
/// one cabin carries it everywhere: in Act 1 by the lot it is the player's own board, in the Hollow (Act 5) it hangs with
/// R.H.'s photographs pinned across it, and in Act 7 its last print burns. The board is Blender's (tools/Blender/
/// cabin_story.py: PhotoBoard, PhotoBoardPanel, BoardPin); the prints are quads with the pictures on them, lit a little
/// from behind.
///
/// Local frame: the origin is the middle of the board's back, on the wall; +Z out of the wall, Y up. It joins the group
/// <see cref="Group"/>.
///
/// <b>Act 1</b> (<see cref="BoardMode.Act1"/>): standing at it, E pins the player's photos: the best of the prints taken
/// since the last pinning go up (up to eight, the most stars first; then the rest by score), filling the board. The first
/// time, with nothing taken yet, the hint "Nothing to pin yet." Pinning after a walk moves the day on: the first pinning
/// sets <see cref="Stage2Flag"/> ("act1_stage_2"), the second <see cref="Stage3Flag"/> ("act1_stage_3"); ForestAtmosphere and
/// the walks read them. What the board shows (the stages, as STORY.md numbers them):
/// <list type="number">
/// <item>After walk 1: the pictures as taken.</item>
/// <item>Looked at again (the player has looked away from it for a few seconds and back): in one print, small at its edge,
/// something tall and black; another slightly blurred in the middle. Pin again (E): the black shape is gone from the
/// picture, and the hum is faintly there, only in this room.</item>
/// <item>After walk 2: the falls print comes out decades old with a figure on the rock, and the framed two-person photo
/// on the table changes the same way (<see cref="Cabin.SetFramedPhotoChanged"/>).</item>
/// <item>Looked at again: the black shape back, closer, in a print taken facing the other way.</item>
/// <item>Looked at again: a print he doesn't remember taking, the stairs, through the cabin's window.</item>
/// </list>
/// Every change happens while the board is out of sight; nothing flashes. Each step is a saved flag, so a Continue rebuilds
/// the board as it was (from the album on disk).
///
/// <b>Hollow</b> (<see cref="BoardMode.Hollow"/>): R.H.'s four prints (the three birds and the stairs at night) pinned
/// across it, no E.
///
/// <b>For Act 7 and Act 25</b>: <see cref="ClearPrints"/>, <see cref="ShowOnlyStairs"/> (the one print left, returned),
/// <see cref="StairsPrintTexture"/>, <see cref="PinPrint"/>, <see cref="GlowOn"/>, <see cref="Prints"/>.
/// </summary>
public partial class PhotoBoard : Node3D
{
	public enum BoardMode { Act1, Hollow, Bare }

	/// <summary>Every photo board joins this group (one per level).</summary>
	public const string Group = "photo_board";
	/// <summary>Set when the board is first pinned after walk 1 (the light and fog go to walk 2's, walk 2 opens).</summary>
	public const string Stage2Flag = "act1_stage_2";
	/// <summary>Set when the board is pinned after walk 2 (the light and fog go to walk 3's, walk 3 opens).</summary>
	public const string Stage3Flag = "act1_stage_3";
	/// <summary>A pinning: <c>act1_board_pin_{n}_{last photo number pinned}</c>.</summary>
	public const string PinPrefix = "act1_board_pin_";
	/// <summary>Stage 2 seen: the black shape at an edge, a blurred print.</summary>
	public const string ShapeFlag = "act1_board_shape";
	/// <summary>Pinned again after the shape: it's gone, and the room hums.</summary>
	public const string RepinFlag = "act1_board_repinned";
	/// <summary>(2026-10-24, two walks) Looked away from after the repin and back: the falls print decades old, the framed
	/// photo changed (stage 3).</summary>
	public const string AgedFlag = "act1_board_aged";
	/// <summary>Stage 4 seen: the shape closer.</summary>
	public const string CloserFlag = "act1_board_closer";
	/// <summary>Stage 5 seen: the stairs print.</summary>
	public const string StairsFlag = "act1_board_stairs";

	[Export] public BoardMode Mode = BoardMode.Act1;
	/// <summary>The hum's level once the shape has gone again (faint: only in this room).</summary>
	[Export] public float HumDb = -22f;
	/// <summary>Seconds the board must be out of the player's view before it changes, and in it before that counts.</summary>
	[Export] public float AwaySeconds = 2.5f, SeenSeconds = 1.2f;

	/// <summary>The prints' render layer (11): everything sees them but the board's own glow.</summary>
	private const uint PrintLayer = 1u << 10;
	/// <summary>The panel's glow (0.85 until 2026-10-24: a little lower, so the prints read against it).</summary>
	private const float PanelGlow = 0.6f;
	public const float Width = 1.04f, Height = 0.74f, PanelFace = 0.042f;
	/// <summary>The size of a print on the board (the card with its border).</summary>
	public static readonly Vector2 PrintSize = new(0.2f, 0.16f);
	private static readonly Vector2[] Slots =
	{
		new(-0.345f, 0.13f), new(-0.115f, 0.135f), new(0.115f, 0.128f), new(0.345f, 0.133f),
		new(-0.345f, -0.11f), new(-0.115f, -0.105f), new(0.115f, -0.112f), new(0.345f, -0.107f),
	};
	private static readonly float[] SlotTilt = { -2.5f, 1.5f, -1f, 3f, 2f, -3f, 1f, -1.5f };
	private static readonly Vector2 StairsSlot = new(0.29f, -0.2f);

	/// <summary>The board's stage, as STORY.md numbers them (0 empty; Hollow and Bare stay 0).</summary>
	public int Stage { get; private set; }
	/// <summary>The prints on it now.</summary>
	public IReadOnlyList<MeshInstance3D> Prints => _prints;
	/// <summary>The stairs print, once it's up (stage 5, or <see cref="ShowOnlyStairs"/>).</summary>
	public MeshInstance3D StairsPrint { get; private set; }
	/// <summary>The E point (Act 1 only).</summary>
	public PickupInteractable Use { get; private set; }
	/// <summary>Raised whenever the stage changes.</summary>
	public event Action<int> StageChanged;

	private readonly List<MeshInstance3D> _prints = new();
	private Node3D _printRoot;
	private MeshInstance3D _panel;
	private StandardMaterial3D _panelMat;
	private OmniLight3D _glow;
	private bool _glowOn = true;
	private AudioStreamPlayer3D _hum;
	private float _seenFor, _awayFor;
	private bool _restored;

	/// <summary>(Act 1) which photo (album number) is in each slot, and the picture shown for it (after the darkroom).</summary>
	private readonly PhotoLog.Photo[] _slotPhoto = new PhotoLog.Photo[8];
	private readonly Dictionary<int, Texture2D> _shown = new();
	private int _shapePhoto = -1, _blurPhoto = -1, _fallsPhoto = -1, _closerPhoto = -1, _closestPhoto = -1;

	/// <summary>The board's light: the panel glowing and its spill on the wall. Off: a dull grey panel.</summary>
	public bool GlowOn
	{
		get => _glowOn;
		set
		{
			_glowOn = value;
			if (_panelMat != null) _panelMat.EmissionEnergyMultiplier = value ? PanelGlow : 0f;
			if (_glow != null) _glow.Visible = value;
			foreach (var p in _prints)
				if (IsInstanceValid(p) && p.MaterialOverride is StandardMaterial3D m) m.AlbedoColor = value ? PrintTone : PrintDark;
		}
	}

	/// <summary>The stairs print's picture (its card), the same every time (for Act 7's last print).</summary>
	public static Texture2D StairsPrintTexture => _stairsTex ??= ImageTexture.CreateFromImage(Card(StairsFromWindow()));
	private static Texture2D _stairsTex;

	public override void _Ready()
	{
		AddToGroup(Group);
		Build();
		if (Engine.IsEditorHint()) return;
		if (Mode == BoardMode.Hollow) PinHollow();
		if (Mode == BoardMode.Act1)
		{
			Use = new PickupInteractable
			{
				Name = "PinUse", Prompt = "Pin your photos", MaxDistance = 2.8f, PickRadius = 0.5f, Position = new Vector3(0, 0, 0.1f),
				HighlightRoot = new NodePath("../Board"),
				PromptFor = _ => Pinnable ? "Pin your photos" : "",
				// (2026-10-24) no white line: its grown hull round the board's backing was drawn over the prints a few
				// millimetres in front of it, washing every one of them out to white
				Outline = false,
			};
			Use.Interacted += OnUse;
			AddChild(Use);
			// (the album is read from disk in PhotoLog's _Ready, after the world's)
			Callable.From(Restore).CallDeferred();
		}
	}

	/// <summary>The board works in Act 1 only (on the way home in Act 25 it's just there).</summary>
	private static bool Pinnable => StoryManager.Instance is not { } s || s.Current < Checkpoint.Act2StairsClimbed;

	// ------------------------------------------------------------------ build

	private void Build()
	{
		var paint = (Func<string, Material>)Paint;
		var board = ItemModels.Add(this, "cabin_story", "PhotoBoard", Transform3D.Identity, "Board", paint);
		if (board != null) board.SetMeta("surface", "wood");
		_panelMat = new StandardMaterial3D
		{
			ResourceName = "board_glow", AlbedoColor = new Color(0.93f, 0.91f, 0.85f), Roughness = 0.5f,
			EmissionEnabled = true, Emission = new Color(1f, 0.95f, 0.84f), EmissionEnergyMultiplier = PanelGlow,
		};
		var panelMesh = ItemModels.Get("cabin_story", "PhotoBoardPanel");
		_panel = new MeshInstance3D
		{
			Name = "Panel", Mesh = panelMesh ?? new QuadMesh { Size = new Vector2(0.92f, 0.62f) }, MaterialOverride = _panelMat,
			Position = panelMesh == null ? new Vector3(0, 0, PanelFace) : Vector3.Zero, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_panel);
		_glow = new OmniLight3D
		{
			Name = "Glow", LightColor = new Color(1f, 0.93f, 0.8f), LightEnergy = 0.3f, OmniRange = 1.9f, OmniAttenuation = 1.4f,
			ShadowEnabled = false, Position = new Vector3(0, 0, 0.32f),
			LightCullMask = 0xFFFFFu & ~PrintLayer,   // (2026-10-24: it lit the prints so close it blew them out to white)
		};
		AddChild(_glow);
		_printRoot = new Node3D { Name = "Prints" };
		_printRoot.AddToGroup(Cabin.PapersGroup);   // (paper burns first, with the cabin's other papers)
		AddChild(_printRoot);
		GlowOn = _glowOn;
	}

	private static Material Paint(string name) => name switch
	{
		"board_wood" => FixtureModels.Tint(ProcTextures.WoodMat, new Color(0.42f, 0.3f, 0.22f)),
		"frame_wood" => FixtureModels.Tint(ProcTextures.WoodMat, new Color(0.55f, 0.38f, 0.26f)),
		"peg_wood" => FixtureModels.Tint(ProcTextures.WoodMat, new Color(0.85f, 0.72f, 0.58f)),
		"backing" => Flat("cs_backing", new Color(0.24f, 0.2f, 0.16f), 0.9f),
		"pin_red" => Flat("cs_pin_red", new Color(0.62f, 0.07f, 0.05f), 0.35f),
		"steel" => ItemTextures.ChromeMat,
		"graphite" => Flat("cs_graphite", new Color(0.07f, 0.07f, 0.07f), 0.45f),
		"manila" => Flat("cs_manila", new Color(0.76f, 0.64f, 0.42f), 0.9f),
		"pencil_paint" => Flat("cs_pencil", new Color(0.82f, 0.6f, 0.1f), 0.45f),
		"eraser" => Flat("cs_eraser", new Color(0.78f, 0.42f, 0.4f), 0.9f),
		"glow" => Flat("cs_glow_off", new Color(0.8f, 0.79f, 0.75f), 0.5f),
		_ => null,
	};

	/// <summary>The cabin's story props' paint (the frame, the pegs, the folder, the pencil share it with the board).</summary>
	public static Material StoryPaint(string name) => Paint(name);

	private static readonly Dictionary<string, StandardMaterial3D> _flat = new();
	private static StandardMaterial3D Flat(string key, Color c, float rough)
	{
		if (_flat.TryGetValue(key, out var m)) return m;
		return _flat[key] = new StandardMaterial3D { ResourceName = key, AlbedoColor = c, Roughness = rough };
	}

	// ------------------------------------------------------------------ prints

	/// <summary>Pins a print (its picture <paramref name="tex"/>) at <paramref name="at"/> on the board's face (metres from its
	/// middle), turned <paramref name="tiltDeg"/>; a red pin through its top. Returns the print.</summary>
	public MeshInstance3D PinPrint(Texture2D tex, Vector2 at, float tiltDeg = 0f, string name = null, float lift = 0f)
	{
		float aspect = tex != null && tex.GetHeight() > 0 ? (float)tex.GetWidth() / tex.GetHeight() : PrintSize.X / PrintSize.Y;
		var size = new Vector2(PrintSize.X, PrintSize.X / aspect);
		var basis = new Basis(Vector3.Back, Mathf.DegToRad(tiltDeg));
		var print = new MeshInstance3D
		{
			Name = name ?? $"Print{_prints.Count}",
			Mesh = new QuadMesh { Size = size },
			MaterialOverride = PrintMaterial(tex),
			Transform = new Transform3D(basis, new Vector3(at.X, at.Y, PanelFace + 0.002f + lift + _prints.Count * 0.0004f)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = PrintLayer,
		};
		_printRoot.AddChild(print);
		var pinMesh = ItemModels.Get("cabin_story", "BoardPin");
		if (pinMesh != null)
		{
			var pin = new MeshInstance3D { Name = "Pin", Mesh = pinMesh, Position = new Vector3(0, size.Y * 0.5f - 0.012f, 0f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			for (int s = 0; s < pinMesh.GetSurfaceCount(); s++)
				if (ItemModels.Paint(pinMesh.SurfaceGetMaterial(s)?.ResourceName ?? "", Paint) is { } m) pin.SetSurfaceOverrideMaterial(s, m);
			print.AddChild(pin);
		}
		_prints.Add(print);
		return print;
	}

	/// <summary>(2026-10-24) Unlit, as a print on a lightbox reads: lit, they took the panel's glow (a few mm behind them)
	/// as bounce light and burned out to white. Dimmed when the board's light is off.</summary>
	private StandardMaterial3D PrintMaterial(Texture2D tex) => new()
	{
		AlbedoTexture = tex, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = _glowOn ? PrintTone : PrintDark,
		TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
	};

	private static readonly Color PrintTone = new(0.86f, 0.84f, 0.8f), PrintDark = new(0.16f, 0.15f, 0.14f);

	private static void SetPicture(MeshInstance3D print, Texture2D tex)
	{
		if (print?.MaterialOverride is not StandardMaterial3D m) return;
		m.AlbedoTexture = tex;
		m.EmissionTexture = tex;
	}

	/// <summary>Takes every print down.</summary>
	public void ClearPrints()
	{
		foreach (var p in _prints)
			if (IsInstanceValid(p)) { p.GetParent()?.RemoveChild(p); p.QueueFree(); }
		_prints.Clear();
		StairsPrint = null;
	}

	/// <summary>Act 7: the pictures are gone, one print is left, of the stairs. Returns it (its pin is its child "Pin").</summary>
	public MeshInstance3D ShowOnlyStairs()
	{
		ClearPrints();
		StairsPrint = PinPrint(StairsPrintTexture, new Vector2(0.02f, 0.02f), -3f, "StairsPrint");
		return StairsPrint;
	}

	/// <summary>R.H.'s photographs pinned across the board: the three birds he came for and the stairs at night.</summary>
	private void PinHollow()
	{
		ClearPrints();
		PinPrint(BuildingTextures.Print(0), new Vector2(-0.31f, 0.13f), -4f, "Print0");
		PinPrint(BuildingTextures.Print(1), new Vector2(0.0f, 0.15f), 3f, "Print1");
		PinPrint(BuildingTextures.Print(2), new Vector2(0.3f, 0.1f), -2f, "Print2");
		StairsPrint = PinPrint(BuildingTextures.Print(3), new Vector2(-0.06f, -0.13f), 5f, "Print3");
	}

	// ------------------------------------------------------------------ Act 1: pinning

	private static StoryManager Story => StoryManager.Instance;

	/// <summary>The pinnings so far: (n, last photo number) in order.</summary>
	private static List<(int n, int upto)> Pinnings()
	{
		var list = new List<(int, int)>();
		if (Story == null) return list;
		foreach (var f in Story.Flags)
		{
			if (!f.StartsWith(PinPrefix)) continue;
			var parts = f[PinPrefix.Length..].Split('_');
			if (parts.Length == 2 && int.TryParse(parts[0], out int n) && int.TryParse(parts[1], out int upto)) list.Add((n, upto));
		}
		list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
		return list;
	}

	private static bool Has(string flag) => Story?.HasFlag(flag) ?? false;

	/// <summary>The best of the album's prints after <paramref name="after"/> up to <paramref name="upto"/>: up to eight, the most
	/// stars first, then the score, then the order taken.</summary>
	private static List<PhotoLog.Photo> Best(int after, int upto)
	{
		if (PhotoLog.Instance == null) return new();
		return PhotoLog.Instance.Photos.Where(p => p.Number > after && p.Number <= upto && p.Texture != null)
			.OrderByDescending(p => p.Stars).ThenByDescending(p => p.Total).ThenBy(p => p.Number).Take(8).ToList();
	}

	private static int LastPhoto => PhotoLog.Instance?.Photos.Count > 0 ? PhotoLog.Instance.Photos.Max(p => p.Number) : 0;

	private void OnUse(PlayerController player)
	{
		if (!Pinnable) return;
		var pins = Pinnings();
		int upto = pins.Count > 0 ? pins[^1].upto : 0;
		int last = LastPhoto;
		if (last > upto)
		{
			int n = pins.Count + 1;
			Story?.SetFlag($"{PinPrefix}{n}_{last}");
			Audio.AudioDirector.OneShot(this, "paper_rustle", 3, GlobalPosition + GlobalBasis.Z * 0.2f, -10f, "Events", 2f, 0.08f);
			GD.Print($"[story] Act 1: the board pinned ({n}: prints {upto + 1}-{last})");
			Restore();
			if (n == 1) Story?.SetFlag(Stage2Flag);
			if (n == 2) Story?.SetFlag(Stage3Flag);
			_seenFor = 0f;
			_awayFor = 0f;
			Pinned?.Invoke(n);
			PulseGlow();
			return;
		}
		if (Stage == 2 && !Has(RepinFlag))
		{
			// pinned again: the black shape is gone from the picture, and the room hums
			Story?.SetFlag(RepinFlag);
			Audio.AudioDirector.OneShot(this, "paper_rustle", 3, GlobalPosition + GlobalBasis.Z * 0.2f, -12f, "Events", 2f, 0.08f);
			GD.Print("[story] Act 1: the board pinned again - the shape is gone; the hum; the last walk opens");
			Restore();
			// (2026-10-24: Act 1 is two walks now) the pinning again is what sends him out the last time, at dusk
			Story?.SetFlag(Stage3Flag);
			_seenFor = 0f;
			_awayFor = 0f;
			return;
		}
		_ = UI.Subtitle.Instance?.Show("Nothing to pin yet.", 0.4f, 2.4f, 0.8f);
	}

	/// <summary>(2026-10-24) A print nobody pinned: the cabin from the trees, taken while he was out on the first walk
	/// (Act1Walks). Pinned over whatever else is up.</summary>
	public bool HasStrangerPrint => _stranger != null;
	private Texture2D _stranger;

	public void SetStrangerPrint(Image photo)
	{
		_stranger = ImageTexture.CreateFromImage(Card(photo));
		Restore();
	}

	/// <summary>The board in the player's view now (close, in the middle of the picture, in the room).</summary>
	public bool InView => Seen();

	/// <summary>Raised on a pinning of new prints (its number, 1 on).</summary>
	public event Action<int> Pinned;

	/// <summary>The pinning felt: the board's light swells and settles.</summary>
	private void PulseGlow()
	{
		if (_panelMat == null || !_glowOn) return;
		var tw = CreateTween();
		tw.TweenProperty(_panelMat, "emission_energy_multiplier", 1.9f, 0.18f).SetTrans(Tween.TransitionType.Sine);
		tw.TweenProperty(_panelMat, "emission_energy_multiplier", PanelGlow, 0.9f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		if (_glow != null)
		{
			var t2 = CreateTween();
			t2.TweenProperty(_glow, "light_energy", 0.85f, 0.18f);
			t2.TweenProperty(_glow, "light_energy", 0.3f, 0.9f).SetEase(Tween.EaseType.Out);
		}
	}

	/// <summary>Rebuilds the board from the saved flags and the album (on load, and after every change).</summary>
	private void Restore()
	{
		_restored = true;
		var pins = Pinnings();
		Array.Clear(_slotPhoto);
		int after = 0;
		List<PhotoLog.Photo> lastBest = new();
		foreach (var (n, upto) in pins)
		{
			var best = Best(after, upto);
			// the new prints go up first; the slots they don't need keep the last walk's
			var keep = _slotPhoto.Where(p => p != null).ToList();
			Array.Clear(_slotPhoto);
			for (int i = 0; i < 8; i++)
				_slotPhoto[i] = i < best.Count ? best[i] : (i - best.Count < keep.Count ? keep[i - best.Count] : null);
			if (n == 1)
			{
				// stage 2's prints: the shape in the least of them (a picture of nothing much: the trees), the blur in another
				var order = best.OrderBy(p => p.Stars).ThenBy(p => p.Total).ToList();
				_shapePhoto = order.Count > 0 ? order[0].Number : -1;
				_blurPhoto = order.Count > 1 ? order[1].Number : -1;
			}
			// (2026-10-24) one walk out before the last now: the falls are walk 1's, so the falls print and the shape's later
			// frames come from the first pinning (a second, from an old save, picks them again from its own)
			if (n == 1 || n == 2)
			{
				// the falls print (the best of the falls, else the best of the walk), and the shape back in the print taken
				// furthest from it (out along the way, as against back: facing the other way)
				var falls = best.Where(p => p.SubjectId == "waterfall").OrderByDescending(p => p.Total).FirstOrDefault() ?? best.FirstOrDefault();
				_fallsPhoto = falls?.Number ?? -1;
				var other = best.Where(p => p != falls && p.Number != _shapePhoto).OrderByDescending(p => Mathf.Abs(p.Number - _fallsPhoto)).FirstOrDefault()
					?? _slotPhoto.FirstOrDefault(p => p != null && p != falls);
				_closerPhoto = other?.Number ?? -1;
				// the shape's last frame: yet another print, right up close in it
				var last = best.Where(p => p != falls && p != other && p.Number != _shapePhoto && p.Number != _blurPhoto).OrderByDescending(p => p.Number).FirstOrDefault()
					?? best.FirstOrDefault(p => p != falls && p != other);
				_closestPhoto = last?.Number ?? -1;
			}
			after = upto;
			lastBest = best;
		}
		// (2026-10-24, two walks) after the one pinning: the shape (2), pinned again (the walk at dusk opens), looked away
		// from and back: the falls print aged (3), then the shape closer (4), then the stairs and the shape right up close (5)
		int stage = pins.Count == 0 ? 0 : pins.Count == 1 && !Has(AgedFlag) ? (Has(ShapeFlag) ? 2 : 1) : (Has(StairsFlag) ? 5 : Has(CloserFlag) ? 4 : 3);
		bool changedStage = stage != Stage;
		Stage = stage;

		// the prints, through the darkroom
		_shown.Clear();
		ClearPrints();
		for (int i = 0; i < 8; i++)
		{
			var p = _slotPhoto[i];
			if (p == null) continue;
			PinPrint(Picture(p), Slots[i], SlotTilt[i], $"Print{i}");
		}
		if (Stage >= 5) StairsPrint = PinPrint(StairsPrintTexture, StairsSlot, -7f, "StairsPrint", 0.003f);
		if (_stranger != null && Pinnable) PinPrint(_stranger, pins.Count == 0 ? new Vector2(0.01f, 0.02f) : new Vector2(0.03f, -0.01f), 4f, "StrangerPrint", 0.005f);
		if (OwnerCabin is { } cabin) cabin.SetFramedPhotoChanged(Stage >= 3);
		UpdateHum();
		if (changedStage) StageChanged?.Invoke(Stage);
	}

	/// <summary>A print's picture as the board shows it now (its card).</summary>
	private Texture2D Picture(PhotoLog.Photo p)
	{
		bool shape = p.Number == _shapePhoto && Stage == 2 && !Has(RepinFlag);
		bool blur = p.Number == _blurPhoto && Has(ShapeFlag);
		bool falls = p.Number == _fallsPhoto && Stage >= 3;
		bool closer = p.Number == _closerPhoto && Stage >= 4 && !falls;
		bool closest = p.Number == _closestPhoto && Stage >= 5 && !falls && !closer;
		if (!shape && !blur && !falls && !closer && !closest) return ImageTexture.CreateFromImage(Card(Copy(p.Texture)));
		var img = Copy(p.Texture);
		if (blur) img = BlurMiddle(img);
		if (shape) img = WithShape(img, new Vector2(0.93f, 0.68f), 0.24f, 0.35f);   // small, at the edge of the trees
		if (closer) img = WithShape(img, new Vector2(0.16f, 0.97f), 0.62f, 0.08f);  // nearer, at the other edge
		if (closest) img = WithShape(img, new Vector2(0.78f, 1.25f), 1.15f, 0.02f); // right up close: its shoulder filling a side
		if (falls) img = Aged(img, p.Number, true, new Vector2(0.5f, 0.74f), 0.3f);
		return ImageTexture.CreateFromImage(Card(img));
	}

	private void UpdateHum()
	{
		bool on = Has(RepinFlag) && Pinnable;
		if (on && _hum == null)
		{
			const string path = "res://assets/audio/ambient/stairs_hum_loop.wav";
			if (!ResourceLoader.Exists(path)) return;
			var stream = GD.Load<AudioStream>(path);
			if (stream is AudioStreamWav wav)
			{
				wav = (AudioStreamWav)wav.Duplicate();
				wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
				wav.LoopEnd = Mathf.RoundToInt(wav.GetLength() * wav.MixRate);
				stream = wav;
			}
			// faint, and only in the room: it dies out before the door
			_hum = new AudioStreamPlayer3D { Name = "Hum", Stream = stream, Bus = "Unnatural", UnitSize = 1.2f, MaxDistance = 4.2f, Position = new Vector3(0, 0, 0.6f) };
			AddChild(_hum);
			_hum.Play();
			StoryBeat.FadeIn(_hum, HumDb, 5f, -50f);
		}
		else if (!on && _hum != null)
		{
			_hum.QueueFree();
			_hum = null;
		}
	}

	// ------------------------------------------------------------------ Act 1: looked at again

	public override void _Process(double delta)
	{
		if (Mode != BoardMode.Act1 || !_restored || Engine.IsEditorHint()) return;
		// only the looks that change it: after the first pinning (to stage 2), after the second (to 4, then 5)
		bool waiting = (Stage == 1) || (Stage == 2 && Has(RepinFlag)) || (Stage == 3) || (Stage == 4);
		if (!waiting) return;
		float dt = (float)delta;
		if (Seen()) { _seenFor += dt; _awayFor = 0f; return; }
		_awayFor += dt;
		if (_seenFor < SeenSeconds || _awayFor < AwaySeconds) return;
		_seenFor = 0f;
		_awayFor = 0f;
		string flag = Stage == 1 ? ShapeFlag : Stage == 2 ? AgedFlag : Stage == 3 ? CloserFlag : StairsFlag;
		GD.Print($"[story] Act 1: the board changed while unseen ({flag})");
		Story?.SetFlag(flag);
		Restore();
	}

	/// <summary>The board is in the player's view: close, in the middle of the picture, and the player in the same room.</summary>
	private bool Seen()
	{
		var cam = GetViewport()?.GetCamera3D();
		if (cam == null) return false;
		Vector3 face = GlobalTransform * new Vector3(0, 0, PanelFace);
		Vector3 to = face - cam.GlobalPosition;
		float d = to.Length();
		if (d > 5.5f || d < 0.05f) return false;
		if (to.Normalized().Dot(-cam.GlobalBasis.Z) < Mathf.Cos(Mathf.DegToRad(32f))) return false;
		if ((cam.GlobalPosition - face).Dot(GlobalBasis.Z) < 0.05f) return false;   // (from behind the wall)
		if (OwnerCabin is { } c)
		{
			var l = c.ToLocal(cam.GlobalPosition);
			if (Mathf.Abs(l.X) > c.Width * 0.5f || Mathf.Abs(l.Z) > c.Depth * 0.5f) return false;
		}
		return true;
	}

	/// <summary>The cabin the board hangs in (null if it hangs anywhere else).</summary>
	public Cabin OwnerCabin
	{
		get
		{
			for (Node n = GetParent(); n != null; n = n.GetParent()) if (n is Cabin c) return c;
			return null;
		}
	}
}
