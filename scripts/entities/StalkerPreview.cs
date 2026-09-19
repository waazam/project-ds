using System.Threading.Tasks;
using Godot;
using ProjectDS.World;

namespace ProjectDS.Entities;

/// <summary>
/// Dev harness for stalker_preview.tscn: stands the stalker in the forest and
/// screenshots it from first-person eye height (1.62 m, FOV 70) at several
/// distances, in the open and half behind a trunk, into test-output/stalker/.
/// The Stalker brain is switched off; only the body (and its idle) runs.
/// Not used by the game.
/// </summary>
public partial class StalkerPreview : Node3D
{
	[Export] public NodePath CameraPath = "../Camera3D";
	[Export] public NodePath StalkerPath = "../Stalker";
	[Export] public float Eye = 1.62f;
	[Export] public float[] TrailStations = { 140f, 230f };
	[Export] public float[] Distances = { 8f, 20f, 40f, 60f };

	private Camera3D _cam;
	private Node3D _stalker;
	private StalkerBody _body;
	private ForestTerrain _terrain;
	private string _out;

	public override void _Ready()
	{
		_cam = GetNode<Camera3D>(CameraPath);
		_stalker = GetNode<Node3D>(StalkerPath);
		_out = ProjectSettings.GlobalizePath("res://test-output/stalker");
		DirAccess.MakeDirRecursiveAbsolute(_out);
		Run();
	}

	private async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private void Log(string s) => GD.Print("[stalker-preview] " + s);

	private async void Run()
	{
		await Frames(30);
		foreach (var n in new[] { "ScreenFader", "PauseMenu", "DebugOverlay" })
			if (GetTree().Root.FindChild(n, true, false) is CanvasLayer cl) cl.Visible = false;
		_terrain = GetTree().GetFirstNodeInGroup("terrain") as ForestTerrain;
		_body = _stalker.GetNode<StalkerBody>("Body");
		_stalker.ProcessMode = ProcessModeEnum.Disabled;   // no brain
		_body.ProcessMode = ProcessModeEnum.Always;         // idle still runs
		_body.Skin.SetShaderParameter("visibility", ((Stalker)_stalker).MaxVisibility);   // as seen in game
		_stalker.Visible = true;
		Log($"triangles {_body.TriangleCount}");

		int station = 0;
		foreach (float s in TrailStations)
		{
			Vector3 p = _terrain.TrailPoint(s, out _);
			Vector3 eye = new(p.X, _terrain.HeightAt(p.X, p.Z) + Eye, p.Z);
			foreach (float d in Distances)
			{
				if (PlaceOpen(eye, d, out Vector3 look))
					await Shot($"s{station}_{d:00}m_open", eye, look);
				else Log($"no open spot at {d} m from s={s}");
				if (PlaceBehindTrunk(eye, d, out look))
					await Shot($"s{station}_{d:00}m_trunk", eye, look);
				else Log($"no trunk at {d} m from s={s}");
			}
			station++;
		}
		// Close reference, front and three-quarter, for shape.
		{
			Vector3 p = _terrain.TrailPoint(TrailStations[0], out _);
			Vector3 eye = new(p.X, _terrain.HeightAt(p.X, p.Z) + Eye, p.Z);
			if (PlaceOpen(eye, 3.2f, out Vector3 look)) await Shot("ref_03m_front", eye, look);
			_stalker.Rotation += new Vector3(0, 0.9f, 0);
			await Shot("ref_03m_threequarter", eye, look);
			_stalker.Rotation += new Vector3(0, 1.2f, 0);
			await Shot("ref_03m_side", eye, look);
			if (PlaceOpen(eye, 2.4f, out look)) await Shot("ref_02m_face", eye, _stalker.GlobalPosition + Vector3.Up * 1.9f);
		}
		GetTree().Quit(0);
	}

	private async Task Shot(string name, Vector3 eye, Vector3 look)
	{
		_cam.GlobalPosition = eye;
		_cam.LookAt(look, Vector3.Up);
		await Frames(10);
		var img = GetViewport().GetTexture().GetImage();
		img.SavePng($"{_out}/{name}.png");
		// A 3x nearest-neighbour crop around the figure, to judge the silhouette at 640x360.
		Vector3 mid = _stalker.GlobalPosition + Vector3.Up * 1.3f;
		if (!_cam.IsPositionBehind(mid))
		{
			Vector2 sp = _cam.UnprojectPosition(mid) * (img.GetWidth() / GetViewport().GetVisibleRect().Size.X);
			int w = 160, h = 90;
			int x = Mathf.Clamp((int)sp.X - w / 2, 0, img.GetWidth() - w), y = Mathf.Clamp((int)sp.Y - h / 2, 0, img.GetHeight() - h);
			var crop = img.GetRegion(new Rect2I(x, y, w, h));
			crop.Resize(w * 3, h * 3, Image.Interpolation.Nearest);
			crop.SavePng($"{_out}/{name}_zoom.png");
		}
		Log($"saved {name} (image {img.GetWidth()}x{img.GetHeight()}, visible {VisibleCount()}/{((Stalker)_stalker).SamplePoints.Length} sample points)");
	}

