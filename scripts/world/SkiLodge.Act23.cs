using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;
using ProjectDS.World.LodgeParts;

namespace ProjectDS.World;

/// <summary>The lodge's story flags (Act 23).</summary>
public static class LodgeFlag
{
	public const string SnowedIn = "lodge_snowed_in", Room202Open = "lodge_202_open", Room202Jammed = "lodge_202_jammed", PantryOpen = "lodge_pantry_open",
		Room203Open = "lodge_203_open", Room204Open = "lodge_204_open", Note204 = "lodge_note_204", Room203Jammed = "lodge_203_jammed", DiningOpen = "lodge_dining_open",
		StormUp = "lodge_storm_up", Room201Open = "lodge_201_open";
	public const string Card202 = "lodge_card_202", PantryKey = "lodge_pantry_key", Card203 = "lodge_card_203", DiningKey = "lodge_dining_key", Card201 = "lodge_card_201";
	public static string Taken(string id) => Pickup.TakenFlagPrefix + id;
	/// <summary>Table t's sheet was the n-th pulled (1..6).</summary>
	public static string Table(int t, int n) => $"lodge_table_{t}_{n}";
}

/// <summary>
/// Act 23 (the owner's "Real Act 23"): the ski lodge, noon, every lamp lit and nobody here.
/// <list type="number">
/// <item>Through the back door, and behind them the roof lets its snow go and buries the doorway: snowed in.</item>
/// <item>The bar: under the counter's lip, taped there, room 202's keycard, which only the blacklight finds; a note
/// taped to its back (the closing staff's instructions: stick together, don't listen to the voices).</item>
/// <item>Room 202 (blue with the snow banked up its windows): its drawers, wardrobe and closet to search; in one
/// (a different one each game), a key wrapped in a note: the servants' pantry. Leave with it and the reader
/// sparks, the door slams and jams; stand at it and listen, and something inside is saying it's cold. Starving.</item>
/// <item>The pantry (off the lobby): a mop falls across the door as it opens. In one of its drawers, 203's card.</item>
/// <item>Room 203: the window left open, the room full of snow and ice, a voice mumbling outside. The dining room's
/// key on the sill. In the bathroom a hole broken through to 204, where a smudged note waits in a drawer.
/// Leaving, 203's door slams and jams too (only the snow coming in behind it now).</item>
/// <item>The dining hall: six long tables under white sheets, frost in the air. The sheets come off (cloth): under
/// the first, dirty dishes; the second, a smear of blood end to end, and laughter outside; the third, a headless
/// frozen skeleton, and the storm gets up; the fourth, roaches pouring off it and a wendigo's skull on a platter
/// bleeding from its mouth, and the laughter all round the room; the fifth, the table breaks and falls; the sixth,
/// always the last, 201's keycard in a bowl of snow, which melts into blood as it's taken.</item>
/// <item>Room 201: the card. (The act goes on inside 201 later; for now, the credits.)</item>
/// </list>
/// </summary>
public partial class SkiLodge
{
	public Pickup Card202Pickup { get; private set; }
	public Pickup Card201Pickup { get; private set; }
	public int TablesPulled { get; private set; }
	public float Storm { get; private set; }
	public bool Room202Jammed { get; private set; }
	public bool Room203Jammed { get; private set; }
	public int PantryKeyIn { get; private set; } = -1;
	public int Card203In { get; private set; } = -1;
	public int Note204In { get; private set; } = -1;
	public Readable Note204 { get; private set; }
	public Node3D SnowPile { get; private set; }
	public Wendigo PlatterSkull { get; private set; }
	public PickupInteractable[] SheetUses { get; } = new PickupInteractable[6];
	/// <summary>For tests: what each pull set off (in order).</summary>
	public readonly List<string> TableEvents = new();

	private Node3D _objective;
	private readonly Node3D[] _sheets = new Node3D[6];
	private readonly Node3D[] _tables = new Node3D[6];
	private AudioStreamPlayer3D _whisper202, _mumble203, _snow203;
	private AudioStreamPlayer _stormWind;
	private double _clock, _nextWhisper, _nextMumble;
	private float _stormTarget;
	private bool _act23Set, _was202, _was203;

	// ------------------------------------------------------------------ set up

	private void StartAct23()
	{
		if (_act23Set) return;
		_act23Set = true;
		var s = StoryManager.Instance;
		_objective = new Node3D { Name = "LodgeObjective" };
		AddChild(_objective);
		_objective.AddToGroup("lodge_objective_marker");
		BuildSnowPile();
		BuildBarCard();
		BuildRoom202();
		BuildPantryStory();
		BuildRoom203();
		BuildDiningStory();
		BuildRoom201();
		_whisper202 = Voice("Whisper202", new Vector3(-14.4f, UpperY + 1.3f, 3.2f), -13f, 1.2f, 6.5f);
		_mumble203 = Voice("Mumble203", new Vector3(-14.31f, UpperY + 1.2f, -InnerZ - 1.2f), -7f, 2.6f, 16f);
		_snow203 = Voice("Snow203", new Vector3(-15.5f, UpperY + 1.2f, -3.5f), -18f, 1.2f, 5.5f);
		_stormWind = new AudioStreamPlayer { Name = "StormWind", Stream = GD.Load<AudioStream>("res://assets/audio/ambient/storm_wind_loop.wav"), Bus = "Weather", VolumeDb = -60f };
		if (_stormWind.Stream is AudioStreamWav wav) wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
		AddChild(_stormWind);
		if (s != null && s.HasFlag(LodgeFlag.StormUp)) { Storm = _stormTarget = 1f; }
		SetProcess(true);
	}

	private AudioStreamPlayer3D Voice(string name, Vector3 at, float db, float unit, float maxDist)
	{
		var p = new AudioStreamPlayer3D { Name = name, Position = at, Bus = "Voice", VolumeDb = db, UnitSize = unit, MaxDistance = maxDist };
		AddChild(p);
		return p;
	}

	private static bool Has(string flag) => StoryManager.Instance?.HasFlag(flag) ?? false;
	private static bool Taken(string id) => Has(LodgeFlag.Taken(id));

	/// <summary>A find hidden in one of <paramref name="count"/> places, a different one each game (kept in the save).</summary>
	private static int Pick(string prefix, int count)
	{
		for (int i = 0; i < count; i++) if (Has($"{prefix}_{i}")) return i;
		int pick = (int)(GD.Randi() % (uint)count);
		StoryManager.Instance?.SetFlag($"{prefix}_{pick}");
		return pick;
	}

	private void ShowNote(string title, string text, Readable.NoteStyle style = Readable.NoteStyle.Handwritten)
	{
		var player = StoryBeat.Player(this);
		var r = new Readable { Name = "HeldNote", Title = title, Text = text, Style = style, Enabled = false, Visible = false };
		AddChild(r);
		NoteOverlay.Instance?.Open(r, player);
	}

	// ------------------------------------------------------------------ 1. snowed in

