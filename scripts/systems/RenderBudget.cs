using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// The optimization pass (the owner's alpha polish): the Hollow's underground (the station, its rooms and
/// stairwell, the long hallway, the sewer, the pit, the library, the round room, the long stair and the
/// church) all sit within the camera's 400 m reach of each other, walled off by nothing but rock, and
/// nothing culls what's behind rock: every one of them was being drawn from every other. Once they've
/// built, this gives every mesh, particle system and multimesh under the station a visibility range (its
/// own reach plus a wide margin, so nothing ever pops in view of where it can be seen from), and every
/// light a distance fade (its shadow fading sooner). Nothing changes up close.
///
/// <para><b>The areas, gated</b> (the second optimization pass, 2026-10-02): the underground's areas in the story's
/// order (the station, the stairwell, the long hallway, the sewer, the pit, the library, the round room, the long
/// stair, the church, the winter woods, the lodge). Only the one the player is in and the ones either side of it are
/// drawn at all; the rest are switched off (their meshes' ranges shut, their lights faded out), however near they lie
/// through the rock. It's done with the ranges and fades this already owns, so it never fights the story's own
/// showing and hiding of things. The area is the smallest one whose own things' bounds hold the player.</para>
/// </summary>
public partial class RenderBudget : Node
{
	public int Meshes { get; private set; }
	public int Lights { get; private set; }
	public bool Done { get; private set; }
	/// <summary>For tests and the debug readout: the area the player's in (or "-"), and how many areas are drawn.</summary>
	public string CurrentArea { get; private set; } = "-";
	public int AreasDrawn { get; private set; }

	/// <summary>`--no-gate` (measuring): every area drawn, as before the gating.</summary>
	private static readonly bool NoGate = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--no-gate") >= 0;

	public const float Margin = 140f, MinEnd = 180f, LightFade = 90f, ShadowFade = 45f;

	private sealed class Area
	{
		public string Name;
		public Node3D Root;
		public Aabb Bounds;
		public bool HasBounds;
		public readonly List<(GeometryInstance3D g, float end)> Geo = new();
		public readonly List<Light3D> Lights = new();
		public bool On = true;
	}

	private readonly List<Area> _areas = new();
	private readonly Dictionary<Node, Area> _byRoot = new();
	private Area _current;
	private double _nextCheck;

	public override void _Ready() => _ = Apply();

