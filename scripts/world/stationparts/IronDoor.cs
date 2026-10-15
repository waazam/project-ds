using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.StationParts;

/// <summary>
/// The door behind the lobby's desk. All through Act 13 it is hidden under a thick old web spun across
/// the whole back corner — just a dusty web, easy to walk past. With Room 2's lighter it burns
/// ("Burn the web"), and behind it is a riveted iron door with three hollows in a row, a word carved
/// over each: LOOK, TOUCH, CLIMB — Room 1's three rules, the other way round. Getting near it for the
/// first time reads them out (<see cref="StoryManager.Flag.StationDoor3Seen"/>), which is what wakes up
/// the scavenger hunt: the dead eye in the basement (LOOK), the hand in Room 1's blood (TOUCH), the
/// stairs in Room 2 (CLIMB). Each piece set in its hollow (E) clunks home; with all three in, the door
/// grinds open on the last room (<see cref="StationRoom3"/>).
///
/// Local space: the back wall's plane is z=0 (the lobby is at -Z); the doorway is 2.4 x 2.5 at x=0.
/// </summary>
public partial class IronDoor : Node3D
{
	public bool WebBurned { get; private set; }
	public bool Seen { get; private set; }
	public bool Opened { get; private set; }
	public Interactable WebUse => _webUse;
	public Interactable DoorUse => _doorUse;
	public int PiecesSet => (_set[0] ? 1 : 0) + (_set[1] ? 1 : 0) + (_set[2] ? 1 : 0);

	private Node3D _web, _leaf;
	private Interactable _webUse, _doorUse;
	private StaticBody3D _blocker;
	private readonly bool[] _set = new bool[3];
	private readonly Node3D[] _hollowPieces = new Node3D[3];
	private static readonly string[] Words = { "LOOK", "TOUCH", "CLIMB" };
	private static readonly ToolKind[] Needs = { ToolKind.DeadEye, ToolKind.PaleHand, ToolKind.StairTread };
	private static readonly string[] SetFlags = { StoryManager.Flag.StationEyeSet, StoryManager.Flag.StationHandSet, StoryManager.Flag.StationStepSet };

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private void Build()
	{
		var s = StoryManager.Instance;
		BuildDoor();
		BuildWeb();
		if (s != null)
		{
			if (s.HasFlag(StoryManager.Flag.StationWebBurned)) { WebBurned = true; _web.Visible = false; _webUse.Enabled = false; _doorUse.Enabled = true; }
			if (s.HasFlag(StoryManager.Flag.StationDoor3Seen)) Seen = true;
			for (int i = 0; i < 3; i++) if (s.HasFlag(SetFlags[i])) SetPiece(i, false);
			if (s.HasFlag(StoryManager.Flag.StationDoor3Open)) OpenDoor(false);
		}
		SetProcess(true);
	}

	// ------------------------------------------------------------------ the door

	private void BuildDoor()
	{
		// the dark beyond the doorway, until it opens
		_leaf = new Node3D { Name = "Leaf" };
		AddChild(_leaf);
		var k = new MeshKit();
		k.Mat(StationTextures.RustPlateMat);
		k.Color = Colors.White;
		BuildKit.Box(k, new Vector3(0, 1.25f, 0.05f), new Vector3(2.4f, 2.5f, 0.12f), 1.3f);
		// rivet bands and two heavy strap hinges
		k.Mat(BuildingTextures.IronMat);
		k.Color = new Color(0.22f, 0.2f, 0.19f);
		foreach (float y in new[] { 0.35f, 2.15f })
			BuildKit.Box(k, new Vector3(0, y, -0.02f), new Vector3(2.36f, 0.12f, 0.03f), 2f);
		for (float x = -1.1f; x <= 1.1f; x += 0.22f)
			foreach (float y in new[] { 0.35f, 2.15f })
				k.Cylinder(new Vector3(x, y, -0.036f), new Vector3(x, y, -0.05f), 0.02f, 0.014f, 6, true);
		// the three hollows, recessed in a raised iron panel at chest height
		BuildKit.Box(k, new Vector3(0, 1.35f, -0.03f), new Vector3(1.9f, 0.62f, 0.05f), 2f);
		k.Mat(StationTextures.Flat("st_hollow", new Color(0.02f, 0.015f, 0.012f), 1f, 0.05f));
		k.Color = Colors.White;
		for (int i = 0; i < 3; i++)
		{
			Vector3 c = HollowAt(i) + new Vector3(0, 0, -0.058f);
			switch (i)
			{
				case 0: ItemMeshes.Disc(k, c, Vector3.Forward, 0.12f, 16); break;                                   // an eye socket
				case 1: BuildKit.Box(k, c, new Vector3(0.16f, 0.22f, 0.004f)); break;                               // a palm
				default: BuildKit.Box(k, c, new Vector3(0.36f, 0.07f, 0.004f)); break;                              // a step
			}
		}
		k.CommitTo(_leaf, "Door", true);
		for (int i = 0; i < 3; i++)
			SignKit.Text(_leaf, Words[i], HollowAt(i) + new Vector3(0, 0.22f, -0.062f), new Basis(Vector3.Up, Mathf.Pi), 0.09f, new Color(0.62f, 0.5f, 0.4f));

		_blocker = new StaticBody3D { Name = "Blocker", CollisionLayer = 1, CollisionMask = 0 };
		_blocker.AddChild(new CollisionShape3D { Position = new Vector3(0, 1.25f, 0.05f), Shape = new BoxShape3D { Size = new Vector3(2.4f, 2.5f, 0.14f) } });
		_leaf.AddChild(_blocker);

		_doorUse = new PickupInteractable
		{
			Name = "Use", Prompt = "Three hollows in the iron", PickRadius = 0.7f, MaxDistance = 2.8f, Position = new Vector3(0, 1.35f, -0.1f),
			PromptFor = PromptFor, Enabled = false,
			Outline = true,   // (the white outline: a door, but the three hollows are the puzzle, not the way through)
		};
		_doorUse.Interacted += OnDoorUsed;
		_leaf.AddChild(_doorUse);
	}

