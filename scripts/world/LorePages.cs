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

	public static readonly IReadOnlyList<Page> All = new List<Page>
	{
		new(1, "respawn_Act11GiantEncounter", "The lake isn't on the map. Neither is whatever's on the far side of it. There's a boat tied at the dock, knotted tight and ugly. I didn't tie it. I don't trust it. I'm going round by the shore."),
		new(2, "respawn_Act12LakeCrossed", "Barricaded from the inside. Took me an hour to get in. Dust on everything, years of it. A logbook, a name on the desk. I read the first pages and stopped."),
		new(3, "respawn_Act13Finished", "They burned the faces out of every one of them on purpose. I think it was so it couldn't look out through them. I'm not staying in here."),
		new(4, "respawn_Act16Finished", "The hand knows the way down. I let it lead. I should have turned round at the steps. I keep saying that like it was a choice."),
		new(5, "respawn_Act21ChurchReached", "Snow up there. In October. The bells don't ring here, they breathe. If you've come this far you can get out. I couldn't find the way."),
		new(6, "respawn_Act23Room203Done", "It wears the ones it takes. It wore my voice at the lodge door and I nearly let myself in. If you hear me after this, it isn't me. - R.H."),
	};

	public static string Flag(int n) => $"read_rh_page_{n}";
	public static int Read => StoryManager.Instance is { } s ? CountRead(s) : 0;
	private static int CountRead(StoryManager s) { int c = 0; foreach (var p in All) if (s.HasFlag(Flag(p.N))) c++; return c; }
	public static readonly List<Readable> Placed = new();

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