	private async Task Apply()
	{
		for (int i = 0; i < 90 && !IsInsideTree(); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		for (int i = 0; i < 300; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			if (StationInterior.Instance?.Boss?.Library?.Round?.Stair?.Church?.FontKeyPickup != null || StationInterior.Instance is { Built: false }) break;
		}
		for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		if (StationInterior.Instance is Node3D st)
		{
			// the areas, in the story's order
			var s = StationInterior.Instance;
			void Add(string name, Node3D root) { if (root != null && IsInstanceValid(root)) { var a = new Area { Name = name, Root = root }; _areas.Add(a); _byRoot[root] = a; } }
			Add("station", s);
			Add("stairwell", s.Room3?.Stairs);
			Add("hallway", s.Room3?.Stairs?.Hallway);
			Add("sewer", s.Sewer);
			Add("pit", s.Boss);
			Add("library", s.Boss?.Library);
			Add("round room", s.Boss?.Library?.Round);
			Add("long stair", s.Boss?.Library?.Round?.Stair);
			Add("church", s.Boss?.Library?.Round?.Stair?.Church);
			Add("woods", s.Boss?.Library?.Round?.Stair?.Church?.Woods);
			Add("lodge", s.Boss?.Library?.Round?.Stair?.Church?.Woods?.Lodge);
			Walk(st, _byRoot.GetValueOrDefault(st));
		}
		Done = true;
		StoryManager.LoadStage("the deferred builds done (render budget walked)");
		GD.Print($"[render] budget: {Meshes} meshes ranged, {Lights} lights faded under the station; {_areas.Count} areas gated");
	}

	private void Walk(Node n, Area area)
	{
		if (n != area?.Root && _byRoot.TryGetValue(n, out var own)) area = own;
		switch (n)
		{
			case GeometryInstance3D g when g.VisibilityRangeEnd <= 0f:
			{
				// the farthest this instance reaches from its own origin, whichever way the engine measures
				var box = g switch
				{
					MeshInstance3D mi when mi.Mesh != null => mi.Mesh.GetAabb(),
					MultiMeshInstance3D mm when mm.Multimesh != null => mm.Multimesh.GetAabb(),
					GpuParticles3D gp => gp.VisibilityAabb,
					_ => new Aabb(Vector3.Zero, Vector3.Zero),
				};
				float reach = 0f;
				for (int c = 0; c < 8; c++) reach = Mathf.Max(reach, box.GetEndpoint(c).Length());
				reach *= g.GlobalBasis.Scale.X;
				g.VisibilityRangeEnd = Mathf.Max(MinEnd, reach + Margin);
				g.VisibilityRangeEndMargin = 20f;
				g.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
				Meshes++;
				if (area != null)
				{
					area.Geo.Add((g, g.VisibilityRangeEnd));
					// the area's extent: its own meshes' (not the long reach of a sky or a fog card)
					if (g is MeshInstance3D && box.Size.Length() < 2000f)
					{
						var wb = g.GlobalTransform * box;
						area.Bounds = area.HasBounds ? area.Bounds.Merge(wb) : wb;
						area.HasBounds = true;
					}
				}
				break;
			}
			case Light3D l when l is not DirectionalLight3D && !l.DistanceFadeEnabled:
				l.DistanceFadeEnabled = true;
				l.DistanceFadeBegin = LightFade;
				l.DistanceFadeLength = 20f;
				l.DistanceFadeShadow = ShadowFade;
				Lights++;
				area?.Lights.Add(l);
				break;
		}
		foreach (var c in n.GetChildren()) Walk(c, area);
	}

	public override void _Process(double delta)
	{
		if (!Done || _areas.Count == 0 || NoGate) return;
		_nextCheck -= delta;
		if (_nextCheck > 0) return;
		_nextCheck = 0.1;
		DarkSuns();
		var player = GetTree().GetFirstNodeInGroup("player") as Node3D;
		if (player == null) return;
		var p = player.GlobalPosition;
		// the smallest area whose bounds (a little grown) hold the player
		Area at = null;
		float best = float.MaxValue;
		foreach (var a in _areas)
		{
			if (!a.HasBounds || !IsInstanceValid(a.Root)) continue;
			var b = a.Bounds.Grow(4f);
			if (!b.HasPoint(p)) continue;
			float v = b.Size.X * b.Size.Y * b.Size.Z;
			if (v < best) { best = v; at = a; }
		}
		if (at == _current && _current != null) return;
		_current = at;
		CurrentArea = at?.Name ?? "-";
		// drawn: this area and its neighbours in the story's order (out of every area: the station, which stands at
		// the surface)
		int i = at != null ? _areas.IndexOf(at) : 0;
		int drawn = 0;
		for (int j = 0; j < _areas.Count; j++)
		{
			bool on = at == null ? j == 0 : Mathf.Abs(j - i) <= 1;
			if (on) { drawn++; DressArea(_areas[j], j); }
			Set(_areas[j], on);
		}
		AreasDrawn = drawn;
	}

	/// <summary>Its stains and rot (DecalDresser), laid the first time it's drawn (the fidelity pass, 2026-10-02).</summary>
	private static void DressArea(Area a, int index)
	{
		if (!a.HasBounds || !IsInstanceValid(a.Root)) return;
		DecalDresser.Theme? theme = a.Name switch
		{
			"station" => DecalDresser.Theme.Station,
			"stairwell" or "long stair" => DecalDresser.Theme.Stair,
			"hallway" => DecalDresser.Theme.Hallway,
			"sewer" => DecalDresser.Theme.Sewer,
			"library" => DecalDresser.Theme.Library,
			"church" => DecalDresser.Theme.Church,
			"lodge" => DecalDresser.Theme.Lodge,
			_ => null,   // the pit, the round room (its webs), the woods: left as they are
		};
		// (room 201 is kept perfect: none in it)
		System.Func<Vector3, bool> skip = a.Root is SkiLodge lodge ? lodge.InRoom201 : null;
		if (theme is { } t) DecalDresser.Dress(a.Root, t, 101 + index, a.Bounds, skip);
	}

	private readonly List<DirectionalLight3D> _suns = new();
	private readonly HashSet<DirectionalLight3D> _sunsOff = new();

	/// <summary>A sun that's gone dark (the surface's, underground: its energy at nothing) is switched off, and back on
	/// the moment it has any light to give. Dark, it still drew its shadow map of everything round the player every
	/// frame: 1.5 ms of the lodge's 7.</summary>
	private void DarkSuns()
	{
		if (_suns.Count == 0)
			foreach (var n in GetTree().CurrentScene.FindChildren("*", "DirectionalLight3D", true, false))
				if (n is DirectionalLight3D d) _suns.Add(d);
		foreach (var d in _suns)
		{
			if (!IsInstanceValid(d)) continue;
			bool dark = d.LightEnergy < 0.005f;
			if (dark && d.Visible) { d.Visible = false; _sunsOff.Add(d); }
			else if (!dark && _sunsOff.Contains(d)) { d.Visible = true; _sunsOff.Remove(d); }
		}
	}

	private static void Set(Area a, bool on)
	{
		if (a.On == on) return;
		a.On = on;
		foreach (var (g, end) in a.Geo)
			if (IsInstanceValid(g)) g.VisibilityRangeEnd = on ? end : 0.001f;   // (0 would mean no range at all: drawn everywhere)
		foreach (var l in a.Lights)
			if (IsInstanceValid(l)) { l.DistanceFadeBegin = on ? LightFade : 0.001f; l.DistanceFadeLength = on ? 20f : 0.001f; }
	}
}
