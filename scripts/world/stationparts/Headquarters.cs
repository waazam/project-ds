using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.StationParts;

/// <summary>
/// (2026-10-16, STORY.md Rev 5) The lobby as Mitchell Webber's headquarters, over the decay (<see cref="LobbyDecor"/>):
/// <list type="bullet">
/// <item>the desk: his copy of the photo the player brought (the man and the boy, the man's face turned away, his corner
/// rubbed nearly smooth; looking at it: "That's... mine."), the nameplate M. WEBBER, CHIEF RANGER, and his key rack by it,
/// the clock's hook empty;</item>
/// <item>the barricades: the windows boarded from the inside, a chair wedged under Room 1's handle (it falls when the
/// door opens), a half-packed duffel by the way out (the furniture stacked at the locked room's door is
/// <see cref="LockedRoom"/>'s);</item>
/// <item>the relief crew's notice of February 1988, pinned up;</item>
/// <item>the lobby changing on every return: the nameplate turned a little further, or a drawer out that was shut. Nothing
/// explains it. The first time the player comes back to find it changed: "Dad... what happened here?"</item>
/// </list>
/// Lobby-local (StationInterior's space: floor y=0, the lobby x in [-5, 5], z in [-4.5, 4.5]).
/// </summary>
public partial class Headquarters : Node3D
{
	private const float W = StationInterior.HalfWidth, D = StationInterior.HalfDepth;
	private const float IX = W - 0.07f, IZ = D - 0.07f;
	/// <summary>The most changes the lobby takes (after that it stays as it is).</summary>
	public const int MaxChanges = 6;

	/// <summary>For tests: how many times the lobby has been changed behind the player's back.</summary>
	public int Changes { get; private set; }
	/// <summary>For tests: the desk's photo (E: look at it).</summary>
	public Interactable PhotoUse { get; private set; }
	/// <summary>For tests: the nameplate's turn off square, radians.</summary>
	public float NameplateTurn => _nameplate?.Rotation.Y ?? 0f;
	/// <summary>For tests: how many of the desk's drawers stand out.</summary>
	public int DrawersOut { get; private set; }
	public Readable Notice { get; private set; }

	private StationInterior _st;
	private Node3D _photo, _nameplate, _doorChair;
	private MeshInstance3D[] _desks;
	private bool _entered, _wasIn, _saying;
	private float _gaze;
	private Transform3D _chairWedged, _chairFallen;

	private static readonly float[] Turns = { 0f, 0.3f, 0.3f, 0.95f, 0.95f, 1.85f, 2.95f };

	/// <summary>Builds it all; <paramref name="desk"/> is the desk's node (at <see cref="StationInterior.DeskAt"/>),
	/// <paramref name="deskVariants"/> the desk shut, one drawer out, two (or null when they aren't built).</summary>
	public void Build(StationInterior st, Node3D desk, MeshInstance3D[] deskVariants, StationDoor room1Door)
	{
		_st = st;
		_desks = deskVariants;
		BuildDesk(desk);
		BuildRack();
		BuildBarricades(room1Door);
		Notice = PaperKit.Pinned(this, new Vector3(-IX, 1.5f, -1.58f), Vector3.Right, new Vector2(0.21f, 0.28f), PaperKit.Look.Note,
			"", StationStory.NoticeText, Readable.NoteStyle.Typed, 1.2f, "Read the notice", 88);
		Notice.ReadFlag = StationStory.NoticeRead;
		BuildWeathered();
		// Continue: as many changes as the story so far has sent the player out and back
		var s = StoryManager.Instance;
		if (s != null)
			foreach (var f in new[] { StoryManager.Flag.StationBasementTapeCut, StoryManager.Flag.StationRoom1Open, StoryManager.Flag.StationRoom1Solved,
				StoryManager.Flag.StationRoom2Solved, StoryManager.Flag.StationDoor3Open })
				if (s.HasFlag(f)) Changes++;
		ApplyChanges();
		SetProcess(true);
	}

	// ------------------------------------------------------------------ the weathered things (2026-10-24)

