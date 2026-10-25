using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// What only film sees (2026-10-24, the game-loop pass: "bring the camera back as a recurring tool ... the film shows
/// what your eyes don't"). A pale figure, too tall and too thin, standing among the trees where nobody is: never
/// drawn, except in the one frame the camera's shutter takes (<see cref="CameraTool.ShutterOpening"/>), so it's there in
/// the print and not when the camera comes down. Caught once (in frame and in range), it's gone from that place for
/// good (<see cref="Flag"/>), with a breath close behind; every one caught counts toward <see cref="Caught"/>.
///
/// <see cref="Haunt"/> stands one a set way from a marker in the level (they're placed off the hollow's landmarks:
/// across the creek from the lookout, at the bunker's tree line, beyond the clearing's stairs, back from the Act 5
/// cabin's porch).
/// </summary>
public partial class FilmGhost : Node3D
{
	public string Id = "";
	/// <summary>How far off it can be caught (m) and still be more than a smudge.</summary>
	public float Range = 34f;
	public static string Flag(string id) => $"film_ghost_{id}";
	public static readonly List<FilmGhost> All = new();
	/// <summary>How many have been caught on film, this game.</summary>
	public static int Caught
	{
		get
		{
			int n = 0;
			if (StoryManager.Instance is { } s) foreach (var id in Ids) if (s.HasFlag(Flag(id))) n++;
			return n;
		}
	}
	public static readonly string[] Ids = { "clearing", "porch", "lookout", "treeline" };

	private Node3D _body;
	private bool _shown;

	public override void _Ready()
	{
		if (StoryManager.Instance?.HasFlag(Flag(Id)) == true) { QueueFree(); return; }
		_body = Build();
		_body.Visible = false;
		AddChild(_body);
		All.Add(this);
		CameraTool.ShutterOpening += OnShutter;
		CameraTool.ShutterClosed += OnClosed;
	}

	public override void _ExitTree()
	{
		All.Remove(this);
		CameraTool.ShutterOpening -= OnShutter;
		CameraTool.ShutterClosed -= OnClosed;
	}

	/// <summary>In the frame (its head and its middle inside the camera's view) and within range.</summary>
	public bool InFrame(Camera3D cam)
	{
		Vector3 mid = GlobalPosition + Vector3.Up * 1.3f;
		if (cam.GlobalPosition.DistanceTo(mid) > Range) return false;
		if (cam.IsPositionBehind(mid)) return false;
		var vp = cam.GetViewport().GetVisibleRect().Size;
		var sp = cam.UnprojectPosition(mid);
		return sp.X > vp.X * 0.08f && sp.X < vp.X * 0.92f && sp.Y > vp.Y * 0.08f && sp.Y < vp.Y * 0.92f;
	}

	private void OnShutter(Camera3D cam)
	{
		if (!IsInsideTree() || cam == null || !InFrame(cam)) return;
		_body.Visible = true;
		_shown = true;
		// facing whoever's taking it
		var to = (cam.GlobalPosition - GlobalPosition) with { Y = 0 };
		if (to.LengthSquared() > 0.01f) _body.GlobalBasis = Basis.LookingAt(-to.Normalized(), Vector3.Up);
	}

	private void OnClosed()
	{
		if (!_shown) return;
		_shown = false;
		_body.Visible = false;
		StoryManager.Instance?.SetFlag(Flag(Id));
		GD.Print($"[story] caught on film where nobody was standing: {Id} ({Caught}/{Ids.Length})");
		// a breath, close behind them, a moment after
		var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
		if (player != null)
		{
			var at = player.GlobalPosition + (player.GlobalBasis.Z with { Y = 0 }).Normalized() * 1.2f + Vector3.Up * 1.6f;
			GetTree().CreateTimer(1.4).Timeout += () => Audio.AudioDirector.OneShot(player, "breath_out", 5, at, -14f, "Unnatural", 4f, 0.05f);
		}
		QueueFree();
	}

