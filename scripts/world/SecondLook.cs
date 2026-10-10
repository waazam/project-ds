using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Wrong on a second look (2026-10-10): rare things, rolled once a run, that are only there if you look twice.
/// <list type="bullet">
/// <item><b>The portrait's eyes</b> (Room 3, Act 14; one run in four): in one of the burnt-out faces, eyes, wet and pale,
/// following you down the hall. Stare straight at it and they close, and aren't there.</item>
/// <item><b>Someone in the picture</b> (the Hollow at night; one run in four, once): a photo taken out in the dark woods
/// comes out with someone standing at the tree line, faint, at the edge of the frame. Nobody was there. It's only seen in
/// the album.</item>
/// </list>
/// Never more than that, never twice the same way in a run, and never anything that moves at you.
/// </summary>
public static class SecondLook
{
	private static bool _rolled, _eyesRun, _figureRun, _figureUsed;

	private static void Roll()
	{
		if (_rolled) return;
		_rolled = true;
		bool force = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--second-look") >= 0;
		_eyesRun = force || GD.Randf() < 0.25f;
		_figureRun = force || GD.Randf() < 0.25f;
	}

	public static bool FigureTaken => _figureUsed;

	// ------------------------------------------------------------------ the portrait

	/// <summary>Maybe give one of Room 3's portraits eyes (call once its portraits are hung).</summary>
	public static void PortraitEyes(Node3D room)
	{
		Roll();
		if (!_eyesRun) return;
		var faces = new List<Node3D>();
		foreach (var c in room.GetChildren())
			if (c is Node3D n && n.Name.ToString().StartsWith("Portrait") && n.GetNodeOrNull<MeshInstance3D>("Canvas") != null) faces.Add(n);
		if (faces.Count == 0) return;
		var p = faces[(int)(GD.Randi() % faces.Count)];
		var size = ((QuadMesh)p.GetNode<MeshInstance3D>("Canvas").Mesh).Size;
		p.AddChild(new PortraitEyes { Name = "Eyes", Position = new Vector3(0, size.Y * 0.13f, 0.02f), Spread = size.X * 0.075f });
		GD.Print($"[story] second look: {p.Name} has eyes this run");
	}

	// ------------------------------------------------------------------ the photo

	/// <summary>Maybe put someone at the tree line in this picture (a dark-woods shot, not of anything on the list that
	/// hunts you). True if it did.</summary>
	public static bool MaybeFigure(Image img, string subject)
	{
		Roll();
		if (!_figureRun || _figureUsed || img == null) return false;
		if (PhotoCatalog.Get(subject) is { Monster: true }) return false;
		var s = StoryManager.Instance;
		if (s == null || s.Current < Checkpoint.Act2StairsClimbed || s.Current >= Checkpoint.Act12LakeCrossed) return false;
		if (Audio.ForestAmbienceManager.Instance is { IsIndoor: true }) return false;
		bool force = System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--second-look") >= 0;
		if (!force && GD.Randf() > 0.15f) return false;
		_figureUsed = true;
		DrawFigure(img);
		GD.Print("[story] second look: someone is standing in the photo");
		return true;
	}