	/// <summary>(2026-10-24, the owner: "populate the game with more weathered objects") The station's own: a green steel
	/// filing cabinet against the east wall between the window and Room 1's door (the warden's records, years of them),
	/// and a can of coffee by the radio.</summary>
	private void BuildWeathered()
	{
		float fz = -1.42f;
		if (WeatheredProps.Put(this, "FilingCabinet", new Vector3(IX, 0f, fz), -90f) is { } cabinet)
			LorePages.Stash(cabinet, 9, "filing cabinet", new Vector3(0f, 1.36f, 0.31f)).PickRadius = 0.3f;   // (R.H.'s page: the rangers knew)
		StationStory.Solid(this, new Vector3(IX - 0.31f, 0.66f, fz), new Vector3(0.64f, 1.32f, 0.48f), "metal", "FilingCabinetBody");
		WeatheredProps.Put(this, "CoffeeCan_Blue", new Vector3(IX - 0.32f, 1.32f, fz + 0.06f), 200f, 0f, false);
		WeatheredProps.Put(this, "CoffeeCan_Red", new Vector3(-0.15f + 0.45f, 0.78f, -IZ + 0.36f), 20f, 0f, false);
	}

	// ------------------------------------------------------------------ the desk

	private void BuildDesk(Node3D desk)
	{
		// his photo, at the desk's right, turned to the room (the copy the player brought: the same picture, aged)
		_photo = new Node3D { Name = "Photo", Position = new Vector3(0.8f, 0.79f, 0.02f), Rotation = new Vector3(0, 0.35f, 0) };
		desk.AddChild(_photo);
		StationStory.Place(_photo, "ss_desk_photo", Transform3D.Identity, StationStory.PhotoRoles);
		AddAgedPrint(_photo);
		PhotoUse = new Interactable { Name = "Look", Prompt = "A photograph", PickRadius = 0.22f, MaxDistance = 2.2f, Position = new Vector3(0, 0.085f, 0.0f) };
		PhotoUse.Interacted += _ => SeePhoto();
		_photo.AddChild(PhotoUse);

		// the nameplate on the desk's front edge, square to the room (until it isn't)
		_nameplate = new Node3D { Name = "Nameplate", Position = new Vector3(-0.32f, 0.79f, -0.335f) };
		desk.AddChild(_nameplate);
		StationStory.Place(_nameplate, "ss_nameplate", Transform3D.Identity);
		var slope = new Vector3(0, 0.763f, 0.646f);
		var n = new Vector3(0, 0.646f, -0.763f);
		var b = new Basis(new Vector3(-1, 0, 0), slope, n);
		SignKit.Text(_nameplate, "M. WEBBER\nCHIEF RANGER", new Vector3(0, 0.0255f, -0.0085f) + n * 0.0016f, b, 0.0115f, new Color(0.09f, 0.07f, 0.04f), shadow: false);
	}