	/// <summary>The figure: pale and thin, a head too small on a long neck, arms down past its knees, no face but two
	/// dark eyes. Unshaded, so the flash or the dark don't lose it.</summary>
	private static Node3D Build()
	{
		var k = new MeshKit();
		var pale = new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.77f, 0.74f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		var dark = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.02f, 0.02f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		k.Mat(pale);
		k.Cylinder(new Vector3(0.07f, 0f, 0f), new Vector3(0.06f, 1.2f, 0f), 0.04f, 0.05f, 6);      // legs
		k.Cylinder(new Vector3(-0.07f, 0f, 0f), new Vector3(-0.06f, 1.2f, 0f), 0.04f, 0.05f, 6);
		k.Cylinder(new Vector3(0f, 1.15f, 0f), new Vector3(0f, 2.05f, 0f), 0.11f, 0.13f, 8);        // body
		k.Cylinder(new Vector3(0f, 2.05f, 0f), new Vector3(0f, 2.28f, 0.02f), 0.04f, 0.035f, 6);    // neck
		k.Cylinder(new Vector3(0.15f, 2.0f, 0f), new Vector3(0.2f, 0.75f, 0.04f), 0.035f, 0.025f, 6); // arms
		k.Cylinder(new Vector3(-0.15f, 2.0f, 0f), new Vector3(-0.2f, 0.75f, 0.04f), 0.035f, 0.025f, 6);
		k.Cylinder(new Vector3(0f, 2.26f, 0.02f), new Vector3(0f, 2.46f, 0.02f), 0.075f, 0.065f, 8, true); // head
		k.Mat(dark);
		foreach (float x in new[] { -0.028f, 0.028f })
			k.Cylinder(new Vector3(x, 2.37f, 0.08f), new Vector3(x, 2.37f, 0.11f), 0.014f, 0.014f, 6);
		var n = new Node3D { Name = "Figure" };
		var mi = k.CommitTo(n, "Mesh", false);
		return n;
	}

	/// <summary>The hollow's four (SurveyLot calls it once the level's up): beyond the clearing's stairs, back from the
	/// Act 5 cabin, out toward the burning cabin from the lookout, at the bunker's tree line.</summary>
	public static void HauntHollow(Node host)
	{
		var tree = host.GetTree();
		Vector3? P(string group) => tree.GetFirstNodeInGroup(group) is Node3D n ? n.GlobalPosition : null;
		var lookout = P("fire_lookout_marker");
		var bunker = P("bunker_marker");
		var clearing = P("stairs_clearing_marker");
		Node3D cabin5 = null;
		foreach (var n in tree.GetNodesInGroup("cabin")) if (n is Node3D c && c.Name == "Cabin") cabin5 = c;
		if (clearing is { } cl) HauntAt(host, "clearing", cl + new Vector3(19f, 0f, -7f));
		if (cabin5 != null) HauntAt(host, "porch", cabin5.GlobalPosition + cabin5.GlobalBasis * new Vector3(0f, 0f, -17f));
		if (lookout is { } lo)
		{
			var d = (new Vector3(70f, 0f, -535f) - lo) with { Y = 0 };
			d = d.Normalized();
			HauntAt(host, "lookout", lo + d * 26f + d.Cross(Vector3.Up) * 7f);
		}
		if (bunker is { } bu && lookout is { } lo2)
		{
			var d = ((lo2 - bu) with { Y = 0 }).Normalized();
			HauntAt(host, "treeline", bu + d * 17f - d.Cross(Vector3.Up) * 10f);
		}
	}

	private static void HauntAt(Node host, string id, Vector3 at)
	{
		var terrain = GroundSnap.FindTerrain(host);
		if (terrain != null) at = terrain.GroundPoint(at.X, at.Z);
		var g = new FilmGhost { Name = $"FilmGhost_{id}", Id = id };
		host.AddChild(g);
		g.GlobalPosition = at;
	}

	/// <summary>Stands a ghost <paramref name="id"/> at the marker in group <paramref name="group"/>, moved <paramref name="offset"/>
	/// in the marker's own frame (flat), on the ground.</summary>
	public static void Haunt(Node host, string id, string group, Vector3 offset)
	{
		if (host.GetTree().GetFirstNodeInGroup(group) is not Node3D m) return;
		var b = new Basis(Vector3.Up, m.GlobalRotation.Y);
		Vector3 at = m.GlobalPosition + b * offset;
		var terrain = GroundSnap.FindTerrain(host);
		if (terrain != null) at = terrain.GroundPoint(at.X, at.Z);
		var g = new FilmGhost { Name = $"FilmGhost_{id}", Id = id };
		host.AddChild(g);
		g.GlobalPosition = at;
	}
}
