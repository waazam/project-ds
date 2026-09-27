using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 19's hidden journal (the owner's "Real Act 20"): the key to the round room's switches, on one of
/// the library's shelves. By lamplight it's nothing: an old leather book lying on a couple of ledgers in
/// a gap on the left-hand shelves. Under the blacklight a handprint glows on its cover and "READ" on
/// the shelf's back over it, and from then on it can be picked up and read. It has the switches as a
/// riddle, the way the owner wrote it.
/// </summary>
public partial class Library
{
	public const float JournalZ = 9.4f;
	/// <summary>The journal's cover, where it sits on its shelf (library space).</summary>
	public static readonly Vector3 JournalLocal = new(-HalfW + 0.2f, 1.4f + 0.075f, JournalZ);

	public Readable Journal { get; private set; }
	public bool JournalFound { get; private set; }
	public Vector3 JournalWorld => ToGlobal(JournalLocal);
	/// <summary>Where to stand to find it: in front of that stretch of shelves.</summary>
	public Vector3 JournalStandWorld => ToGlobal(new Vector3(-HalfW + 1.5f, 0.05f, JournalZ));

	public const string JournalText =
		"They wired this room long after they built it. Bulbs where the candles were, "
		+ "and six switches in the round room, three to a wall. The platform won't go up without them.\n\n"
		+ "The keeper wouldn't write it down plainly. He made me say it back to him until I had it:\n\n"
		+ "      The lefts are feeling down,\n"
		+ "      the rights are all right,\n"
		+ "      and if that's true,\n"
		+ "      the middles are doing just fine.\n\n"
		+ "Both walls say the same thing.\n\n"
		+ "When the ring goes green, don't look down.";

	/// <summary>Books out of the way where the journal lies (left wall, fourth shelf up).</summary>
	private static bool InJournalSlot(Vector3 bookCentre)
		=> bookCentre.X < -HalfW + 0.5f && Mathf.Abs(bookCentre.Z - JournalZ) < 0.3f && bookCentre.Y > 1.4f && bookCentre.Y < 1.84f;

	private void BuildJournal()
	{
		var root = new Node3D { Name = "Journal", Position = new Vector3(-HalfW + 0.2f, 1.4f, JournalZ) };
		AddChild(root);
		// two old ledgers under it, a little askew
		var ledger = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.16f, 0.1f), Roughness = 0.8f };
		root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.24f, 0.035f, 0.32f) }, Position = new Vector3(0, 0.0175f, 0), Rotation = new Vector3(0, 0.06f, 0), MaterialOverride = ledger });
		root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.22f, 0.03f, 0.3f) }, Position = new Vector3(0.005f, 0.05f, 0.01f), Rotation = new Vector3(0, -0.08f, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.12f, 0.1f), Roughness = 0.8f } });
		// the journal: worn brown leather, a strap round it, its pages showing
		var book = new Node3D { Name = "Book", Position = new Vector3(0, 0.075f, 0), Rotation = new Vector3(0, 0.12f, 0) };
		root.AddChild(book);
		book.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.15f, 0.028f, 0.21f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.19f, 0.1f), Roughness = 0.55f, MetallicSpecular = 0.3f } });
		book.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.022f, 0.2f) }, Position = new Vector3(0.006f, 0, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.82f, 0.76f, 0.6f), Roughness = 0.9f } });
		book.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.155f, 0.03f, 0.018f) }, Position = new Vector3(0, 0, 0.03f), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.14f, 0.08f, 0.05f), Roughness = 0.6f } });
		// what the blacklight shows: a hand on the cover, and "READ" on the shelf's back above it
		UvInk.OnSurface(book, new Vector3(-0.005f, 0.014f, -0.015f), Vector3.Up, new Vector2(0.12f, 0.15f), UvInk.Hand(), UvInk.Pale, 2f, 0.3f);
		UvInk.Write(this, new Vector3(-HalfW + 0.022f, 1.66f, JournalZ), Vector3.Right, "READ", 0.26f, UvInk.Cyan);
		Journal = new Readable
		{
			Name = "Readable", Title = "A journal, hidden among the books", Text = JournalText, Style = Readable.NoteStyle.Handwritten,
			Prompt = "Read the journal", PickRadius = 0.2f, MaxDistance = 2.6f, ReadFlag = "read_round_room_journal",
			Position = new Vector3(0, 0.02f, 0),
		};
		book.AddChild(Journal);
		JournalFound = StoryManager.Instance?.HasFlag(StoryManager.Flag.LibraryJournalFound) ?? false;
		Journal.Enabled = JournalFound;
	}

	private void SeekJournal()
	{
		if (Journal == null || JournalFound) return;
		// found when the blacklight falls on it from close enough to see the print
		var uv = Lantern.Uv;
		if (!uv.on || uv.pos.DistanceTo(JournalWorld) > 4.5f || Lantern.UvOn(JournalWorld) < 0.3f) return;
		JournalFound = true;
		Journal.Enabled = true;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.LibraryJournalFound);
		GD.Print("[story] Act 19: the blacklight shows a hand on a journal among the books");
	}
}