	/// <summary>
	/// The print in the desk frame is the very picture in the Act 1 cabin's frame (PhotoBoard.TwoPersonPhoto: the falls,
	/// the man and the boy on the rock), aged: yellowed, faded, and the top corner his thumb held rubbed to the pale card.
	/// It lies just in front of the Blender model's own painted layers, inside the frame's lip (2026-10-16: the two copies
	/// had come out as different pictures).
	/// </summary>
	private static void AddAgedPrint(Node3D photo)
	{
		const float iw = 0.094f, ih = 0.13f, bw = 0.018f, d = -0.005f;
		float c = Mathf.Cos(Mathf.DegToRad(12f)), s = Mathf.Sin(Mathf.DegToRad(12f));
		float v = bw + ih * 0.5f;
		var at = new Vector3(0, v * c - d * s + 0.0014f, v * s + d * c);
		var basis = new Basis(new Vector3(-1, 0, 0), new Vector3(0, c, s), new Vector3(0, s, -c));
		var img = PhotoBoard.TwoPersonPhoto(false);
		var rng = new RandomNumberGenerator { Seed = 1988 };
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
			{
				var col = img.GetPixel(x, y);
				float grey = col.R * 0.3f + col.G * 0.59f + col.B * 0.11f;
				col = col.Lerp(new Color(grey, grey, grey), 0.45f).Lerp(new Color(0.78f, 0.66f, 0.45f), 0.28f);
				// the corner his thumb wore (the viewer's top left): soft-edged, down to the card
				float du = x / (float)img.GetWidth(), dv = y / (float)img.GetHeight();
				float rub = 1f - Mathf.SmoothStep(0.18f, 0.42f, Mathf.Sqrt(du * du + dv * dv * 0.6f) + rng.RandfRange(-0.03f, 0.03f));
				col = col.Lerp(new Color(0.8f, 0.74f, 0.62f), rub * 0.85f);
				img.SetPixel(x, y, col);
			}
		var print = new MeshInstance3D
		{
			Name = "AgedPrint", Mesh = new QuadMesh { Size = new Vector2(iw, ih) }, Transform = new Transform3D(basis, at),
			MaterialOverride = new StandardMaterial3D { AlbedoTexture = ImageTexture.CreateFromImage(img), Roughness = 0.6f,
				TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps },
		};
		photo.AddChild(print);
	}

	/// <summary>"That's... mine." (once): on E, or on a long enough look at it from close.</summary>
	private void SeePhoto()
	{
		var s = StoryManager.Instance;
		if (s == null || s.HasFlag(StationStory.PhotoSeen)) return;
		s.SetFlag(StationStory.PhotoSeen);
		GD.Print("[story] Act 13: the photo on the desk - his own");
		_ = PlayerVoice.Say(this, "thats_mine", 2.6f);
	}

	// ------------------------------------------------------------------ the key rack: the clock's hook empty

	private void BuildRack()
	{
		var rack = new Node3D { Name = "KeyRack", Position = new Vector3(1.8f, 1.1f, IZ) };
		AddChild(rack);
		StationStory.Place(rack, "ss_key_rack", Transform3D.Identity);
		// the labels in his hand (the viewer's left to right is -X to +X turned: index 4 to 0)
		string[] tags = { "GATE", "PUMP", "CLOCK", "BOAT", "STORE" };
		float[] xs = { -0.16f, -0.08f, 0f, 0.08f, 0.16f };
		for (int i = 0; i < 5; i++)
			StationStory.HandText(rack, tags[i], new Vector3(xs[i], 0.098f, -0.0193f), new Basis(Vector3.Up, Mathf.Pi), 0.0105f, StationStory.PenInk, $"Tag{i}");
	}

	// ------------------------------------------------------------------ the barricades