	private static Vector3 HollowAt(int i) => new((i - 1) * 0.6f, 1.33f, 0);

	private string PromptFor(PlayerController p)
	{
		for (int i = 0; i < 3; i++)
			if (!_set[i] && p?.Inventory is { } inv && inv.HasTool(Needs[i]))
				return i switch { 0 => "Set the eye in its hollow", 1 => "Set the hand in its hollow", _ => "Set the step in its hollow" };
		return "LOOK.  TOUCH.  CLIMB.";
	}

	private void OnDoorUsed(PlayerController player)
	{
		if (Opened) return;
		MarkSeen();
		for (int i = 0; i < 3; i++)
		{
			if (_set[i] || player?.Inventory is not { } inv || !inv.HasTool(Needs[i])) continue;
			Player.HeldUse.Do(player, Needs[i], GlobalPosition + Vector3.Up * 1.2f);
			inv.Consume(Needs[i]);
			SetPiece(i, true);
			StoryManager.Instance?.SetFlag(SetFlags[i]);
			if (PiecesSet == 3) _ = Cutscene.Run(this, ct => OpenSequence(ct), lockInput: true);
			return;
		}
		_ = StoryBeat.Caption(this, "LOOK.   TOUCH.   CLIMB.", 0.4f, 2.4f, 1f);
	}

