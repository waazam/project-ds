using Godot;
using ProjectDS.Audio;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// The story's things in the one cabin (2026-10-16, STORY.md Rev 5: "It is one model, the same inside and out wherever it
/// stands"). Every cabin but the derelict one carries them, in the same places:
/// <list type="bullet">
/// <item>the backlit <see cref="PhotoBoard"/> on the back wall, left of the table (Act 1: the player's own; the Hollow:
/// R.H.'s photographs pinned across it);</item>
/// <item>on the table: the assignment sheet on its folder with a pencil, and the framed photo of a man and a boy, the man
/// turned away (it changes with the board's third stage; in the Hollow it is the changed one);</item>
/// <item>an axe on two pegs over the hearth (the stove), on the left wall above the stove pipe: dressing until Act 7.</item>
/// </list>
/// The Hollow's table is friend.tscn's ("HisTable/Table", with the cap and R.H.'s page); the hub (Act 1) gets a clean one
/// of the same models, its chair pushed in. All of it is Blender's (cabin_story.py; the table and chair friend's, the axe
/// the tools' own).
///
/// For Act 7: <see cref="Board"/>, <see cref="HearthAxe"/> (and <see cref="HearthAxeRest"/>), <see cref="InteriorBounds"/>,
/// <see cref="AssignmentSheet"/>, <see cref="FramedPhoto"/>.
/// </summary>
public partial class Cabin
{
	/// <summary>Act 1's cabin joins this group (the compass and the walks route to it).</summary>
	public const string HubGroup = "trailhead_cabin";
	/// <summary>Act 1: the cabin's door has been opened (it stays open).</summary>
	public const string HubDoorFlag = "act1_cabin_door_open";
	/// <summary>(2026-10-29) Act 1: the key taken from under the door mat (the door is locked until then).</summary>
	public const string HubKeyFlag = "act1_cabin_key";
	/// <summary>(2026-10-29) Act 1: the door shut again behind him while he was out (the alarm's beat, Act1Walks.Alarm.cs)
	/// until it slams open.</summary>
	public const string HubDoorShutFlag = "act1_cabin_door_shut";
	/// <summary>(2026-10-29) Act 1: ... and slammed open again (open from then on).</summary>
	public const string HubDoorSlamFlag = "act1_cabin_door_slammed";

	/// <summary>The photo board on the back wall.</summary>
	public PhotoBoard Board { get; private set; }
	/// <summary>The axe on its pegs over the hearth (a plain model, no pickup: Act 7 makes it one). Its transform is
	/// <see cref="HearthAxeRest"/> in the cabin's space.</summary>
	public Node3D HearthAxe { get; private set; }
	/// <summary>Where the hearth axe rests, in the cabin's local space (the handle along Z, the bit up, the head flat to
	/// the wall).</summary>
	public Transform3D HearthAxeRest { get; private set; }
	/// <summary>The assignment sheet on the table (a Readable).</summary>
	public Readable AssignmentSheet { get; private set; }
	/// <summary>(2026-10-24) The Hollow's: R.H.'s own copy of the assignment beside the player's, its shot list all ticked,
	/// and one more line pencilled under it (null elsewhere).</summary>
	public Readable RHSheet { get; private set; }
	/// <summary>The photo in the standing frame on the table (its material's albedo is the picture).</summary>
	public MeshInstance3D FramedPhoto { get; private set; }
	public bool FramedPhotoChanged { get; private set; }
	/// <summary>The room inside the walls, in the cabin's local space (the floor at y = 0).</summary>
	public Aabb InteriorBounds
	{
		get
		{
			float ix = Hw - LogT * 0.5f - LogBulge, iz = Hd - LogT * 0.5f - LogBulge;
			return new Aabb(new Vector3(-ix, 0f, -iz), new Vector3(ix * 2f, WallHeight, iz * 2f));
		}
	}

	/// <summary>The table's top, in its own frame (friend.tscn's Table node: the top's middle over the origin).</summary>
	private const float TableTop = 0.74f;
	/// <summary>Where the hub's table stands (the same place friend.tscn puts the Hollow's: HisTable (0.3, 0, -1.6) + Table (0, 0, 0.7)).</summary>
	private static readonly Vector3 HubTableAt = new(0.3f, 0f, -0.9f);
	private const float FrameTiltDeg = 12f;

	private const string AssignmentText =
		"October feature: Autumnal Delights\n\n" +
		"A nature photographer's trip into the woods, at the turn of the season.\n\n" +
		"What we're after:\n" +
		"   autumn light\n" +
		"   wildlife\n" +
		"   a place that matters\n\n" +
		"Shoot what the woods give you. Prints back to the office by the first.";