	private void BuildBarricades(StationDoor room1Door)
	{
		// the windows, boarded from the inside: one on the +X wall by the coat stand, one in the front wall by the radio
		StationStory.Place(this, "ss_boarded_window", new Transform3D(new Basis(Vector3.Up, Mathf.Pi * 0.5f), new Vector3(IX, 0.92f, -2.6f)), null, "WindowEast");
		StationStory.Place(this, "ss_boarded_window", new Transform3D(new Basis(Vector3.Up, Mathf.Pi), new Vector3(0.85f, 0.92f, -IZ)), null, "WindowFront");
		// the duffel by the way out, half packed
		StationStory.Place(this, "ss_duffel", new Transform3D(new Basis(Vector3.Up, 0.25f), new Vector3(-0.95f, 0f, -3.72f)), null, "Duffel");
		StationStory.Solid(this, new Vector3(-0.95f, 0.12f, -3.72f), new Vector3(0.6f, 0.24f, 0.3f), "fabric", "DuffelBody");
		// a chair wedged under Room 1's handle, on the lobby side (the handle at x = W - 0.07, z = 0.8): it goes over when
		// the door opens
		_doorChair = new Node3D { Name = "DoorChair" };
		AddChild(_doorChair);
		var chairXf = Transform3D.Identity;
		if (StationStory.Place(_doorChair, "dining_chair", chairXf, LobbyDecor.Roles) == null) return;
		// (2026-10-19, the clip audit: walked through) solid, wedged or knocked over (the body goes with it)
		StationStory.Solid(_doorChair, new Vector3(0, 0.45f, 0), new Vector3(0.44f, 0.9f, 0.44f), "wood", "ChairBody");
		_chairWedged = new Transform3D(new Basis(Vector3.Up, Mathf.Pi * 0.5f) * new Basis(Vector3.Right, -0.32f), new Vector3(W - 0.42f, 0.04f, 0.8f));
		_chairFallen = new Transform3D(new Basis(Vector3.Up, 2.3f) * new Basis(Vector3.Right, -1.5f), new Vector3(W - 0.95f, 0.24f, 1.65f));
		bool open = StoryManager.Instance?.HasFlag(StoryManager.Flag.StationRoom1Open) ?? false;
		_doorChair.Transform = open ? _chairFallen : _chairWedged;
		if (room1Door != null && !open)
			room1Door.Opened += () =>
			{
				var tw = CreateTween();
				tw.TweenProperty(_doorChair, "transform", _chairFallen, 0.55f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
				Audio.AudioDirector.OneShot(this, "chair_scrape", 2, _doorChair.GlobalPosition, -6f, "Events", 3f, 0.08f);
			};
	}

	// ------------------------------------------------------------------ the lobby changing behind the player's back

	private void ApplyChanges()
	{
		int c = Mathf.Clamp(Changes, 0, MaxChanges);
		if (_nameplate != null) _nameplate.Rotation = new Vector3(0, Turns[c], 0);
		DrawersOut = c >= 4 ? 2 : c >= 2 ? 1 : 0;
		if (_desks != null)
			for (int i = 0; i < _desks.Length; i++)
				if (_desks[i] != null) _desks[i].Visible = i == DrawersOut;
	}

	public override void _Process(double delta)
	{
		var p = StoryBeat.Player(this);
		if (p == null || _st == null) return;
		bool inLobby = _st.InLobby(p.GlobalPosition);
		if (inLobby && !_entered) { _entered = true; _wasIn = true; }
		if (!_entered) return;
		if (!inLobby && _wasIn && Changes < MaxChanges)
		{
			// out of the room: something moves the nameplate on, or pulls a drawer
			Changes++;
			ApplyChanges();
			GD.Print($"[story] Act 13: the lobby has changed while they were out ({Changes})");
		}
		if (inLobby && !_wasIn && Changes > 0 && !_saying && StoryManager.Instance is { } s && !s.HasFlag(StationStory.LobbyChangedSaid))
			_ = SayChanged();
		_wasIn = inLobby;
		Gaze(p, (float)delta);
	}

	private async System.Threading.Tasks.Task SayChanged()
	{
		_saying = true;
		await ToSignal(GetTree().CreateTimer(1.3), SceneTreeTimer.SignalName.Timeout);
		var s = StoryManager.Instance;
		if (s != null && !s.HasFlag(StationStory.LobbyChangedSaid) && IsInsideTree())
		{
			s.SetFlag(StationStory.LobbyChangedSaid);
			GD.Print("[story] Act 13: back in the lobby, and it has changed");
			await PlayerVoice.Say(this, "dad_what_happened", 2.8f);
		}
		_saying = false;
	}

	/// <summary>A look at the photo from close (about two seconds' worth, the camera on it) is as good as E.</summary>
	private void Gaze(PlayerController p, float dt)
	{
		if (_photo == null || (StoryManager.Instance?.HasFlag(StationStory.PhotoSeen) ?? true)) return;
		var cam = GetViewport()?.GetCamera3D();
		if (cam == null) return;
		Vector3 at = _photo.GlobalPosition + Vector3.Up * 0.085f;
		Vector3 to = at - cam.GlobalPosition;
		bool looking = to.Length() < 1.7f && (-cam.GlobalBasis.Z).Dot(to.Normalized()) > 0.985f;
		_gaze = looking ? _gaze + dt : Mathf.Max(0f, _gaze - dt * 2f);
		if (_gaze > 1.6f) SeePhoto();
	}
}