	private int VisibleCount()
	{
		int n = 0;
		foreach (var local in ((Stalker)_stalker).SamplePoints)
		{
			Vector3 p = _body.GlobalTransform * local;
			if (_cam.IsPositionInFrustum(p) && Clear(_cam.GlobalPosition, p)) n++;
		}
		return n;
	}

	private bool Clear(Vector3 from, Vector3 to)
	{
		var q = PhysicsRayQueryParameters3D.Create(from, to, 1);
		return GetWorld3D().DirectSpaceState.IntersectRay(q).Count == 0;
	}

	private void Face(Vector3 spot, Vector3 eye, float lean)
	{
		_stalker.GlobalPosition = spot;
		Vector3 to = eye - spot;
		_stalker.Rotation = new Vector3(0, Mathf.Atan2(to.X, to.Z), 0);
		_body.Rotation = new Vector3(0, 0, lean);
	}

	/// <summary>Stand it in the open d metres away with clear sight of feet, middle and head.</summary>
	private bool PlaceOpen(Vector3 eye, float d, out Vector3 look)
	{
		look = Vector3.Zero;
		for (int a = 0; a < 72; a++)
		{
			Vector3 dir = Vector3.Forward.Rotated(Vector3.Up, Mathf.Tau * a / 72f);
			Vector3 spot = eye + dir * d;
			spot.Y = _terrain.HeightAt(spot.X, spot.Z);
			if (!Clear(eye, spot + Vector3.Up * 0.4f) || !Clear(eye, spot + Vector3.Up * 1.4f) || !Clear(eye, spot + Vector3.Up * 2.4f)) continue;
			Vector3 side = dir.Cross(Vector3.Up).Normalized() * 0.5f;
			if (!Clear(eye, spot + side + Vector3.Up * 1.8f) || !Clear(eye, spot - side + Vector3.Up * 1.8f)) continue;
			Face(spot, eye, 0f);
			look = spot + Vector3.Up * 1.4f + side * 0.9f;   // a little off-centre, like a glance
			return true;
		}
		return false;
	}

	/// <summary>Stalker.TryPlace's rule: behind a trunk, a sliver of shoulder and head exposed, leaning out.</summary>
	private bool PlaceBehindTrunk(Vector3 eye, float d, out Vector3 look)
	{
		look = Vector3.Zero;
		var space = GetWorld3D().DirectSpaceState;
		for (int a = 0; a < 144; a++)
		{
			Vector3 dir = Vector3.Forward.Rotated(Vector3.Up, Mathf.Tau * a / 144f + 0.01f);
			var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, eye + dir * (d + 2f), 1));
			if (hit.Count == 0) continue;
			Vector3 at = (Vector3)hit["position"], normal = (Vector3)hit["normal"];
			float dist = eye.DistanceTo(at);
			if (Mathf.Abs(normal.Y) > 0.5f || dist < d - 3f) continue;
			foreach (float side in new[] { -1f, 1f })
			{
				Vector3 lateral = dir.Cross(Vector3.Up).Normalized() * side;
				var st = (Stalker)_stalker;
				Vector3 spot = at + dir * 0.55f + lateral * (st.ExposeOffset.X + st.ExposeOffset.Y) * 0.5f;   // as in game
				spot.Y = _terrain.HeightAt(spot.X, spot.Z);
				Face(spot, eye, Mathf.DegToRad(st.LeanDegrees) * -side);
				// Only keep spots where it is partly exposed, as in the game.
				int total = ((Stalker)_stalker).SamplePoints.Length, seen = 0;
				foreach (var local in ((Stalker)_stalker).SamplePoints)
					if (Clear(eye, _body.GlobalTransform * local)) seen++;
				if (seen == 0 || seen > total - 3) continue;
				look = at + Vector3.Up * (spot.Y + 1.3f - at.Y);
				return true;
			}
		}
		return false;
	}
}