	/// <summary>The snow that buries the back door (outside and a tongue of it in over the sill), and a wall of it
	/// the player can't get through. Hidden until it comes down.</summary>
	private void BuildSnowPile()
	{
		SnowPile = new Node3D { Name = "SnowPile", Position = new Vector3(BackDoorX, 0, -WingHalfZ) };
		AddChild(SnowPile);
		var k = new MeshKit();
		// the woods' own snow (Poly Haven's, world-mapped), not the roofs' glazed look: packed hard into the
		// doorway to the lintel, heaped outside, and a soft fan of it spilled in over the sill with loose clumps
		k.Mat(WinterWoods.SoftSnow);
		k.Color = Colors.White;
		k.Blob(new Vector3(0, 0, -1.3f), new Vector3(2.1f, 2.7f, 1.7f), 2301, 0.18f, true, 1f);
		k.Blob(new Vector3(0.05f, 0, -0.4f), new Vector3(0.78f, 2.35f, 0.62f), 2302, 0.12f, true, 1f);   // the doorway, packed full
		k.Blob(new Vector3(-0.05f, 0, 0.45f), new Vector3(1.05f, 0.62f, 0.85f), 2303, 0.2f, true, 1f);   // spilled in over the sill
		k.Blob(new Vector3(0.25f, 0, 1.15f), new Vector3(0.7f, 0.16f, 0.55f), 2304, 0.3f, true, 1f);     // the fan's thin edge
		k.Blob(new Vector3(-0.75f, 0, 0.95f), new Vector3(0.16f, 0.12f, 0.14f), 2305, 0.3f, true, 1f);   // clumps thrown in
		k.Blob(new Vector3(0.7f, 0, 1.4f), new Vector3(0.12f, 0.09f, 0.1f), 2306, 0.3f, true, 1f);
		k.CommitTo(SnowPile, "Mesh", false);
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "snow");
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, 1.3f, -0.3f), Shape = new BoxShape3D { Size = new Vector3(1.6f, 2.6f, 1.2f) } });
		SnowPile.AddChild(body);
		bool snowed = Has(LodgeFlag.SnowedIn) || (StoryManager.Instance?.Current ?? Checkpoint.None) >= Checkpoint.Act22Finished;
		SnowPile.Visible = snowed;
		if (!snowed) foreach (var c in body.GetChildren()) if (c is CollisionShape3D cs) cs.Disabled = true;
		SnowPile.Scale = snowed ? Vector3.One : new Vector3(1f, 0.01f, 1f);
	}

	/// <summary>In through the back door: they turn at a rumble overhead, and the roof's snow comes down and buries
	/// the doorway behind them, a spray of it blowing in. Snowed in.</summary>
	private async Task SnowedIn(PlayerController player, CancellationToken ct)
	{
		StartAct23();
		await Cutscene.Wait(this, 0.8, ct);
		AudioDirector.OneShot(this, "snow_bury", 1, BackDoorWorld + Vector3.Up * 3f, 2f, "Events", 8f, 0.02f);
		await Cutscene.Wait(this, 0.4, ct);
		await StoryBeat.PanTowards(this, player, BackDoorWorld + Vector3.Up * 0.2f, 0.9f, ct);
		SnowPile.Visible = true;
		var tw = CreateTween();
		tw.TweenProperty(SnowPile, "scale", Vector3.One, 1.3f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		foreach (var c in SnowPile.GetNode("Body").GetChildren()) if (c is CollisionShape3D cs) cs.Disabled = false;
		var spray = new GpuParticles3D
		{
			Name = "Spray", Amount = 200, Lifetime = 1.8f, OneShot = true, Explosiveness = 0.8f, Position = new Vector3(BackDoorX, 1.2f, -WingHalfZ + 0.3f),
			ProcessMaterial = new ParticleProcessMaterial
			{
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.5f, 1f, 0.1f),
				Direction = new Vector3(0, 0.1f, 1f), Spread = 40f, InitialVelocityMin = 1f, InitialVelocityMax = 3f, Gravity = new Vector3(0, -2f, 0), ScaleMin = 0.6f, ScaleMax = 1.4f,
			},
			DrawPass1 = new QuadMesh { Size = new Vector2(0.05f, 0.05f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.88f, 0.94f, 0.85f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles } },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(spray);
		spray.Emitting = true;
		await Cutscene.Tween(this, tw, ct);
		StoryManager.Instance?.SetFlag(LodgeFlag.SnowedIn);
		GD.Print("[story] Act 23: the snow comes off the roof and buries the back door - snowed in");
		await Cutscene.Wait(this, 1.2, ct);
		_ = StoryBeat.Caption(this, "Snowed in.", 0.4f, 2.2f, 1f);
	}

	// ------------------------------------------------------------------ 2. the bar: 202's card, under the counter

	private void BuildBarCard()
	{
		Card202Pickup = new Pickup { Name = "Card202", Kind = ToolKind.Keycard202, TakenId = LodgeFlag.Card202, UseSpot = false, SnapToSurface = false, Position = BarCardSpot.Position, Rotation = new Vector3(Mathf.Pi, 0.4f, 0) };
		AddChild(Card202Pickup);
		// only the blacklight finds it: a smeared handprint and an arrow on the counter's front, pointing under, and the
		// card's edge glowing where it's taped
		UvInk.OnSurface(this, new Vector3(BarCounterX + 0.025f, 0.72f, 3.9f), Vector3.Right, new Vector2(0.22f, 0.22f), UvInk.Hand(), UvInk.Green, 1.8f, 0.3f);
		UvInk.Write(this, new Vector3(BarCounterX + 0.025f, 0.42f, 3.9f), Vector3.Right, "UNDER", 0.34f, UvInk.Green, 0f, 1.8f);
		UvInk.OnSurface(this, BarCardSpot.Position + new Vector3(0, -0.006f, 0), Vector3.Down, new Vector2(0.1f, 0.07f), UvInk.Smear(3), UvInk.Pale, 2.2f);
		Callable.From(() =>
		{
			if (Card202Pickup == null || !IsInstanceValid(Card202Pickup) || Card202Pickup.Use == null) return;
			var take = Card202Pickup.Use.CanUse;
			Card202Pickup.Use.CanUse = p => UvLit(Card202Pickup) && (take?.Invoke(p) ?? true);
			var prompt = Card202Pickup.Use.PromptFor;
			Card202Pickup.Use.PromptFor = p => UvLit(Card202Pickup) ? "Peel the card off" : "";
			Card202Pickup.Use.PickRadius = 0.12f;
		}).CallDeferred();
		if (StoryManager.Instance is { } s) s.FlagSet += OnFlag;
	}

	/// <summary>Is the blacklight on this (and near enough to see under the lip)?</summary>
	public static bool UvLit(Node3D n) => n != null && IsInstanceValid(n) && Lantern.UvOn(n.GlobalPosition) > 0.25f;

	private void OnFlag(string flag)
	{
		if (flag == LodgeFlag.Taken(LodgeFlag.Card202))
		{
			GD.Print("[story] Act 23: 202's keycard, peeled from under the bar counter - a note taped to its back");
			ShowNote("Taped to the back of the card",
				"For our closing staff going on holiday break, remember to lock up tight. Always stick together in pairs and do not listen to any strange noises or voices outside or inside of the Lodge.\n\nStay safe, have a happy holiday season, and don't forget to go through the proper shut down procedure.");
		}
		else if (flag == LodgeFlag.Taken(LodgeFlag.PantryKey))
		{
			GD.Print("[story] Act 23: a key wrapped in a note - the servants' pantry");
			ShowNote("Wrapped round a key", "Servant's Pantry key.");
		}
		else if (flag == LodgeFlag.Taken(LodgeFlag.Card203)) GD.Print("[story] Act 23: 203's keycard, in a pantry drawer");
		else if (flag == LodgeFlag.Taken(LodgeFlag.DiningKey)) GD.Print("[story] Act 23: the dining room's key, from 203's windowsill");
		else if (flag == LodgeFlag.Taken(LodgeFlag.Card201)) _ = MeltToBlood();
	}

	public override void _ExitTree()
	{
		if (StoryManager.Instance is { } s) s.FlagSet -= OnFlag;
	}

	// ------------------------------------------------------------------ 3. room 202

	private void BuildRoom202()
	{
		var d = RoomDoors[202];
		PantryKeyIn = Pick("lodge_202_key", Search202.Count);
		for (int i = 0; i < Search202.Count; i++)
		{
			var sr = Search202[i];
			if (i != PantryKeyIn) continue;
			sr.HasSomething = !Taken(LodgeFlag.PantryKey);
			sr.OnOpened = s2 => { if (!Taken(LodgeFlag.PantryKey)) Place(s2, ToolKind.PantryKey, LodgeFlag.PantryKey); };
		}
		d.Opened += _ => StoryManager.Instance?.SetFlag(LodgeFlag.Room202Open);
		if (Has(LodgeFlag.Room202Jammed)) { Room202Jammed = true; d.SlamAndJam(instant: true); }
		else if (Has(LodgeFlag.Room202Open)) d.Open(null, instant: true);
	}

	/// <summary>Puts a find in an opened drawer or cabinet (a pickup sitting in it).</summary>
	private void Place(Searchable s, ToolKind kind, string id)
	{
		var at = s.Type == Searchable.Kind.Drawer ? new Vector3(0.05f, -0.05f, 0.02f) : new Vector3(0.4f, 1.0f, -0.3f);
		var p = new Pickup { Name = "Find", Kind = kind, TakenId = id, UseSpot = false, SnapToSurface = false, Position = at };
		s.Part.AddChild(p);
	}

	private bool InRoom(int num, Vector3 l)
	{
		if (l.Y < UpperY - 0.5f || l.Y > RoomTop) return false;
		bool front = num == 201 || num == 202;
		bool east = num == 202 || num == 203;
		float x0 = east ? RoomSplitX : CorrX0, x1 = east ? CorrX1 : RoomSplitX;
		// 203 and 204 run into each other through the hole
		return l.X > x0 && l.X < x1 && (front ? l.Z > CorrHalf && l.Z < InnerZ : l.Z < -CorrHalf && l.Z > -InnerZ);
	}

	// ------------------------------------------------------------------ 4. the pantry

	private void BuildPantryStory()
	{
		Card203In = Pick("lodge_pantry_card", SearchPantry.Count);
		var sr = SearchPantry[Card203In];
		sr.HasSomething = !Taken(LodgeFlag.Card203);
		sr.OnOpened = s2 => { if (!Taken(LodgeFlag.Card203)) Place(s2, ToolKind.Keycard203, LodgeFlag.Card203); };
		PantryDoor.Opened += _ => { StoryManager.Instance?.SetFlag(LodgeFlag.PantryOpen); MopFalls(); };
		if (Has(LodgeFlag.PantryOpen)) { PantryDoor.Open(null, instant: true); Mop.Rotation = new Vector3(0, 0, -1.48f); }
	}

	/// <summary>The false scare: as the pantry door swings in, the mop that was leaning by it comes down across the
	/// doorway at their feet, the handle cracking on the tiles.</summary>
	private void MopFalls()
	{
		GD.Print("[story] Act 23: the pantry door opens - the mop falls");
		var tw = CreateTween();
		tw.TweenInterval(0.25f);
		tw.TweenProperty(Mop, "rotation", new Vector3(0, 0, -1.48f), 0.42f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		tw.TweenCallback(Callable.From(() => AudioDirector.OneShot(this, "mop_clatter", 1, ToGlobal(new Vector3(-9f, 0.3f, -6.1f)), 3f, "Events", 4f, 0.03f)));
		tw.TweenProperty(Mop, "rotation", new Vector3(0, 0, -1.4f), 0.08f);
		tw.TweenProperty(Mop, "rotation", new Vector3(0, 0, -1.48f), 0.12f);
		MopFell = true;
	}

	public bool MopFell { get; private set; }

	// ------------------------------------------------------------------ 5. rooms 203 and 204

	private void BuildRoom203()
	{
		var d = RoomDoors[203];
		d.Opened += _ => StoryManager.Instance?.SetFlag(LodgeFlag.Room203Open);
		if (!Taken(LodgeFlag.DiningKey))
		{
			var key = new Pickup { Name = "DiningKey", Kind = ToolKind.DiningKey, TakenId = LodgeFlag.DiningKey, UseSpot = false, SnapToSurface = false, Position = SillSpot203.Position };
			AddChild(key);
		}
		// 204: the note in one of its drawers, smudged
		Note204In = Pick("lodge_204_note", Search204.Count);
		var sr = Search204[Note204In];
		sr.HasSomething = true;
		sr.OnOpened = s2 =>
		{
			var at = s2.Type == Searchable.Kind.Drawer ? new Vector3(0, -0.06f, 0.02f) : new Vector3(0.45f, 1.0f, -0.3f);
			Note204 = PaperKit.Flat(s2.Part, at, 12f, new Vector2(0.12f, 0.16f), PaperKit.Look.Note, "", "If you can hear it, it already knows you are here.", Readable.NoteStyle.Smudged, "Read the note", 204);
			Note204.ReadFlag = LodgeFlag.Note204;
			Note204.Read += _ => GD.Print("[story] Act 23: a smudged note in 204 - \"If you can hear it, it already knows you are here.\"");
		};
		var d204 = RoomDoors[204];
		d204.Opened += _ => StoryManager.Instance?.SetFlag(LodgeFlag.Room204Open);
		if (Has(LodgeFlag.Room203Jammed)) { Room203Jammed = true; d.SlamAndJam(instant: true); }
		else if (Has(LodgeFlag.Room203Open)) d.Open(null, instant: true);
		if (Has(LodgeFlag.Room204Open)) d204.Open(null, instant: true);
	}

	// ------------------------------------------------------------------ 6. the dining hall

	private void BuildDiningStory()
	{
		DiningDoorL.Opened += _ => { StoryManager.Instance?.SetFlag(LodgeFlag.DiningOpen); GD.Print("[story] Act 23: the dining room's doors open - frost in the air"); };
		if (Has(LodgeFlag.DiningOpen)) { DiningDoorL.Open(null, instant: true); DiningDoorR.Open(null, instant: true); }
		for (int t = 0; t < 6; t++)
		{
			_tables[t] = BuildTable(t);
			int pulledAs = 0;
			for (int n = 1; n <= 6; n++) if (Has(LodgeFlag.Table(t, n))) pulledAs = n;
			if (pulledAs > 0)
			{
				TablesPulled = Mathf.Max(TablesPulled, pulledAs);
				Reveal(t, pulledAs, instant: true);
				continue;
			}
			_sheets[t] = Drape(t);
			int idx = t;
			SheetUses[t] = new PickupInteractable
			{
				Name = $"Sheet{t}", PickRadius = 0.9f, MaxDistance = 2.6f, Position = TableCentre(t) + new Vector3(0, TableH + 0.1f, 0),
				PromptFor = _ => "Pull the sheet off", CanUse = _ => _sheets[idx] != null && !_pulling,
			};
			SheetUses[t].Interacted += p => { if (_sheets[idx] != null && !_pulling) _ = Cutscene.Run(this, ct => PullSheet(idx, p, ct), lockInput: true); };
			AddChild(SheetUses[t]);
		}
	}

	/// <summary>A dining table as its own node (so one can fall apart): the top, the legs, collision.</summary>
	private Node3D BuildTable(int t)
	{
		var n = new Node3D { Name = $"Table{t}", Position = TableCentre(t) with { Y = FloorY } };
		AddChild(n);
		var k = new MeshKit();
		LodgeKit.Table(k, Vector3.Zero, 0f, new Vector2(TableLen, TableW), TableH, LodgeTextures.DarkWoodMat, LodgeTextures.DarkWoodMat);
		k.CommitTo(n, "Mesh", true);
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, TableH * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(TableLen, TableH, TableW) } });
		n.AddChild(body);
		return n;
	}

	private const int SheetNx = 36, SheetNz = 9;

	/// <summary>The sheet's shape at rest: flat over the top (lumps where things lie under it), hanging to 0.6 m past
	/// the edges, folded where it hangs. In the table's space, from its top.</summary>
	private static Vector3[,] SheetGrid(int t)
	{
		var pos = new Vector3[SheetNx + 1, SheetNz + 1];
		float hx = TableLen * 0.5f + 0.4f, hz = TableW * 0.5f + 0.4f;
		var rng = new RandomNumberGenerator { Seed = (ulong)(t * 31 + 7) };
		var lumps = new List<Vector3>();
		for (int i = 0; i < 4; i++) lumps.Add(new Vector3(rng.RandfRange(-3f, 3f), rng.RandfRange(0.04f, 0.12f), rng.RandfRange(-0.4f, 0.4f)));
		for (int i = 0; i <= SheetNx; i++)
			for (int j = 0; j <= SheetNz; j++)
			{
				float x = Mathf.Lerp(-hx, hx, i / (float)SheetNx), z = Mathf.Lerp(-hz, hz, j / (float)SheetNz);
				float over = Mathf.Max(Mathf.Abs(x) - TableLen * 0.5f, 0f) + Mathf.Max(Mathf.Abs(z) - TableW * 0.5f, 0f);
				// flat a hand past the edge before it falls (the grid's coarse: falling from the edge itself, the straight
				// span to the next row cut under the table top's edge and the wood showed through the linen)
				float y = TableH + 0.012f - Mathf.Max(over - 0.15f, 0f) * 1.5f;
				foreach (var l in lumps) y += l.Y * Mathf.Max(0f, 1f - new Vector2(x - l.X, z - l.Z).Length() / 0.45f);
				y += 0.02f * Mathf.Sin(x * 9f + z * 4f) * Mathf.Clamp(over * 5f, 0f, 1f);
				pos[i, j] = new Vector3(x * (1f - Mathf.Clamp(over, 0f, 0.3f) * 0.05f), Mathf.Max(y, TableH - 0.6f), z);
			}
		return pos;
	}

	private Node3D Drape(int t)
	{
		var n = new Node3D { Name = $"Sheet{t}", Position = TableCentre(t) with { Y = FloorY } };
		AddChild(n);
		var pos = SheetGrid(t);
		var k = new MeshKit();
		k.Mat(LodgeTextures.LinenMat);
		k.Color = Colors.White;
		for (int i = 0; i < SheetNx; i++)
			for (int j = 0; j < SheetNz; j++)
			{
				Vector3 a = pos[i, j], b = pos[i + 1, j], c = pos[i + 1, j + 1], d = pos[i, j + 1];
				Vector3 nn = (b - a).Cross(d - a).Normalized();
				if (nn.Y < 0) k.Quad(a, d, c, b, -nn); else k.Quad(a, b, c, d, nn);
			}
		k.CommitTo(n, "Drape", true);
		return n;
	}

	private ArrayMesh SheetClothMesh(int t)
	{
		var pos = SheetGrid(t);
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		for (int i = 0; i <= SheetNx; i++)
			for (int j = 0; j <= SheetNz; j++) { st.SetUV(new Vector2(i / (float)SheetNx, j / (float)SheetNz)); st.AddVertex(pos[i, j]); }
		for (int i = 0; i < SheetNx; i++)
			for (int j = 0; j < SheetNz; j++)
			{
				int a = i * (SheetNz + 1) + j, b = (i + 1) * (SheetNz + 1) + j, c = b + 1, d = a + 1;
				st.AddIndex(a); st.AddIndex(b); st.AddIndex(c);
				st.AddIndex(a); st.AddIndex(c); st.AddIndex(d);
			}
		st.GenerateNormals();
		st.SetMaterial(LodgeTextures.SheetLinenMat);
		return st.Commit();
	}

	private bool _pulling;

	/// <summary>A sheet comes off (cloth: the still drape becomes a soft body, a handful of its near edge is drawn up
	/// and off toward the player, then let go; it falls in a heap and is set down as a still mesh again). What was
	/// under it depends on how many have come off before: the n-th sheet is the n-th thing, whichever table.</summary>
	private async Task PullSheet(int t, PlayerController player, CancellationToken ct)
	{
		_pulling = true;
		try
		{
			var sheet = _sheets[t];
			_sheets[t] = null;
			if (SheetUses[t] != null) SheetUses[t].Enabled = false;
			int n = ++TablesPulled;
			StoryManager.Instance?.SetFlag(LodgeFlag.Table(t, n));
			await StoryBeat.PanTowards(this, player, ToGlobal(TableCentre(t) + new Vector3(0, TableH, 0)), 0.5f, ct);
			// what's under it is there before it lifts
			Reveal(t, n, instant: false, soon: true);
			AudioDirector.OneShot(this, "cloth", 4, sheet.GlobalPosition + Vector3.Up, -2f, "Events", 3f, 0.08f);
			var toward = (player.GlobalPosition - sheet.GlobalPosition) with { Y = 0 };
			var towardLocal = sheet.GlobalBasis.Inverse() * toward.Normalized();
			int gj = towardLocal.Z >= 0 ? SheetNz : 0;
			float px = Mathf.Clamp(sheet.ToLocal(player.GlobalPosition).X, -TableLen * 0.4f, TableLen * 0.4f);
			int gi = Mathf.Clamp(Mathf.RoundToInt((px / (TableLen + 0.8f) + 0.5f) * SheetNx), 3, SheetNx - 3);
			var grips = new List<int>();
			for (int d = -4; d <= 4; d++) grips.Add(Mathf.Clamp(gi + d, 0, SheetNx) * (SheetNz + 1) + gj);
			var mesh = SheetClothMesh(t);
			Vector3 gripLocal = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array()[grips[4]];
			var hand = new Node3D { Name = "Hand", Position = gripLocal };
			sheet.AddChild(hand);
			var cloth = new SoftBody3D
			{
				Name = "Cloth", Mesh = mesh, SimulationPrecision = 10, TotalMass = 0.6f, LinearStiffness = 1f,
				PressureCoefficient = 0f, DampingCoefficient = 0.03f, DragCoefficient = 0.01f, CollisionLayer = 0, CollisionMask = 1, RayPickable = false,
			};
			sheet.AddChild(cloth);
			foreach (var c in sheet.GetChildren()) if (c is MeshInstance3D m && m != cloth) m.Visible = false;
			foreach (int g in grips) cloth.SetPointPinned(g, true, cloth.GetPathTo(hand));
			Vector3 dir = new(0, 0, Mathf.Sign(towardLocal.Z == 0 ? 1 : towardLocal.Z));
			var tw = CreateTween();
			tw.TweenProperty(hand, "position", gripLocal + Vector3.Up * 0.7f + dir * 0.4f, 0.3f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
			tw.TweenProperty(hand, "position", gripLocal + dir * 2.6f + Vector3.Down * 0.3f, 0.6f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			tw.TweenInterval(0.2f);
			await Cutscene.Tween(this, tw, ct);
			foreach (int g in grips) cloth.SetPointPinned(g, false);
			_ = SettleSheet(cloth);
			GD.Print($"[story] Act 23: sheet {n} off (table {t})");
			await Cutscene.Wait(this, 0.4, ct);
		}
		finally { _pulling = false; }
		// what it sets off plays out without holding the player
		_ = Aftermath(t, TablesPulled);
	}

	private async Task SettleSheet(SoftBody3D cloth)
	{
		await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
		if (!IsInstanceValid(cloth)) return;
		if (cloth.Mesh is not ArrayMesh src) return;
		var arrays = src.SurfaceGetArrays(0);
		var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		var parent = (Node3D)cloth.GetParent();
		var inv = parent.GlobalTransform.AffineInverse();
		for (int i = 0; i < verts.Length; i++) verts[i] = inv * cloth.GetPointTransform(i);
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
		for (int i = 0; i < verts.Length; i++) { st.SetUV(uvs[i]); st.AddVertex(verts[i]); }
		foreach (int idx in arrays[(int)Mesh.ArrayType.Index].AsInt32Array()) st.AddIndex(idx);
		st.GenerateNormals();
		st.SetMaterial(LodgeTextures.SheetLinenMat);
		parent.AddChild(new MeshInstance3D { Name = "Heap", Mesh = st.Commit() });
		cloth.QueueFree();
	}

	/// <summary>What the n-th sheet had under it (built on table t).</summary>
	private void Reveal(int t, int n, bool instant, bool soon = false)
	{
		var c = TableCentre(t) + new Vector3(0, TableH + FloorY, 0);
		var holder = new Node3D { Name = $"Under{t}", Position = c };
		AddChild(holder);
		var k = new MeshKit();
		k.Color = Colors.White;
		switch (n)
		{
			case 1: Dishes(k); break;
			case 2: BloodStreak(k); break;
			case 3: Skeleton(k); break;
			case 4: Platter(k, holder, instant); break;
			case 5: if (instant) { _tables[t].Visible = false; Wreckage(k); } break;
			case 6: SnowBowl(k, holder, instant); break;
		}
		if (!k.IsEmpty) k.CommitTo(holder, "Mesh", false);
		if (n == 5 && !instant) holder.Visible = false;
	}

	private async Task Aftermath(int t, int n)
	{
		var mid = ToGlobal(TableCentre(t) + Vector3.Up * 1.5f);
		switch (n)
		{
			case 1:
				TableEvents.Add("dishes");
				GD.Print("[story] Act 23: under the first sheet - dirty dishes, left for weeks");
				break;
			case 2:
				TableEvents.Add("blood");
				GD.Print("[story] Act 23: under the second - blood, dragged end to end; laughing, outside");
				await Wait(1.2);
				Laugh("wendigo_laugh_01", ToGlobal(new Vector3(30f, 3f, InnerZ + 6f)), -2f, false);
				break;
			case 3:
				TableEvents.Add("skeleton");
				GD.Print("[story] Act 23: under the third - a frozen skeleton, no head; the storm gets up");
				await Wait(1.0);
				StormUp();
				await Wait(2.0);
				Laugh("wendigo_laugh_02", ToGlobal(new Vector3(40f, 3f, -InnerZ - 6f)), -10f, false);
				break;
			case 4:
				TableEvents.Add("skull");
				GD.Print("[story] Act 23: under the fourth - roaches pour off it; a wendigo's skull on a platter, bleeding; the laughing all round the room");
				Roaches(t);
				await Wait(1.6);
				Laugh("wendigo_laugh_03", mid, 4f, true);
				break;
			case 5:
				TableEvents.Add("collapse");
				GD.Print("[story] Act 23: the fifth - the table breaks and comes down");
				Collapse(t);
				break;
			case 6:
				TableEvents.Add("card");
				GD.Print("[story] Act 23: the sixth - 201's keycard in a bowl of snow");
				break;
		}
	}

	private async Task Wait(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

	// ---- what's under them

	private static void Dishes(MeshKit k)
	{
		var rng = new RandomNumberGenerator { Seed = 5101 };
		for (float x = -3f; x <= 3f; x += 0.85f)
			foreach (float z in new[] { -0.35f, 0.35f })
			{
				if (rng.Randf() < 0.2f) continue;
				Vector3 p = new(x + rng.RandfRange(-0.1f, 0.1f), 0.005f, z + rng.RandfRange(-0.06f, 0.06f));
				k.Mat(LodgeTextures.PorcelainMat);
				k.Cylinder(p, p + Vector3.Up * 0.02f, 0.13f, 0.11f, 14, true);
				k.Mat(LodgeTextures.LeatherMat);   // scraps, gone dark
				k.Blob(p + Vector3.Up * 0.03f, new Vector3(0.06f, 0.02f, 0.05f), 5102 + (int)(x * 10), 0.4f, true, 1f);
				k.Mat(LodgeTextures.IronMat);
				k.Box(p + new Vector3(0.17f, 0.005f, 0), new Vector3(0.015f, 0.006f, 0.18f), 1f);
				if (rng.Randf() < 0.5f)
				{
					k.Mat(LodgeTextures.PorcelainMat);
					k.Cylinder(p + new Vector3(-0.1f, 0, -z * 0.5f), p + new Vector3(-0.1f, 0.08f, -z * 0.5f), 0.035f, 0.04f, 8, false);
				}
			}
		// a glass on its side, a wine stain
		k.Mat(StationParts.StationTextures.BloodPoolMat);
		k.Quad(new Vector3(0.9f, 0.004f, 0.3f), new Vector3(1.3f, 0.004f, 0.3f), new Vector3(1.3f, 0.004f, -0.05f), new Vector3(0.9f, 0.004f, -0.05f), Vector3.Up);
	}

	private static StandardMaterial3D _wetBlood;
	private static StandardMaterial3D WetBlood => _wetBlood ??= new StandardMaterial3D { ResourceName = "lodge_wetblood", AlbedoColor = new Color(0.42f, 0.02f, 0.015f), Roughness = 0.12f, MetallicSpecular = 0.9f, AlbedoTexture = ProcTextures.Grime() };

	private static void BloodStreak(MeshKit k)
	{
		k.Mat(WetBlood);
		var rng = new RandomNumberGenerator { Seed = 5201 };
		float prevZ = 0f;
		for (float x = -TableLen * 0.5f; x < TableLen * 0.5f - 0.2f; x += 0.3f)
		{
			float z = Mathf.Clamp(prevZ + rng.RandfRange(-0.05f, 0.05f), -0.2f, 0.2f), w = 0.2f + 0.07f * Mathf.Sin(x * 3f) + rng.RandfRange(-0.02f, 0.02f);
			k.Quad(new Vector3(x, 0.004f, prevZ + w), new Vector3(x + 0.3f, 0.004f, z + w), new Vector3(x + 0.3f, 0.004f, z - w), new Vector3(x, 0.004f, prevZ - w), Vector3.Up,
				new Vector2(0, 0), new Vector2(0.3f, 0), new Vector2(0.3f, 1), new Vector2(0, 1));
			prevZ = z;
		}
		// it runs off the end and down to the floor
		k.Mat(StationParts.StationTextures.BloodDripMat);
		k.Quad(new Vector3(TableLen * 0.5f - 0.01f, 0.0f, 0.12f), new Vector3(TableLen * 0.5f - 0.01f, 0.0f, -0.12f), new Vector3(TableLen * 0.5f + 0.01f, -TableH, -0.12f), new Vector3(TableLen * 0.5f + 0.01f, -TableH, 0.12f), Vector3.Right);
	}

	/// <summary>A frozen skeleton laid out on the table, no skull: the ribcage and spine, pelvis, the long bones; dirt
	/// and snow clinging to it, a glaze of ice.</summary>
	private static void Skeleton(MeshKit k)
	{
		k.Mat(WinterWoods.Bone);
		k.Color = new Color(0.8f, 0.76f, 0.66f);
		for (int v = 0; v < 18; v++) k.Blob(new Vector3(-0.9f + v * 0.075f, 0.04f, 0), new Vector3(0.03f, 0.025f, 0.03f), 5300 + v, 0.2f, false, 1f);
		for (int r = 0; r < 9; r++)
		{
			float x = -0.7f + r * 0.07f;
			for (int s = -1; s <= 1; s += 2)
			{
				Vector3 prev = new(x, 0.05f, 0);
				for (int j = 1; j <= 5; j++)
				{
					float a = j / 5f * 1.4f;
					var p = new Vector3(x + j * 0.01f, 0.05f + Mathf.Sin(a) * 0.1f, s * Mathf.Sin(a * 0.9f) * 0.16f);
					k.Cylinder(prev, p, 0.008f, 0.008f, 4, false);
					prev = p;
				}
			}
		}
		k.Blob(new Vector3(0.45f, 0.05f, 0), new Vector3(0.12f, 0.05f, 0.18f), 5320, 0.2f, false, 1f);   // the pelvis
		foreach (float s in new[] { -1f, 1f })
		{
			k.Cylinder(new Vector3(0.5f, 0.04f, s * 0.1f), new Vector3(0.95f, 0.04f, s * 0.13f), 0.022f, 0.018f, 6, true);
			k.Cylinder(new Vector3(0.95f, 0.04f, s * 0.13f), new Vector3(1.38f, 0.035f, s * 0.14f), 0.018f, 0.014f, 6, true);
			k.Cylinder(new Vector3(-0.85f, 0.05f, s * 0.2f), new Vector3(-0.5f, 0.04f, s * 0.3f), 0.018f, 0.015f, 6, true);
			k.Cylinder(new Vector3(-0.5f, 0.04f, s * 0.3f), new Vector3(-0.2f, 0.035f, s * 0.34f), 0.014f, 0.012f, 6, true);
		}
		// the neck ends in nothing: a snapped vertebra
		k.Color = new Color(0.55f, 0.42f, 0.32f);
		k.Blob(new Vector3(-1.0f, 0.04f, 0), new Vector3(0.03f, 0.02f, 0.03f), 5330, 0.5f, false, 1f);
		// dirt and snow clinging, ice over it
		k.Mat(BuildingTextures.StoneMat);
		k.Color = new Color(0.35f, 0.27f, 0.2f);
		var rng = new RandomNumberGenerator { Seed = 5340 };
		for (int i = 0; i < 18; i++) k.Blob(new Vector3(rng.RandfRange(-1f, 1.3f), 0.03f, rng.RandfRange(-0.3f, 0.3f)), new Vector3(0.05f, 0.02f, 0.05f), 5341 + i, 0.4f, true, 1f);
		k.Mat(WinterWoods.SoftSnow);
		k.Color = Colors.White;
		for (int i = 0; i < 10; i++) k.Blob(new Vector3(rng.RandfRange(-1f, 1.3f), 0.05f, rng.RandfRange(-0.3f, 0.3f)), new Vector3(0.07f, 0.03f, 0.06f), 5360 + i, 0.3f, true, 1f);
		k.Mat(WinterWoods.IceMat);
		k.Blob(new Vector3(0.1f, 0.01f, 0), new Vector3(1.3f, 0.02f, 0.4f), 5380, 0.3f, true, 1f);
	}

	/// <summary>A silver platter, and on it the head of a wendigo (its elk skull, its man's jaw, one antler snapped),
	/// blood pouring from its mouth and spreading over the plate and the linen.</summary>
	private void Platter(MeshKit k, Node3D holder, bool instant)
	{
		k.Mat(LodgeTextures.MirrorMat);
		k.Cylinder(Vector3.Zero, new Vector3(0, 0.025f, 0), 0.5f, 0.45f, 20, true);
		k.Mat(StationParts.StationTextures.BloodPoolMat);
		k.Cylinder(new Vector3(0.05f, 0.026f, -0.1f), new Vector3(0.05f, 0.028f, -0.1f), instant ? 0.42f : 0.2f, instant ? 0.42f : 0.2f, 14, true);
		PlatterSkull = new Wendigo { Name = "Skull", Position = new Vector3(0, 0.18f, 0.05f), Scale = Vector3.One * 0.85f };
		holder.AddChild(PlatterSkull);
		PlatterSkull.ShowOnlyHead();
		PlatterSkull.Rotation = new Vector3(0, 0.6f, 0);
		if (instant) return;
		var blood = new GpuParticles3D
		{
			Name = "Blood", Amount = 80, Lifetime = 0.9f, LocalCoords = false,
			ProcessMaterial = new ParticleProcessMaterial
			{
				Direction = new Vector3(0, -1, -0.2f), Spread = 10f, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.5f, Gravity = new Vector3(0, -6f, 0),
				ScaleMin = 0.6f, ScaleMax = 1.4f, Color = new Color(0.35f, 0.02f, 0.02f),
			},
			DrawPass1 = new SphereMesh { Radius = 0.012f, Height = 0.024f, RadialSegments = 4, Rings = 2, Material = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.02f, 0.02f), Roughness = 0.1f, MetallicSpecular = 0.8f } },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		holder.AddChild(blood);
		Callable.From(() => { if (IsInstanceValid(PlatterSkull) && IsInstanceValid(blood)) blood.GlobalPosition = PlatterSkull.MouthWorld; }).CallDeferred();
		AudioDirector.OneShot(this, "blood_drain", 1, holder.GlobalPosition, -10f, "Events", 3f, 0.05f);
	}

	/// <summary>Little roaches pouring off the table, in every direction, and gone over its edges.</summary>
	private void Roaches(int t)
	{
		var c = TableCentre(t) + new Vector3(0, TableH + FloorY + 0.01f, 0);
		var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = 60 };
		var body = new MeshKit();
		body.Mat(LodgeTextures.Glow("lodge_roach", new Color(0.12f, 0.06f, 0.03f), 0.02f));
		body.Color = Colors.White;
		body.Blob(Vector3.Zero, new Vector3(0.012f, 0.006f, 0.022f), 5400, 0.1f, false, 1f);
		mm.Mesh = body.Commit();
		var mi = new MultiMeshInstance3D { Name = "Roaches", Multimesh = mm, Position = c };
		AddChild(mi);
		var rng = new RandomNumberGenerator { Seed = 5401 };
		var starts = new Vector3[60]; var dirs = new Vector3[60]; var speeds = new float[60];
		for (int i = 0; i < 60; i++)
		{
			starts[i] = new Vector3(rng.RandfRange(-0.4f, 0.4f), 0, rng.RandfRange(-0.2f, 0.2f));
			float a = rng.RandfRange(0, Mathf.Tau);
			dirs[i] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
			speeds[i] = rng.RandfRange(0.6f, 1.4f);
		}
		AudioDirector.OneShot(this, "roach_skitter", 1, ToGlobal(c), -2f, "Events", 3f, 0.05f);
		void Step(float time)
		{
			for (int i = 0; i < 60; i++)
			{
				var p = starts[i] + dirs[i] * speeds[i] * time + new Vector3(Mathf.Sin(time * 20f + i) * 0.01f, 0, 0);
				bool off = Mathf.Abs(p.X) > TableLen * 0.5f || Mathf.Abs(p.Z) > TableW * 0.5f;
				if (off) p.Y = -Mathf.Min((Mathf.Abs(p.X) - TableLen * 0.5f + Mathf.Abs(p.Z) - TableW * 0.5f) * 3f, TableH);
				var basis = Basis.LookingAt(dirs[i], Vector3.Up);
				mm.SetInstanceTransform(i, new Transform3D(time > 2.6f ? basis.Scaled(Vector3.Zero) : basis, p));
			}
		}
		Step(0f);
		var tw = CreateTween();
		tw.TweenMethod(Callable.From<float>(v => Step(v * 2.8f)), 0f, 1f, 2.8f);
		tw.TweenCallback(Callable.From(mi.QueueFree));
	}

	private void Collapse(int t)
	{
		var table = _tables[t];
		table.Visible = false;
		foreach (var cs in table.GetNode("Body").GetChildren()) if (cs is CollisionShape3D c) c.Disabled = true;
		var holder = GetNodeOrNull<Node3D>($"Under{t}");
		if (holder != null) holder.Visible = true;
		// the pieces: two halves of the top folding down into a V, the legs kicking out
		var pieces = new Node3D { Name = $"Collapse{t}", Position = TableCentre(t) with { Y = FloorY } };
		AddChild(pieces);
		foreach (float s in new[] { -1f, 1f })
		{
			var half = new Node3D { Position = new Vector3(0, TableH, 0) };
			pieces.AddChild(half);
			var k = new MeshKit();
			k.Mat(LodgeTextures.DarkWoodMat);
			k.Color = Colors.White;
			k.Box(new Vector3(s * TableLen * 0.25f, -0.03f, 0), new Vector3(TableLen * 0.5f - 0.02f, 0.06f, TableW), 1f);
			k.CommitTo(half, "Top", true);
			var tw = CreateTween();
			tw.TweenProperty(half, "position:y", 0.1f, 0.35f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			tw.Parallel().TweenProperty(half, "rotation:z", s * 0.22f, 0.35f);
			var leg = new Node3D { Position = new Vector3(s * (TableLen * 0.5f - 0.1f), 0, 0) };
			pieces.AddChild(leg);
			var lk = new MeshKit();
			lk.Mat(LodgeTextures.DarkWoodMat);
			lk.Box(new Vector3(0, 0.34f, 0), new Vector3(0.07f, 0.68f, 0.07f), 1f);
			lk.CommitTo(leg, "Leg", false);
			var lt = CreateTween();
			lt.TweenProperty(leg, "rotation:z", -s * 1.5f, 0.5f).SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
		}
		AudioDirector.OneShot(this, "table_collapse", 1, ToGlobal(TableCentre(t) + Vector3.Up * 0.6f), 6f, "Events", 6f, 0.02f);
		var dust = new GpuParticles3D
		{
			Name = "Dust", Amount = 90, Lifetime = 2f, OneShot = true, Explosiveness = 0.9f, Position = TableCentre(t) + Vector3.Up * 0.2f,
			ProcessMaterial = new ParticleProcessMaterial { EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(3.5f, 0.1f, 0.6f), Direction = Vector3.Up, Spread = 70f, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.8f, Gravity = new Vector3(0, -0.3f, 0), ScaleMin = 1f, ScaleMax = 3f, Color = new Color(0.55f, 0.5f, 0.45f, 0.4f) },
			DrawPass1 = new QuadMesh { Size = new Vector2(0.12f, 0.12f), Material = new StandardMaterial3D { AlbedoTexture = LakeParts.LakeFx.SoftDot(), VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles } },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Emitting = true,
		};
		AddChild(dust);
		Collapsed = t;
	}

	public int Collapsed { get; private set; } = -1;

	private static void Wreckage(MeshKit k)
	{
		k.Mat(LodgeTextures.DarkWoodMat);
		k.Color = Colors.White;
		foreach (float s in new[] { -1f, 1f })
			k.Box(new Vector3(s * TableLen * 0.25f, -TableH + 0.2f, 0), new Vector3(TableLen * 0.5f, 0.06f, TableW), 1f, new Basis(Vector3.Back, s * 0.22f));
	}

	private Node3D _bowlSnow;

	/// <summary>A porcelain bowl heaped with snow, and standing up out of the snow, 201's keycard.</summary>
	private void SnowBowl(MeshKit k, Node3D holder, bool instant)
	{
		k.Mat(LodgeTextures.PorcelainMat);
		k.Cylinder(Vector3.Zero, new Vector3(0, 0.1f, 0), 0.1f, 0.17f, 16, false);
		k.Cylinder(Vector3.Zero, new Vector3(0, 0.005f, 0), 0.1f, 0.1f, 16, true);
		_bowlSnow = new Node3D { Name = "BowlSnow" };
		holder.AddChild(_bowlSnow);
		var s = new MeshKit();
		bool melted = Taken(LodgeFlag.Card201);
		s.Mat(melted ? StationParts.StationTextures.BloodPoolMat : RoofSnow);
		s.Color = Colors.White;
		if (melted) s.Cylinder(new Vector3(0, 0.06f, 0), new Vector3(0, 0.065f, 0), 0.14f, 0.14f, 14, true);
		else s.Blob(new Vector3(0, 0.08f, 0), new Vector3(0.15f, 0.09f, 0.15f), 5600, 0.2f, true, 1f);
		s.CommitTo(_bowlSnow, "Snow", false);
		if (melted) return;
		Card201Pickup = new Pickup { Name = "Card201", Kind = ToolKind.Keycard201, TakenId = LodgeFlag.Card201, UseSpot = false, SnapToSurface = false, Position = new Vector3(0, 0.16f, 0), Rotation = new Vector3(Mathf.Pi * 0.5f, 0.3f, 0) };
		holder.AddChild(Card201Pickup);
	}

	/// <summary>201's card lifted out, and the snow in the bowl slumps and runs red: blood, to the brim.</summary>
	private async Task MeltToBlood()
	{
		GD.Print("[story] Act 23: 201's keycard taken - the snow in the bowl melts into blood");
		if (_bowlSnow == null || !IsInstanceValid(_bowlSnow)) return;
		var snow = _bowlSnow.GetNodeOrNull<MeshInstance3D>("Snow");
		var blood = new MeshInstance3D
		{
			Name = "Blood", Mesh = new CylinderMesh { TopRadius = 0.14f, BottomRadius = 0.1f, Height = 0.06f, RadialSegments = 16 }, Position = new Vector3(0, 0.035f, 0),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.02f, 0.02f), Roughness = 0.08f, MetallicSpecular = 0.9f }, Scale = new Vector3(1f, 0.05f, 1f),
		};
		_bowlSnow.AddChild(blood);
		var tw = CreateTween().SetParallel();
		if (snow != null) tw.TweenProperty(snow, "scale", new Vector3(0.8f, 0.05f, 0.8f), 2.4f).SetTrans(Tween.TransitionType.Sine);
		tw.TweenProperty(blood, "scale", Vector3.One, 2.6f).SetTrans(Tween.TransitionType.Sine);
		AudioDirector.OneShot(this, "haunt_drip", 3, _bowlSnow.GlobalPosition, -8f, "Events", 2f, 0.1f);
		await Wait(1.2);
		AudioDirector.OneShot(this, "blood_suck", 1, _bowlSnow.GlobalPosition, -10f, "Events", 2f, 0.05f);
		await Wait(1.6);
		if (snow != null && IsInstanceValid(snow)) snow.Visible = false;
		BowlMelted = true;
		var p = StoryBeat.Player(this);
		if (p != null) StoryBeat.ReachCheckpoint(p, Checkpoint.Act23Keycard201);
	}

	public bool BowlMelted { get; private set; }

	/// <summary>The storm gets up outside: the windows go grey and dark, the wind rises to a howl in the chimney.</summary>
	private void StormUp()
	{
		_stormTarget = 1f;
		StoryManager.Instance?.SetFlag(LodgeFlag.StormUp);
		AudioDirector.OneShot(this, "thunder", 1, ToGlobal(new Vector3(30f, 8f, 0)), -18f, "Weather", 20f, 0.05f);
	}

	/// <summary>A laugh: from a point, or moving round the dining hall (it seems to bounce from wall to wall).</summary>
	private void Laugh(string name, Vector3 at, float db, bool roams)
	{
		var p = new AudioStreamPlayer3D { Name = "Laugh", Stream = GD.Load<AudioStream>($"res://assets/audio/sfx/{name}.wav"), Bus = "Voice", VolumeDb = db, UnitSize = roams ? 12f : 10f, MaxDistance = 120f };
		AddChild(p);
		p.GlobalPosition = at;
		p.Play();
		p.Finished += p.QueueFree;
		if (!roams) return;
		// round the hall's walls, quickly, and back and forth across it
		var tw = CreateTween();
		tw.TweenMethod(Callable.From<float>(u =>
		{
			if (!IsInstanceValid(p)) return;
			float a = u * Mathf.Tau * 3.5f;
			var l = new Vector3(27f + Mathf.Cos(a) * 15f, 2.5f + Mathf.Sin(a * 1.7f), Mathf.Sin(a) * 6.5f);
			p.GlobalPosition = ToGlobal(l);
		}), 0f, 1f, 5.5f);
		// a second voice of it, the other way round
		var q = new AudioStreamPlayer3D { Name = "Laugh2", Stream = p.Stream, Bus = "Voice", VolumeDb = db - 4f, UnitSize = 10f, MaxDistance = 120f, PitchScale = 0.94f };
		AddChild(q);
		q.Play();
		q.Finished += q.QueueFree;
		var tq = CreateTween();
		tq.TweenMethod(Callable.From<float>(u =>
		{
			if (!IsInstanceValid(q)) return;
			float a = -u * Mathf.Tau * 2.5f + 2f;
			q.GlobalPosition = ToGlobal(new Vector3(27f + Mathf.Cos(a) * 14f, 3f, Mathf.Sin(a) * 6f));
		}), 0f, 1f, 5.5f);
	}

	// ------------------------------------------------------------------ 7. room 201

	private void BuildRoom201()
	{
		var d = RoomDoors[201];
		d.ConsumeKey = false;
		d.LockedPrompt = "Room 201. The reader wants a keycard.";
		// the card opens it (the act goes on inside 201 later; for now the reader turns green, the lock clicks back,
		// and it's the end of the demo)
		d.Opened += p => { StoryManager.Instance?.SetFlag(LodgeFlag.Room201Open); _ = Cutscene.Run(this, ct => End201(p, ct), lockInput: true); };
	}

	private async Task End201(PlayerController player, CancellationToken ct)
	{
		GD.Print("[story] Act 23: the card in 201's reader - green; the door opens on the dark (to be continued)");
		await Cutscene.Wait(this, 1.8, ct);
		var fader = StoryBeat.Fader(this);
		if (fader != null) await fader.Fade(1f, 3f, ct);
		if (GetTree().GetFirstNodeInGroup("act11_ending") is Act11Ending ending) await ending.Credits(fader, ct);
	}

	// ------------------------------------------------------------------ every frame

	public override void _Process(double delta)
	{
		if (!_act23Set) return;
		float dt = (float)delta;
		_clock += delta;
		var s = StoryManager.Instance;
		var player = StoryBeat.Player(this);
		if (player == null || s == null) return;
		var l = ToLocal(player.GlobalPosition);
		bool inside = InsideLocal(l);
		// the light in here: clean and warm (noon, every lamp lit), not the woods' murk
		var atmo = StoryBeat.Atmosphere(this);
		if (atmo != null) atmo.Lodge = Mathf.MoveToward(atmo.Lodge, inside ? 1f : 0f, dt * 1.2f);
		// the storm
		Storm = Mathf.MoveToward(Storm, _stormTarget, dt * 0.2f);
		var day = LodgeTextures.DayGlass;
		day.EmissionEnergyMultiplier = Mathf.Lerp(0.9f, 0.28f, Storm);
		day.Emission = new Color(0.62f, 0.68f, 0.76f).Lerp(new Color(0.34f, 0.38f, 0.45f), Storm);
		foreach (var dl in _dayLights) dl.LightEnergy = Mathf.Lerp(0.9f, 0.25f, Storm);
		if (_stormWind != null)
		{
			float want = inside ? Mathf.Lerp(-40f, -9f, Storm) : -60f;
			if (want > -55f && !_stormWind.Playing) _stormWind.Play();
			_stormWind.VolumeDb = Mathf.MoveToward(_stormWind.VolumeDb, want, dt * 10f);
		}
		if (_fireLight != null) _fireLight.LightEnergy = 1.5f + 0.2f * Mathf.Sin((float)_clock * 1.3f) + 0.1f * Mathf.Sin((float)_clock * 2.9f + 1f);
		// 202: leave with the key and it slams behind you
		bool in202 = InRoom(202, l);
		if (in202) _was202 = true;
		if (!Room202Jammed && _was202 && !in202 && Taken(LodgeFlag.PantryKey) && RoomDoors[202].Current == LodgeParts.LodgeDoor.State.Open && l.Y > UpperY - 0.5f)
		{
			Room202Jammed = true;
			s.SetFlag(LodgeFlag.Room202Jammed);
			RoomDoors[202].SlamAndJam();
			GD.Print("[story] Act 23: out of 202 with the key - the reader sparks, the door slams and jams");
			StoryBeat.ReachCheckpoint(player, Checkpoint.Act23Room202Done);
			_nextWhisper = _clock + 4.0;
		}
		// the voice behind 202's door (only close to it, and only if they stand and listen)
		if (Room202Jammed && !_whisper202.Playing && _clock >= _nextWhisper && player.GlobalPosition.DistanceTo(RoomDoors[202].CentreWorld) < 5f)
		{
			_whisper202.Stream = GD.Load<AudioStream>($"res://assets/audio/sfx/wendigo_room_{(int)(GD.Randi() % 3) + 1:00}.wav");
			_whisper202.Play();
			Whispers++;
			_nextWhisper = _clock + GD.RandRange(5.0, 10.0);
		}
		// 203's window: a voice mumbling out in the storm (once they're in there, until they're out)
		if (RoomDoors[203].Current == LodgeParts.LodgeDoor.State.Open && !Room203Jammed && !_mumble203.Playing && _clock >= _nextMumble)
		{
			_mumble203.Stream = GD.Load<AudioStream>($"res://assets/audio/sfx/wendigo_mumble_{(int)(GD.Randi() % 4) + 1:00}.wav");
			_mumble203.Play();
			Mumbles++;
			_nextMumble = _clock + GD.RandRange(3.5, 7.0);
		}
		// 204: from inside, its door opens
		bool in203or204 = InRoom(203, l) || InRoom(204, l);
		if (InRoom(204, l) && RoomDoors[204].Current == LodgeParts.LodgeDoor.State.Locked) { RoomDoors[204].Unlock(); RoomDoors[204].OpenPrompt = "Open the door"; }
		if (in203or204) _was203 = true;
		if (!Room203Jammed && _was203 && !in203or204 && Taken(LodgeFlag.DiningKey) && Has(LodgeFlag.Note204) && l.Y > UpperY - 0.5f)
		{
			Room203Jammed = true;
			s.SetFlag(LodgeFlag.Room203Jammed);
			RoomDoors[203].SlamAndJam();
			GD.Print("[story] Act 23: out of 203/204 - 203's door slams and jams too");
			StoryBeat.ReachCheckpoint(player, Checkpoint.Act23Room203Done);
		}
		// behind 203's door: only the snow coming in, if they hold still and listen
		if (Room203Jammed && _snow203 != null && !_snow203.Playing)
		{
			_snow203.Stream = GD.Load<AudioStream>("res://assets/audio/ambient/winter_wind_loop.wav");
			if (_snow203.Stream is AudioStreamWav w) w.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			_snow203.Play();
		}
		UpdateObjective();
	}

	/// <summary>For tests: how often the voice behind 202 has spoken, and the mumbling outside 203.</summary>
	public int Whispers { get; private set; }
	public int Mumbles { get; private set; }

	/// <summary>Where the compass points in the lodge.</summary>
	private void UpdateObjective()
	{
		if (_objective == null) return;
		Vector3 goal;
		if (!Taken(LodgeFlag.Card202)) goal = BarCardSpot.Position;
		else if (!Taken(LodgeFlag.PantryKey)) goal = RoomDoors[202].Current == LodgeParts.LodgeDoor.State.Open ? new Vector3(-16f, UpperY + 1f, 4.3f) : RoomDoors[202].Position + new Vector3(0.5f, 1.2f, 0);
		else if (!Taken(LodgeFlag.Card203)) goal = PantryDoor.Current == LodgeParts.LodgeDoor.State.Open ? new Vector3(-16f, 1f, -6.4f) : PantryDoor.Position + PantryDoor.Basis * new Vector3(0.55f, 1.2f, 0);
		else if (!Taken(LodgeFlag.DiningKey)) goal = RoomDoors[203].Current == LodgeParts.LodgeDoor.State.Open ? SillSpot203.Position : RoomDoors[203].Position + RoomDoors[203].Basis * new Vector3(0.5f, 1.2f, 0);
		else if (!Has(LodgeFlag.Note204)) goal = new Vector3(-23.5f, UpperY + 1f, -4f);
		else if (!Has(LodgeFlag.DiningOpen)) goal = DiningDoorL.Position + DiningDoorL.Basis * new Vector3(1.2f, 1.4f, 0);
		else if (!Taken(LodgeFlag.Card201)) goal = new Vector3(27f, 1f, 0);
		else goal = RoomDoors[201].Position + new Vector3(0.5f, 1.2f, 0);
		_objective.Position = goal;
	}
}
