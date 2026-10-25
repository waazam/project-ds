using System.Collections.Generic;
using Godot;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// R.H.'s pages (2026-10-10b): six leaves torn from his notebook, dropped along the way he went before you, from the
/// lake's far shore down to the lodge. Each lies on the floor a step or two from one of the saves, to be read (E); read,
/// it's kept on the album's last page, so his story can be put together in order. He's a stranger: these are all there is
/// of him, beyond the register at the trailhead, his note at the camp and the page in the cabin.
/// </summary>
public static class LorePages
{
	public sealed record Page(int N, string Group, string Text);

	// (2026-10-24, the game-loop pass) twelve now, in the order they're come upon: his six by the saves, and six more hidden
	// in things he left them in (a coffee can, a filing cabinet, a crock, a tobacco tin: <see cref="Stash"/>; their Group
	// is empty). N is each page's own number (its flag), not its place.
	public static readonly IReadOnlyList<Page> All = new List<Page>
	{
		new(11, "", "Signed the register. Nobody else has since August. I left my spare film in the can on the shelf in case I come back for it. If you're reading this, I didn't. - R.H."),
		new(7, "", "Built this fire to have something to look at that isn't the trees. The thing that walks over them stopped when I lit it. I don't think it likes being watched back. The other half of the key's under the stones, where the arrows end."),
		new(8, "", "Every drawer in here is files about the stairs. Dates, places, a photograph clipped to each. They've been finding them for forty years. Nobody filed the last one. I think it's mine."),
		new(1, "respawn_Act11GiantEncounter", "The lake isn't on the map. Neither is whatever's on the far side of it. There's a boat tied at the dock, knotted tight and ugly. I didn't tie it. I don't trust it. I'm going round by the shore."),
		new(2, "respawn_Act12LakeCrossed", "Barricaded from the inside. Took me an hour to get in. Dust on everything, years of it. A logbook, a name on the desk. I read the first pages and stopped."),
		new(9, "", "The rangers knew. There's a map in the second drawer with every staircase marked in red pencil and a note across it: DO NOT CLIMB. Somebody climbed anyway. Somebody always does."),
		new(3, "respawn_Act13Finished", "They burned the faces out of every one of them on purpose. I think it was so it couldn't look out through them. I'm not staying in here."),
		new(4, "respawn_Act16Finished", "The hand knows the way down. I let it lead. I should have turned round at the steps. I keep saying that like it was a choice."),
		new(5, "respawn_Act21ChurchReached", "Snow up there. In October. The bells don't ring here, they breathe. If you've come this far you can get out. I couldn't find the way."),
		new(10, "", "Ate the last of the crackers in the pantry. Something ate the rest before me and left the tins clean. It didn't use a can opener."),
		new(12, "", "The bar tab's under the counter, last entry the night of the storm. Forty names. I counted the coats in the hall. Forty coats."),
		new(6, "respawn_Act23Room203Done", "It wears the ones it takes. It wore my voice at the lodge door and I nearly let myself in. If you hear me after this, it isn't me. - R.H."),
	};

	public static string Flag(int n) => $"read_rh_page_{n}";
	public static int Read => StoryManager.Instance is { } s ? CountRead(s) : 0;
	private static int CountRead(StoryManager s) { int c = 0; foreach (var p in All) if (s.HasFlag(Flag(p.N))) c++; return c; }
	public static readonly List<Readable> Placed = new();

	/// <summary>One of the hidden pages (<paramref name="n"/>), folded inside <paramref name="prop"/> (a can, a drawer, a
	/// crock): "Look in the <paramref name="what"/>" finds it and reads it (and keeps it: the album's page), after that it's
	/// "Read the page" again. <paramref name="local"/> is the pick point, in the prop's own frame.</summary>
	public static Readable Stash(Node3D prop, int n, string what, Vector3 local, string doodle = "")
	{
		if (prop == null || !GodotObject.IsInstanceValid(prop)) return null;
		Page page = null;
		foreach (var pg in All) if (pg.N == n) page = pg;
		if (page == null) return null;
		var r = new Readable
		{
			Name = $"RHPage{n}", Title = $"A page torn from a notebook, folded small", Text = page.Text, Style = Readable.NoteStyle.Handwritten,
			ReadFlag = Flag(n), PickRadius = 0.16f, MaxDistance = 2.2f, Position = local, Doodle = doodle,
		};
		bool found = StoryManager.Instance?.HasFlag(Flag(n)) == true;
		r.Prompt = found ? "Read the page" : $"Look in the {what}";
		r.Read += _ =>
		{
			if (r.Prompt != "Read the page") Audio.AudioDirector.OneShot(r, "can_clatter", 1, r.GlobalPosition, -14f, "Events", 4f, 0.08f);
			r.Prompt = "Read the page";
		};
		prop.AddChild(r);
		Placed.Add(r);
		return r;
	}

	/// <summary>Lays the pages by their saves (call once the markers are up; the floors under them may come later, so
	/// each page drops onto its floor once it's there).</summary>
	public static void Place(Node3D host)
	{
		Placed.RemoveAll(r => !GodotObject.IsInstanceValid(r));
		foreach (var p in All)
		{
			if (host.GetTree().GetFirstNodeInGroup(p.Group) is not Node3D m) continue;
			var drop = new LorePageDrop { Name = $"RHPage{p.N}", Page = p };
			host.AddChild(drop);
			drop.GlobalPosition = m.GlobalPosition;
			drop.GlobalRotation = m.GlobalRotation;
		}
	}
}

/// <summary>One of R.H.'s pages, put down on the floor a step ahead of its save once the floor is there.</summary>
public partial class LorePageDrop : Node3D
{
	public LorePages.Page Page;
	public Readable Readable { get; private set; }
	private float _retry;

	public override void _Process(double delta)
	{
		if (Readable != null) { SetProcess(false); return; }
		_retry -= (float)delta;
		if (_retry > 0f) return;
		_retry = 1f;
		if (StoryBeat.Player(this) is not { } pl || pl.GlobalPosition.DistanceTo(GlobalPosition) > 40f) return;
		// a step ahead and to the side of where they stand at the save, on the floor
		var at = GlobalTransform * new Vector3(0.7f, 1.0f, -1.3f);
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(at, at + Vector3.Down * 2.5f, 1));
		if (hit.Count == 0) return;
		var ground = (Vector3)hit["position"];
		var root = new Node3D { Name = "Page" };
		AddChild(root);
		root.GlobalPosition = ground + Vector3.Up * 0.004f;
		root.GlobalRotation = Vector3.Zero;
		Readable = PaperKit.Flat(root, Vector3.Zero, Page.N * 37f % 360f, new Vector2(0.15f, 0.2f), PaperKit.Look.Lined,
			$"A page torn from a notebook ({Page.N})", Page.Text, Readable.NoteStyle.Handwritten, prompt: "Read the page", seed: 300 + Page.N);
		Readable.ReadFlag = LorePages.Flag(Page.N);
		Readable.PickRadius = 0.16f;
		LorePages.Placed.Add(Readable);
	}
}