	private void SetPiece(int i, bool live)
	{
		_set[i] = true;
		var holder = new Node3D { Name = $"Piece{i}", Position = HollowAt(i) + new Vector3(0, 0, -0.07f) };
		_leaf.AddChild(holder);
		switch (i)
		{
			case 0: StationProps.DeadEye(holder, 0.11f, stump: false); break;
			case 1: { var h = StationProps.PaleHand(holder); h.Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0); h.Scale = Vector3.One * 1.3f; break; }
			default: { var t = StationProps.StairTread(holder); t.Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0); t.Scale = Vector3.One * 0.95f; break; }
		}
		_hollowPieces[i] = holder;
		if (!live) return;
		holder.Position += new Vector3(0, 0, -0.2f);
		var tw = CreateTween();
		tw.TweenProperty(holder, "position:z", HollowAt(i).Z - 0.07f, 0.35f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
		PlaySfx("steel_door_slam", 2, holder.GlobalPosition, -10f, 1.6f);
		GD.Print($"[story] Act 13: the {Words[i].ToLowerInvariant()} piece is set in the iron door ({PiecesSet}/3)");
	}

	private void MarkSeen()
	{
		if (Seen) return;
		Seen = true;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationDoor3Seen);
		GD.Print("[story] Act 13: the iron door's three hollows are read - LOOK, TOUCH, CLIMB");
	}

	public override void _Process(double delta)
	{
		// the first time they come close to the revealed door, it is read to them
		if (WebBurned && !Seen && StoryBeat.Player(this) is { } p && p.GlobalPosition.DistanceTo(ToGlobal(new Vector3(0, 0, -1f))) < 2.6f)
		{
			MarkSeen();
			_ = StoryBeat.Caption(this, "Three hollows in the iron.   LOOK.   TOUCH.   CLIMB.", 0.5f, 3.2f, 1.2f);
		}
	}

	private async Task OpenSequence(CancellationToken ct)
	{
		await Cutscene.Wait(this, 0.8, ct);
		OpenDoor(true);
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationDoor3Open);
		GD.Print("[story] Act 13: the iron door opens");
		await Cutscene.Wait(this, 3.5, ct);
	}

	private void OpenDoor(bool live)
	{
		Opened = true;
		_doorUse.Enabled = false;
		foreach (var c in _blocker.GetChildren()) if (c is CollisionShape3D cs) cs.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
		if (!live) { _leaf.Position = new Vector3(0, 2.45f, 0); return; }
		PlaySfx("steel_door_open", 2, GlobalPosition + Vector3.Up * 1.2f, 4f, 0.7f);
		var tw = CreateTween();
		// grinds up into the wall, in fits
		tw.TweenProperty(_leaf, "position:y", 0.6f, 1.2f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		tw.TweenInterval(0.4f);
		tw.TweenProperty(_leaf, "position:y", 2.45f, 1.8f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
	}

	// ------------------------------------------------------------------ the web

	private void BuildWeb()
	{
		_web = new Node3D { Name = "Web" };
		AddChild(_web);
		var rng = new RandomNumberGenerator { Seed = 3131 };
		// layers spun across the whole back corner behind the desk, thick enough to hide what's behind
		for (int i = 0; i < 6; i++)
		{
			Vector3 at = new(rng.RandfRange(-0.4f, 0.4f), 1.3f + rng.RandfRange(-0.2f, 0.25f), -0.12f - i * 0.05f);
			World.WebKit.Card(_web, i % 3 == 1 ? World.WebKit.Kind.Sheet : World.WebKit.Kind.Tangle, rng, at, Vector3.Forward, new Vector2(3.2f, 2.8f) * rng.RandfRange(0.9f, 1.15f), rng.RandfRange(0, 3f));
		}
		// (2026-10-15, the scan: behind the cards hung a flat grey rectangle, 2.5 x 2.6, that read as a sheet of cloth in the
		// middle of the room. Now the mass of old silk is a sagging drape of its own: an irregular, ragged outline, thick and
		// grey with dust in its belly, thinning to strands at its edges, bellied out toward the room and slack at the foot;
		// its edges pulled out to the ceiling, the walls and the desk by guy lines.)
		BuildSilk(_web, rng);
		// strands out to the walls, up to the ceiling and down to the desk's corner, from the web's edge
		var k = new MeshKit();
		k.Mat(StationTextures.Flat("st_strand", new Color(0.7f, 0.7f, 0.66f), 1f, 0.05f));
		k.Color = Colors.White;
		for (int i = 0; i < 22; i++)
		{
			float t = rng.RandfRange(0f, Mathf.Tau);
			Vector3 a = new(Mathf.Cos(t) * rng.RandfRange(0.8f, 1.25f), 1.3f + Mathf.Sin(t) * rng.RandfRange(0.9f, 1.25f), -0.18f);
			Vector3 b = (i % 3) switch
			{
				0 => new Vector3(a.X + rng.RandfRange(-0.6f, 0.6f), 3.17f, rng.RandfRange(-0.9f, -0.1f)),          // the ceiling
				1 => new Vector3(Mathf.Sign(a.X) * rng.RandfRange(1.5f, 2.6f), a.Y + rng.RandfRange(-0.5f, 0.5f), -0.02f),   // along the back wall
				_ => a + new Vector3(rng.RandfRange(-1.2f, 1.2f), rng.RandfRange(-0.6f, 0.5f), rng.RandfRange(-0.9f, -0.3f)),
			};
			b.Y = Mathf.Clamp(b.Y, 0.05f, 3.17f);
			// a little slack in each, sagging at its middle
			Vector3 mid = (a + b) * 0.5f + Vector3.Down * a.DistanceTo(b) * rng.RandfRange(0.02f, 0.07f);
			k.Cylinder(a, mid, 0.0018f, 0.0015f, 3, false);
			k.Cylinder(mid, b, 0.0015f, 0.0018f, 3, false);
		}
		k.CommitTo(_web, "Strands", false);
		// and its maker, dead in the middle of it
		var sp = new MeshKit();
		sp.Mat(StationTextures.Flat("st_spider", new Color(0.08f, 0.06f, 0.05f), 0.6f, 0.3f));
		sp.Color = Colors.White;
		Vector3 c = new(0.3f, 1.7f, -0.2f);
		sp.Blob(c, new Vector3(0.035f, 0.035f, 0.045f), 3, 0.1f, false);
		for (int l = 0; l < 8; l++)
		{
			float a = Mathf.Tau * l / 8f;
			Vector3 knee = c + new Vector3(Mathf.Cos(a) * 0.07f, 0.03f, Mathf.Sin(a) * 0.04f);
			sp.Cylinder(c, knee, 0.004f, 0.003f, 3, false);
			sp.Cylinder(knee, knee + new Vector3(Mathf.Cos(a) * 0.05f, -0.06f, Mathf.Sin(a) * 0.03f), 0.003f, 0.002f, 3, false);
		}
		sp.CommitTo(_web, "Spider", false);

		_webUse = new PickupInteractable
		{
			Name = "WebUse", Prompt = "Thick with web", PickRadius = 0.9f, MaxDistance = 3.2f, Position = new Vector3(0, 1.3f, -0.3f), Outline = true,   // (the web is the thing, not the door)
			// (2026-10-15: drawn round the web's own ragged shape; it had found the door's leaf behind it, a lit rectangle through the silk)
			OutlineProxy = SilkOutline(), OutlineProxyAt = new Transform3D(Basis.Identity, new Vector3(0, 0.1f, 0.02f)),
			PromptFor = p => p?.Inventory is { } inv && inv.HasTool(ToolKind.Lighter) ? "Burn the web" : "Thick with web. It won't tear.",
		};
		_webUse.Interacted += OnWebUsed;
		AddChild(_webUse);
	}

	private static Texture2D _silkTex;

	/// <summary>Dense old web, 256 px: an irregular, lobed outline that frays into loose strands; inside it a felt of fine
	/// threads running every way (more of them across than up), grey with dust in its middle.</summary>
	private static Texture2D SilkTex()
	{
		if (_silkTex != null) return _silkTex;
		const int S = 256;
		var rng = new RandomNumberGenerator { Seed = 3133 };
		var a = new float[S * S];
		// the threads: hundreds of slightly curved strokes, most near the middle
		for (int i = 0; i < 900; i++)
		{
			float r = Mathf.Sqrt(rng.Randf()) * 0.62f, th = rng.RandfRange(0f, Mathf.Tau);
			float cx = S * (0.5f + Mathf.Cos(th) * r), cy = S * (0.5f + Mathf.Sin(th) * r);
			float ang = rng.Randf() < 0.6f ? rng.RandfRange(-0.5f, 0.5f) : rng.RandfRange(0f, Mathf.Pi);
			float len = rng.RandfRange(20f, 90f), bend = rng.RandfRange(-0.008f, 0.008f), str = rng.RandfRange(0.12f, 0.4f);
			for (float t = -len * 0.5f; t < len * 0.5f; t += 0.7f)
			{
				float aa = ang + bend * t;
				int x = (int)(cx + Mathf.Cos(aa) * t), y = (int)(cy + Mathf.Sin(aa) * t);
				if (x < 0 || y < 0 || x >= S || y >= S) continue;
				a[y * S + x] = Mathf.Min(1f, a[y * S + x] + str);
			}
		}
		var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
		for (int y = 0; y < S; y++)
			for (int x = 0; x < S; x++)
			{
				float dx = (x - S * 0.5f) / (S * 0.5f), dy = (y - S * 0.5f) / (S * 0.5f);
				float r = Mathf.Sqrt(dx * dx + dy * dy), th = Mathf.Atan2(dy, dx);
				// the ragged outline: lobes and notches round it
				float edge = 0.78f + 0.09f * Mathf.Sin(th * 3f + 0.7f) + 0.06f * Mathf.Sin(th * 7f + 2.1f) + 0.04f * Mathf.Sin(th * 13f);
				float body = Mathf.SmoothStep(edge, edge - 0.3f, r);
				// the felt: a dusty haze in the belly, the threads over it
				float haze = body * 0.55f * Mathf.SmoothStep(edge, 0.1f, r);
				float al = Mathf.Clamp(haze + a[y * S + x] * Mathf.SmoothStep(edge + 0.18f, edge - 0.1f, r), 0f, 0.92f);
				float dust = Mathf.SmoothStep(0.7f, 0f, r);
				var c = new Color(0.66f, 0.65f, 0.6f).Lerp(new Color(0.46f, 0.44f, 0.39f), dust * 0.7f);
				img.SetPixel(x, y, new Color(c.R, c.G, c.B, al));
			}
		img.GenerateMipmaps();
		return _silkTex = ImageTexture.CreateFromImage(img);
	}

	/// <summary>The web's outline for its white line: a flat lobed disc the size of the silk's thick part (both faces).</summary>
	private static Mesh SilkOutline()
	{
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		const int N = 28;
		Vector3 R(int i)
		{
			float th = Mathf.Tau * i / N;
			float r = 0.88f + 0.09f * Mathf.Sin(th * 3f + 0.7f) + 0.06f * Mathf.Sin(th * 7f + 2.1f);
			return new Vector3(Mathf.Cos(th) * 1.3f * r, -Mathf.Sin(th) * 1.25f * r, 0f);
		}
		for (int i = 0; i < N; i++)
			foreach (var v in new[] { Vector3.Zero, R(i), R(i + 1), Vector3.Zero, R(i + 1), R(i) })
			{
				st.SetNormal(Vector3.Back);
				st.AddVertex(v);
			}
		return st.Commit();
	}

	/// <summary>The drape of silk over the doorway: a grid bellied out toward the room and slack at its foot.</summary>
	private static void BuildSilk(Node3D parent, RandomNumberGenerator rng)
	{
		const int N = 10;
		float w = 3.1f, h = 3.0f, cy = 1.4f;
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		Vector3 P(int i, int j)
		{
			float u = i / (float)N, v = j / (float)N;   // v: 0 top .. 1 foot
			float belly = Mathf.Sin(u * Mathf.Pi) * Mathf.Pow(Mathf.Sin(v * Mathf.Pi * 0.9f + 0.1f), 0.7f);
			float x = (u - 0.5f) * w, y = cy + (0.5f - v) * h - 0.22f * v * v * Mathf.Sin(u * Mathf.Pi);
			return new Vector3(x, y, -0.1f - 0.32f * belly);
		}
		for (int j = 0; j < N; j++)
			for (int i = 0; i < N; i++)
			{
				var q = new[] { (i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1) };
				foreach (int idx in new[] { 0, 1, 2, 0, 2, 3 })
				{
					var (qi, qj) = q[idx];
					st.SetUV(new Vector2(qi / (float)N, qj / (float)N));
					st.SetNormal(Vector3.Back);
					st.AddVertex(P(qi, qj));
				}
			}
		st.GenerateNormals();
		var mi = new MeshInstance3D
		{
			Name = "Silk", Mesh = st.Commit(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoTexture = SilkTex(), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				Roughness = 1f, MetallicSpecular = 0.1f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
			},
			Rotation = new Vector3(0, 0, rng.RandfRange(-0.08f, 0.08f)),
		};
		parent.AddChild(mi);
	}

	private void OnWebUsed(PlayerController player)
	{
		if (WebBurned || player?.Inventory is not { } inv || !inv.HasTool(ToolKind.Lighter)) return;
		_ = Cutscene.Run(this, ct => BurnWeb(ct), lockInput: true);
	}

	private async Task BurnWeb(CancellationToken ct)
	{
		WebBurned = true;
		_webUse.Enabled = false;
		PlaySfx("lighter_flick", 1, ToGlobal(new Vector3(0, 1.2f, -0.6f)), 0f, 1f);
		await Cutscene.Wait(this, 0.6, ct);
		var fire = new FireVfx { Name = "WebFire", Extent = new Vector3(1.3f, 1.2f, 0.15f), FlameScale = 0.55f, Smoke = true, SmokeAmount = 0.6f, LightRange = 6f, LightEnergy = 2f, LightShadows = false, Position = new Vector3(0, 1.3f, -0.2f) };
		AddChild(fire);
		fire.Intensity = 0f;
		PlaySfx("web_burn", 1, ToGlobal(new Vector3(0, 1.3f, -0.2f)), 2f, 1f);
		double t = 0;
		while (t < 3.2)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			float u = (float)(t / 3.2);
			fire.Intensity = Mathf.Sin(Mathf.Min(1f, u * 1.2f) * Mathf.Pi);
			// the web shrivels from the middle out
			_web.Scale = new Vector3(1f - 0.9f * u, 1f - 0.9f * u, 1f);
			foreach (var n in _web.GetChildren()) if (n is GeometryInstance3D g) g.Transparency = Mathf.Clamp(u * 1.2f, 0f, 1f);
		}
		_web.Visible = false;
		fire.QueueFree();
		_doorUse.Enabled = true;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.StationWebBurned);
		GD.Print("[story] Act 13: the web burns away - there is a door behind it");
	}

	private void PlaySfx(string name, int variants, Vector3 at, float db, float pitch)
	{
		string path = $"res://assets/audio/sfx/{name}_{GD.RandRange(1, variants):00}.wav";
		if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}.wav";
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = db, PitchScale = pitch, UnitSize = 4f, MaxDistance = 30f };
		AddChild(s);
		s.GlobalPosition = at;
		s.Finished += s.QueueFree;
		s.Play();
	}
}
