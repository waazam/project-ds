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

	private void BuildStory()
	{
		if (Engine.IsEditorHint() || _gen == null) return;
		float wallZ = -(Hd - LogT * 0.5f - LogBulge), wallX = -(Hw - LogT * 0.5f - LogBulge);

		// ---- the board: on the back wall, left of the table, at eye height
		Board = new PhotoBoard { Name = "PhotoBoard", Mode = Hub ? PhotoBoard.BoardMode.Act1 : PhotoBoard.BoardMode.Hollow, Position = new Vector3(-0.85f, 1.5f, wallZ + 0.004f) };
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

	/// <summary>Act 1: E on the hub's door. It swings in on its iron hinges and stays open (saved).</summary>
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