	/// <summary>A dark standing figure, faint, soft-edged, at the frame's edge on the tree line.</summary>
	private static void DrawFigure(Image img)
	{
		if (img.GetFormat() != Image.Format.Rgb8 && img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
		int w = img.GetWidth(), h = img.GetHeight();
		bool left = GD.Randf() < 0.5f;
		float cx = w * (left ? (float)GD.RandRange(0.1, 0.2) : (float)GD.RandRange(0.8, 0.9));
		float feet = h * (float)GD.RandRange(0.62, 0.7), tall = h * (float)GD.RandRange(0.2, 0.26);
		float head = tall * 0.11f;
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float u = (x - cx) / tall, v = (feet - y) / tall;   // v: 0 feet .. 1 crown
				if (v < -0.05f || v > 1.05f || Mathf.Abs(u) > 0.4f) continue;
				// head, shoulders, a long body narrowing to the legs; arms hanging
				float d = 1f;
				float hx = u, hy = v - 0.89f;
				d = Mathf.Min(d, Mathf.Sqrt(hx * hx + hy * hy * 0.8f) / (head / tall));
				float bodyHalf = v > 0.74f ? 0f : Mathf.Lerp(0.065f, 0.15f, Mathf.SmoothStep(0f, 0.74f, v));
				if (v <= 0.78f && v >= 0f) d = Mathf.Min(d, Mathf.Abs(u) / Mathf.Max(bodyHalf, 0.001f));
				if (v > 0.74f && v <= 0.8f) d = Mathf.Min(d, Mathf.Abs(u) / 0.06f);
				float inside = 1f - Mathf.SmoothStep(0.75f, 1.15f, d);
				if (inside <= 0f) continue;
				float fadeUp = Mathf.SmoothStep(-0.05f, 0.25f, v);   // (its feet lost in the undergrowth)
				float k = inside * fadeUp * 0.45f;
				var c = img.GetPixel(x, y);
				img.SetPixel(x, y, new Color(c.R * (1f - k), c.G * (1f - k), c.B * (1f - k), c.A));
			}
	}
}

/// <summary>Two pale wet eyes in a burnt-out face, following the camera; looked at straight on, they close.</summary>
public partial class PortraitEyes : Node3D
{
	public float Spread = 0.05f;
	private readonly Node3D[] _eyes = new Node3D[2];
	private float _stare, _close;
	private bool _gone;

	public override void _Ready()
	{
		var white = new StandardMaterial3D { AlbedoColor = new Color(0.72f, 0.68f, 0.6f), Roughness = 0.15f, MetallicSpecular = 0.8f };
		var pupil = new StandardMaterial3D { AlbedoColor = new Color(0.02f, 0.015f, 0.01f), Roughness = 0.2f };
		for (int i = 0; i < 2; i++)
		{
			var e = new Node3D { Name = $"Eye{i}", Position = new Vector3((i == 0 ? -1 : 1) * Spread, 0, 0) };
			AddChild(e);
			e.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.012f, Height = 0.024f, RadialSegments = 10, Rings = 6 }, MaterialOverride = white, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
			e.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.0055f, Height = 0.011f, RadialSegments = 8, Rings = 4 }, MaterialOverride = pupil, Position = new Vector3(0, 0, 0.009f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
			_eyes[i] = e;
		}
	}

	public override void _Process(double delta)
	{
		if (_gone) return;
		float dt = (float)delta;
		var cam = GetViewport().GetCamera3D();
		if (cam == null) return;
		foreach (var e in _eyes)
		{
			var to = cam.GlobalPosition - e.GlobalPosition;
			if (to.LengthSquared() > 0.0001f)
			{
				// (they turn only so far in their sockets)
				var local = GlobalBasis.Inverse() * to.Normalized();
				local.Z = Mathf.Max(local.Z, 0.55f);
				e.Basis = Basis.LookingAt(-local.Normalized(), Vector3.Up);
			}
		}
		// stared at straight on (and near enough to tell), they close, and they're not there
		var dir = (GlobalPosition - cam.GlobalPosition);
		bool staring = dir.Length() < 9f && (-cam.GlobalBasis.Z).Dot(dir.Normalized()) > 0.995f;
		_stare = staring ? _stare + dt : Mathf.Max(0f, _stare - dt);
		if (_stare > 0.7f || _close > 0f)
		{
			_close += dt / 0.25f;
			foreach (var e in _eyes) e.Scale = new Vector3(1f, Mathf.Max(0.02f, 1f - _close), 1f);
			if (_close >= 1f) { _gone = true; Visible = false; PlayerBreathing.Startle(0.5f); GD.Print("[story] second look: the portrait's eyes closed"); }
		}
	}
}
