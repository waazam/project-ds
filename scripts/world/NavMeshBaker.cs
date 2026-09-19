using Godot;

namespace ProjectDS.World;

/// <summary>
/// Bakes a runtime NavigationMesh over the terrain and every static building
/// (cabin, shed, footbridge, the original staircase) once the world has
/// finished generating, and again whenever Act 6 adds new obstacles (the
/// giant trees and the fifteen mini staircases around the clearing). Reads
/// only physics layer 1 ("World"), the same layer every static body in the
/// level already uses, via static collision shapes rather than render meshes
/// (much cheaper to voxelize). The autotest bot uses this for real
/// pathfinding around obstacles instead of straight-line steering; nothing
/// else in the game depends on it, so a bake that's late or a touch coarse
/// never blocks real play.
/// </summary>
[GlobalClass]
public partial class NavMeshBaker : Node
{
	public static NavMeshBaker Instance { get; private set; }

	[Export] public NodePath RegionPath = "../NavRegion";
	[Export] public NodePath ParseRootPath = "..";
	[Export] public Vector3 BakeAabbMin = new(-90f, -20f, -700f);
	[Export] public Vector3 BakeAabbSize = new(300f, 90f, 760f);
	[Export] public float AgentRadius = 0.4f;
	[Export] public float AgentHeight = 1.8f;
	[Export] public float AgentMaxClimb = 0.5f;
	[Export] public float AgentMaxSlope = 52f;
	[Export] public float CellSize = 0.75f;
	[Export] public float CellHeight = 0.4f;

	/// <summary>True once at least one bake has finished and agents can trust a path.</summary>
	public bool NavReady { get; private set; }

	private NavigationRegion3D _region;
	private Node _parseRoot;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		_region = GetNodeOrNull<NavigationRegion3D>(RegionPath);
		_parseRoot = GetNodeOrNull(ParseRootPath) ?? GetTree().Root;
		// ForestScatter's tree/collider build is deferred a frame past _Ready; give everything
		// (terrain, scatter, cabin, shed, bridge, the staircase) a few frames to finish first.
		_ = BakeAfterFrames(3);
	}

	private async System.Threading.Tasks.Task BakeAfterFrames(int frames)
	{
		for (int i = 0; i < frames; i++)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Bake();
	}

	/// <summary>Re-run this after new static geometry appears (e.g. Act 6's clearing dressing).</summary>
	public void Bake()
	{
		if (_region == null) return;
		var sw = System.Diagnostics.Stopwatch.StartNew();
		var mesh = new NavigationMesh
		{
			GeometryParsedGeometryType = NavigationMesh.ParsedGeometryType.StaticColliders,
			GeometryCollisionMask = 1,
			AgentRadius = AgentRadius,
			AgentHeight = AgentHeight,
			AgentMaxClimb = AgentMaxClimb,
			AgentMaxSlope = AgentMaxSlope,
			CellSize = CellSize,
			CellHeight = CellHeight,
			FilterBakingAabb = new Aabb(BakeAabbMin, BakeAabbSize),
		};
		var sourceGeometry = new NavigationMeshSourceGeometryData3D();
		NavigationServer3D.ParseSourceGeometryData(mesh, sourceGeometry, _parseRoot, new Callable());
		NavigationServer3D.BakeFromSourceGeometryData(mesh, sourceGeometry, new Callable());
		_region.NavigationMesh = mesh;
		NavReady = true;
		GD.Print($"[nav] navmesh baked in {sw.ElapsedMilliseconds} ms, {mesh.GetPolygonCount()} polys");
	}
}