	private const string RHAssignmentText =
		"October feature: Autumnal Delights\n" +
		"\n" +
		"A nature photographer's trip into the woods, at the turn of the season.\n" +
		"\n" +
		"Shoot what the woods give you. Prints back to the office by the first.\n" +
		"\n" +
		"Shot list (pencilled in):\n" +
		"   [x]  the valley from the overlook, in the afternoon light\n" +
		"   [x]  the falls, up close\n" +
		"   [x]  the trail at last light\n" +
		"   [x]  the steps\n" +
		"\n" +
		"(the last line in a different pencil, pressed hard enough to tear)\n" +
		"\n" +
		"                                                  R.H.";

	private void BuildStory()
	{
		if (Engine.IsEditorHint() || _gen == null) return;
		float wallZ = -(Hd - LogT * 0.5f - LogBulge), wallX = -(Hw - LogT * 0.5f - LogBulge);

		// ---- the board: on the back wall, left of the table, at eye height
		Board = new PhotoBoard { Name = "PhotoBoard", Mode = BurnedOut ? PhotoBoard.BoardMode.Bare : Hub ? PhotoBoard.BoardMode.Act1 : PhotoBoard.BoardMode.Hollow, Position = new Vector3(-0.85f, 1.5f, wallZ + 0.004f) };
		_gen.AddChild(Board);
		if (Hub && StoryManager.Instance is { Current: >= Checkpoint.Act24Finished }) Board.GlowOn = false;

		// ---- the table (the Hollow's is friend.tscn's; the hub's its own, clean, the chair pushed in)
		Transform3D table;
		if (!Hub && GetNodeOrNull<Node3D>("HisTable/Table") is { } his)
			table = GlobalTransform.AffineInverse() * his.GlobalTransform;
		else
		{
			table = new Transform3D(Basis.Identity, HubTableAt);
			if (Hub) BuildHubTable(table);
		}

		// ---- the assignment sheet on its folder, a pencil beside it
		var paint = (System.Func<string, Material>)PhotoBoard.StoryPaint;
		var folderAt = table * new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(8f)), new Vector3(-0.38f, TableTop, 0.15f));
		ItemModels.Add(_gen, "cabin_story", "AssignmentFolder", folderAt, "AssignmentFolder", paint, false);
		var sheetRoot = new Node3D { Name = "AssignmentSheet", Transform = folderAt };
		sheetRoot.AddToGroup(PapersGroup);
		_gen.AddChild(sheetRoot);
		AssignmentSheet = PaperKit.Flat(sheetRoot, new Vector3(0.004f, 0.0005f, 0.004f), -3f, new Vector2(0.2f, 0.26f), PaperKit.Look.Note,
			$"{StoryNames.Magazine}: Autumnal Delights", AssignmentText, Readable.NoteStyle.Typed, prompt: "Read the assignment", seed: 23);
		AssignmentSheet.PickRadius = 0.12f;   // (clear of the cap's on the Hollow's table)
		// the Hollow's (and so Act 7's): his copy beside it. He was on this assignment before you.
		if (!Hub && !BurnedOut)
		{
			var rhAt = table * new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(-14f)), new Vector3(-0.08f, TableTop, 0.24f));
			var rhRoot = new Node3D { Name = "RHAssignmentSheet", Transform = rhAt };
			rhRoot.AddToGroup(PapersGroup);
			_gen.AddChild(rhRoot);
			RHSheet = PaperKit.Flat(rhRoot, new Vector3(0, 0.0012f, 0), 0f, new Vector2(0.2f, 0.26f), PaperKit.Look.Note,
				$"Another copy of the assignment, soft with handling: \"R.H.\" in the corner", RHAssignmentText, Readable.NoteStyle.Typed, prompt: "Read the other assignment", seed: 29);
			RHSheet.PickRadius = 0.12f;
		}
		ItemModels.Add(_gen, "cabin_story", "Pencil", table * new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(78f)), new Vector3(-0.17f, TableTop, 0.17f)), "Pencil", paint, false);

		// ---- the framed photo, standing, turned a little toward the door
		var frameAt = table * new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(14f)), new Vector3(-0.38f, TableTop, -0.2f));
		if (ItemModels.Add(_gen, "cabin_story", "PictureFrame", frameAt, "PictureFrame", paint) != null)
		{
			// the photo in its opening (cabin_story.py: upright, its middle at (0, 0.109, 0.004), turned back 12 degrees round
			// (0, 0, 0.008))
			var tilt = new Basis(Vector3.Right, -Mathf.DegToRad(FrameTiltDeg));
			var pivot = new Vector3(0, 0, 0.008f);
			var centre = pivot + tilt * (new Vector3(0, 0.109f, 0.0045f) - pivot);
			FramedPhoto = new MeshInstance3D
			{
				Name = "FramedPhoto", Mesh = new QuadMesh { Size = new Vector2(0.127f, 0.178f) },
				Transform = frameAt * new Transform3D(tilt, centre), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				MaterialOverride = new StandardMaterial3D { Roughness = 0.45f, MetallicSpecular = 0.5f, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps },
			};
			FramedPhoto.AddToGroup(PapersGroup);
			_gen.AddChild(FramedPhoto);
			// the Hollow's is the changed one (the board changed it, by the lot)
			FramedPhotoChanged = !Hub;
			ApplyFramedPhoto();
		}

		// ---- the axe on its pegs over the hearth: the left wall, over the stove pipe's thimble
		const float hz = -1.0f, pegY = 1.97f;
		var pegs = new Basis(new Vector3(0, 0, -1), Vector3.Up, Vector3.Right);   // (the pegs' +Z out of the wall: +X)
		ItemModels.Add(_gen, "cabin_story", "AxePegs", new Transform3D(pegs, new Vector3(wallX, pegY, hz)), "AxePegs", paint);
		if (ToolModels.Load("axe") is { } axe)
		{
			// the handle (the model's Y) along the wall (Z), the bit (its -Z) up, the head flat to the logs
			var b = new Basis(Vector3.Right, Vector3.Back, Vector3.Down);
			var box = ToolModels.Bounds(axe, new Transform3D(b, Vector3.Zero));
			var at = new Vector3(wallX + 0.06f, pegY + 0.03f + 0.018f, hz - (box.Position.Z + box.Size.Z * 0.5f));
			HearthAxeRest = new Transform3D(b, at);
			HearthAxe = new Node3D { Name = "HearthAxe", Transform = HearthAxeRest };
			HearthAxe.AddChild(axe);
			_gen.AddChild(HearthAxe);
		}

		// ---- the hub: the room is indoors (the forest heard through the walls)
		if (Hub)
		{
			if (!BurnedOut) BuildDoorMat();
			var zone = new Audio.IndoorZone { Name = "IndoorZone", Position = new Vector3(0, WallHeight * 0.5f, 0) };
			zone.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(Width - 0.25f, WallHeight, Depth - 0.25f) } });
			_gen.AddChild(zone);
		}
	}

	/// <summary>The hub's table and chair: friend.tscn's models, clean (no stains, nothing of his), the chair pushed in.</summary>
	private void BuildHubTable(Transform3D table)
	{
		var t = new Node3D { Name = "HubTable", Transform = table };
		_gen.AddChild(t);
		ItemModels.Add(t, "friend", "FriendTable", Transform3D.Identity, "TableMesh");
		var chairAt = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(4f)), new Vector3(0.03f, 0f, -0.48f));
		ItemModels.Add(t, "friend", "FriendChair", chairAt, "ChairMesh");
		if (!BuildCollision) return;
		var body = new StaticBody3D { Name = "Collision", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		t.AddChild(body);
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, TableTop * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(1.1f, TableTop, 0.7f) } });
		body.AddChild(new CollisionShape3D { Position = chairAt.Origin + new Vector3(0, 0.48f, -0.03f), Rotation = new Vector3(0, Mathf.DegToRad(4f), 0), Shape = new BoxShape3D { Size = new Vector3(0.46f, 0.96f, 0.46f) } });
	}

	/// <summary>The framed photo as it was (a man and a boy by the falls), or changed (old and grey, the man alone).</summary>
	public void SetFramedPhotoChanged(bool changed)
	{
		if (FramedPhotoChanged == changed) return;
		FramedPhotoChanged = changed;
		ApplyFramedPhoto();
	}

	private static Texture2D _photoAsWas, _photoChanged;

	private void ApplyFramedPhoto()
	{
		if (FramedPhoto?.MaterialOverride is not StandardMaterial3D m) return;
		m.AlbedoTexture = FramedPhotoChanged
			? _photoChanged ??= ImageTexture.CreateFromImage(PhotoBoard.TwoPersonPhoto(true))
			: _photoAsWas ??= ImageTexture.CreateFromImage(PhotoBoard.TwoPersonPhoto(false));
	}

	/// <summary>Act 1: whether the hub's door has been opened.</summary>
	public bool HubDoorOpen => Hub && IsOpen;

	/// <summary>For tests: the mat's E-point (null once the key's out), and the key taken.</summary>
	public Interactable MatUse { get; private set; }
	public bool HasHubKey => StoryManager.Instance?.HasFlag(HubKeyFlag) ?? false;
	/// <summary>For tests: tried the door while it was locked; the door's E-point while it's shut.</summary>
	public bool TriedLocked { get; private set; }
	public Interactable HubDoorUse => _door != null && IsInstanceValid(_door) ? _door.GetNodeOrNull<Interactable>("OpenDoor") : null;

	private Node3D _mat;

	/// <summary>(2026-10-29, the owner's friend's playtest: "the cabin door is locked; a key is under the cabin's door mat")
	/// A worn coir mat on the porch before the door; E lifts its far edge, and the key under it is taken.</summary>
	private void BuildDoorMat()
	{
		const float w = 0.78f, d = 0.46f;
		// (hinged along its door-side edge: lifting it turns it up off the boards toward the door)
		_mat = new Node3D { Name = "DoorMat", Position = new Vector3(0f, 0.003f, FrontFace + 0.12f) };
		_gen.AddChild(_mat);
		var k = new MeshKit();
		k.Color = Colors.White;
		k.Mat(ProcTextures.Flat("hub_mat", new Color(0.36f, 0.27f, 0.16f), 1f, 0.05f));
		k.BevelBox(new Vector3(0f, 0.008f, d * 0.5f), new Vector3(w, 0.016f, d), 0.006f, 2f);
		k.Mat(ProcTextures.Flat("hub_mat_edge", new Color(0.2f, 0.15f, 0.09f), 1f, 0.05f));
		k.BevelBox(new Vector3(0f, 0.0165f, d * 0.5f), new Vector3(w - 0.08f, 0.002f, d - 0.08f), 0.001f, 2f);
		k.CommitTo(_mat, "Mesh", false);
		if (HasHubKey || (StoryManager.Instance?.HasFlag(HubDoorFlag) ?? false)) return;
		// the key, under it (seen only as it lifts)
		var key = new MeshKit();
		key.Color = Colors.White;
		key.Mat(ProcTextures.Flat("hub_key", new Color(0.62f, 0.5f, 0.24f), 0.35f, 0.7f));
		key.Cylinder(new Vector3(-0.03f, 0.003f, 0f), new Vector3(-0.03f, 0.006f, 0f), 0.017f, 0.017f, 10);
		key.Box(new Vector3(0.015f, 0.004f, 0f), new Vector3(0.06f, 0.004f, 0.007f));
		key.Box(new Vector3(0.036f, 0.004f, 0.007f), new Vector3(0.01f, 0.004f, 0.01f));
		var keyNode = new Node3D { Name = "Key", Position = new Vector3(0.12f, -0.002f, FrontFace + 0.12f + d * 0.6f), Rotation = new Vector3(0, 0.5f, 0) };
		_gen.AddChild(keyNode);
		key.CommitTo(keyNode, "Mesh", false);
		MatUse = new Interactable { Name = "LiftMat", Prompt = "Lift the mat", MaxDistance = 2.4f, PickRadius = 0.35f, Position = new Vector3(0f, 0.05f, d * 0.5f), HighlightRoot = new NodePath("..") };
		_mat.AddChild(MatUse);
		MatUse.Interacted += _ => LiftMat(keyNode);
	}

	private void LiftMat(Node3D key)
	{
		if (HasHubKey) return;
		StoryManager.Instance?.SetFlag(HubKeyFlag);
		if (MatUse != null && IsInstanceValid(MatUse)) MatUse.QueueFree();
		MatUse = null;
		AudioDirector.OneShot(this, "cloth", 4, _mat.GlobalPosition, -10f, "Events", 2f, 0.06f);
		var tw = _mat.CreateTween();
		tw.TweenProperty(_mat, "rotation:x", Mathf.DegToRad(-62f), 0.45f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		tw.TweenInterval(0.7f);
		tw.TweenCallback(Callable.From(() =>
		{
			if (IsInstanceValid(key)) key.QueueFree();
			AudioDirector.OneShot(this, "book_handle", 3, _mat.GlobalPosition, -14f, "Events", 2f, 0.1f);
		}));
		tw.TweenProperty(_mat, "rotation:x", 0f, 0.35f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		_ = UI.Subtitle.Instance?.Show("The key. Right where they said.", 0.4f, 2.6f, 0.9f);
		GD.Print("[story] Act 1: the key from under the cabin's mat");
	}

	/// <summary>Act 1: E on the hub's door: locked without the key (a rattle, the thought), opened with it.</summary>
	public void UseHubDoor()
	{
		if (!Hub || IsOpen) return;
		if (!HasHubKey && !(StoryManager.Instance?.HasFlag(HubDoorFlag) ?? false))
		{
			TriedLocked = true;
			StoryBeat.PlayAt(this, AudioDirector.Take("door_locked", 2), "Events", new Vector3(0, 1.0f, Hd), volumeDb: -6f, unitSize: 3f, maxDistance: 20f, pitch: (float)GD.RandRange(0.95, 1.05));
			_ = UI.Subtitle.Instance?.Show("Locked. They said they'd leave a key out.", 0.4f, 3.0f, 1.0f);
			GD.Print("[story] Act 1: the cabin door is locked");
			return;
		}
		OpenHubDoor();
	}

	/// <summary>(2026-10-29) Act 1: shut again behind him, quietly, while he's far off (the alarm's beat): saved, the door
	/// solid, E opens it (he has the key).</summary>
	public void ShutHubDoor()
	{
		if (!Hub || BurnedOut) return;
		StoryManager.Instance?.SetFlag(HubDoorShutFlag);
		if (!IsOpen) return;
		IsOpen = false;
		if (_doorCollision != null && IsInstanceValid(_doorCollision)) _doorCollision.Disabled = false;
		if (_door != null && IsInstanceValid(_door))
		{
			_door.Rotation = Vector3.Zero;
			if (_door.GetNodeOrNull<Interactable>("OpenDoor") == null)
			{
				var use = new Interactable { Name = "OpenDoor", Prompt = "Open the door", MaxDistance = 2.6f, PickRadius = 0.6f, Position = new Vector3(DoorWidth * 0.3f, 1.0f, 0.1f), HighlightRoot = new NodePath("NoHighlight") };
				use.Interacted += _ => UseHubDoor();
				_door.AddChild(use);
			}
		}
		GD.Print("[story] Act 1: the cabin door shut again behind him");
	}

	/// <summary>(2026-10-29) Act 1: the door slams open on its own, hard back against the wall, as he comes level with the
	/// cabin on the way back (the alarm stops).</summary>
	public void SlamHubDoorOpen()
	{
		if (!Hub) return;
		StoryManager.Instance?.SetFlag(HubDoorFlag);
		StoryManager.Instance?.SetFlag(HubDoorSlamFlag);
		if (IsOpen) return;
		IsOpen = true;
		if (_doorCollision != null && IsInstanceValid(_doorCollision)) _doorCollision.Disabled = true;
		if (_door != null && IsInstanceValid(_door))
		{
			if (_door.GetNodeOrNull<Interactable>("OpenDoor") is { } use) use.QueueFree();
			var tw = _door.CreateTween();
			tw.TweenProperty(_door, "rotation:y", Mathf.DegToRad(104f), 0.16f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
			tw.TweenProperty(_door, "rotation:y", Mathf.DegToRad(92f), 0.25f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		}
		int take = (int)(GD.Randi() % 2) + 1;
		StoryBeat.PlayAt(this, $"res://assets/audio/sfx/door_slam_{take:00}.wav", "Events", new Vector3(0, 1.1f, Hd), volumeDb: 2f, unitSize: 8f, maxDistance: 120f, pitch: 0.95f);
		StoryBeat.PlayAt(this, AudioDirector.Take("door_frame_thud", 4), "Events", new Vector3(-DoorWidth * 0.5f, 1.0f, Hd - 1f), volumeDb: -2f, unitSize: 6f, maxDistance: 90f, pitch: 0.9f);
		GD.Print("[story] Act 1: the cabin door slams open");
	}

	/// <summary>Act 1: the hub's door opened (unlocked with the key). It swings in on its iron hinges and stays open (saved).</summary>
	public void OpenHubDoor()
	{
		if (!Hub || IsOpen) return;
		IsOpen = true;
		if (_doorCollision != null && IsInstanceValid(_doorCollision)) _doorCollision.Disabled = true;
		if (_door != null && IsInstanceValid(_door))
		{
			if (_door.GetNodeOrNull<Interactable>("OpenDoor") is { } use) use.QueueFree();
			_door.CreateTween().TweenProperty(_door, "rotation:y", Mathf.DegToRad(96f), 1.3f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
		}
		StoryBeat.PlayAt(this, AudioDirector.Take("hinge_creak", 6), "Events", new Vector3(0, 1.0f, Hd), volumeDb: -6f, unitSize: 3f, maxDistance: 25f, pitch: (float)GD.RandRange(0.92, 1.05));
		StoryManager.Instance?.SetFlag(HubDoorFlag);
		GD.Print("[story] Act 1: the cabin door opened");
	}
}
