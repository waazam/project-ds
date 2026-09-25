using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.StairwellParts;
using ProjectDS.World.StationParts;

namespace ProjectDS.World;

/// <summary>
/// Act 20: the start of the slow climb back to the surface (its save is <see cref="Checkpoint.Act19Finished"/>,
/// on walking in from the passage behind the library's bookcase). A round stone room with a very
/// high ceiling, eight long, tall arched windows all the way round, every one bricked up. In the
/// middle, a round stone dais, thick with old spider web: the lighter burns it away. Standing in the
/// middle of it, the dais rises (twenty seconds, the player held) up the room, through a shaft in the
/// ceiling, into a small room above: <see cref="Checkpoint.Act20Finished"/>, and the demo ends there.
///
/// Local space: y=0 is the floor; the room's middle is x=z=0; the way in is on the -Z side.
/// </summary>
public partial class RoundRoom : Node3D
{
	public const float Radius = 7.5f, Height = 32f, DaisTop = 0.35f, DaisR = 2.0f, DaisFoot = 2.9f, ShaftR = 3.1f;
	/// <summary>How far the dais rises: from the floor to the room above.</summary>
	public const float Rise = 42f;
	public const float AscentSeconds = 20f;
	public const float TopR = 4.6f, TopH = 3.6f;
	public static readonly Vector3 EntryLocal = new(0, 0.05f, -Radius + 1.2f);
	public static readonly Vector3 TopLocal = new(0, Rise + DaisTop + 0.05f, 2.9f);

	public bool WebsBurned { get; private set; }
	public bool Rising { get; private set; }
	public bool Arrived { get; private set; }
	public Node3D Dais { get; private set; }
	public PickupInteractable WebUse { get; private set; }
	public Vector3 CentreWorld => ToGlobal(new Vector3(0, DaisTop + 0.05f, 0));
	public Vector3 DaisEdgeWorld => ToGlobal(new Vector3(0, 0.05f, -DaisFoot - 0.9f));
	public Vector3 EntryWorld => ToGlobal(EntryLocal);
	public Vector3 TopWorld => ToGlobal(TopLocal);

	private StaticBody3D _body, _barrier, _topFloor;
	private Node3D _webs;
	private AnimatableBody3D _daisBody;
	private readonly List<GeometryInstance3D> _webMeshes = new();
	private readonly RandomNumberGenerator _rng = new() { Seed = 2020 };
	private StandardMaterial3D _stone;
	private bool _checkpointed;

	public override void _Ready() => Callable.From(Build).CallDeferred();

	private void Build()
	{
		_body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		_body.SetMeta("surface", "stone");
		AddChild(_body);
		_stone = new StandardMaterial3D { AlbedoTexture = StairwellTextures.CleanConcrete, AlbedoColor = new Color(0.8f, 0.77f, 0.7f), Roughness = 0.88f, VertexColorUseAsAlbedo = true, Uv1Triplanar = true, Uv1WorldTriplanar = true, Uv1Scale = Vector3.One * 0.6f };
		BuildWalls();
		BuildWindows();
		BuildSconces();
		BuildDais();
		BuildWebs();
		BuildTopRoom();
		StoryBeat.MakeTrigger(this, new BoxShape3D { Size = new Vector3(3f, 2.4f, 1.2f) }, new Vector3(0, 1.2f, -Radius + 1.4f), OnEnter, "Act20Trigger");
		StoryBeat.MakeTrigger(this, new CylinderShape3D { Radius = 0.8f, Height = 2f }, new Vector3(0, DaisTop + 1f, 0), OnCentre, "DaisCentreTrigger");
		var s = StoryManager.Instance;
		if (s != null && (s.Current >= Checkpoint.Act20Finished || s.HasFlag(StoryManager.Flag.RoundRoomWebBurned))) ClearWebs();
		if (s != null && s.Current >= Checkpoint.Act20Finished) { Arrived = _checkpointed = true; PlaceDais(1f); SetTopFloor(true); }
		// the air here: still, a faint low hum high up
		var hum = "res://assets/audio/ambient/stairs_hum_loop.wav";
		if (ResourceLoader.Exists(hum))
		{
			var p = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(hum), Bus = "Events", VolumeDb = -22f, UnitSize = 8f, MaxDistance = 40f, Position = new Vector3(0, 12f, 0), Autoplay = true };
			if (p.Stream is AudioStreamWav w) { w = (AudioStreamWav)w.Duplicate(); w.LoopMode = AudioStreamWav.LoopModeEnum.Forward; w.LoopEnd = Mathf.RoundToInt(w.GetLength() * w.MixRate); p.Stream = w; }
			AddChild(p);
		}
	}

	// ------------------------------------------------------------------ building

	private void Seg(MeshKit k, Vector3 c, Vector3 s, float yaw, bool collide)
	{
		var b = new Basis(Vector3.Up, yaw);
		BuildKit.Box(k, c, s, 1f, BuildKit.Face.None, b);
		if (collide) _body.AddChild(new CollisionShape3D { Position = c, Rotation = new Vector3(0, yaw, 0), Shape = new BoxShape3D { Size = s } });
	}

	/// <summary>The drum of the walls, the floor, the ceiling with its round shaft.</summary>
	private void BuildWalls()
	{
		var k = new MeshKit();
		k.Mat(_stone);
		const int n = 40;
		float segW = Mathf.Tau * (Radius + 0.3f) / n + 0.05f;
		for (int i = 0; i < n; i++)
		{
			float a = Mathf.Tau * i / n;          // angle from +Z, round through +X (segment 20 is the way in)
			Vector3 dir = new(Mathf.Sin(a), 0, Mathf.Cos(a));
			float yaw = a;
			bool door = Mathf.Abs(Mathf.AngleDifference(a, Mathf.Pi)) < 0.1f;   // the way in, on -Z
			k.Color = Colors.White * (0.93f + 0.07f * _rng.Randf());
			if (door)
			{
				Seg(k, dir * (Radius + 0.3f) + Vector3.Up * (2.5f + Height) * 0.5f, new Vector3(segW, Height - 2.5f, 0.6f), yaw, true);
				continue;
			}
			Seg(k, dir * (Radius + 0.3f) + Vector3.Up * Height * 0.5f, new Vector3(segW, Height, 0.6f), yaw, true);
		}
		// string courses round the drum
		foreach (float y in new[] { 0.3f, 14f, 24f })
			for (int i = 0; i < n; i++)
			{
				float a = Mathf.Tau * i / n;
				if (y < 1f && Mathf.Abs(Mathf.AngleDifference(a, Mathf.Pi)) < 0.1f) continue;
				Vector3 dir = new(Mathf.Sin(a), 0, Mathf.Cos(a));
				k.Color = Colors.White * 0.85f;
				Seg(k, dir * (Radius - 0.05f) + Vector3.Up * y, new Vector3(segW, y < 1f ? 0.6f : 0.25f, 0.2f), a, false);
			}
		k.CommitTo(this, "Walls", true);
		// the floor: flagstones in rings
		var f = new MeshKit();
		f.Mat(_stone);
		f.Color = new Color(0.75f, 0.72f, 0.66f);
		f.Cylinder(new Vector3(0, -0.2f, 0), new Vector3(0, 0, 0), Radius + 0.1f, Radius + 0.1f, 48, true);
		f.CommitTo(this, "Floor", true);
		_body.AddChild(new CollisionShape3D { Position = new Vector3(0, -0.1f, 0), Shape = new CylinderShape3D { Radius = Radius + 0.2f, Height = 0.2f } });
		var joints = new MeshKit();
		joints.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.33f, 0.3f), Roughness = 1f });
		foreach (float r in new[] { 3.6f, 4.9f, 6.2f })
			for (int i = 0; i < 64; i++)
			{
				float a0 = Mathf.Tau * i / 64f, a1 = Mathf.Tau * (i + 1) / 64f;
				joints.Beam(new Vector3(Mathf.Sin(a0) * r, 0.003f, Mathf.Cos(a0) * r), new Vector3(Mathf.Sin(a1) * r, 0.003f, Mathf.Cos(a1) * r), 0.03f, 0.004f);
			}
		joints.CommitTo(this, "Joints", false);
		// the passage's end into the room
		var p = new MeshKit();
		p.Mat(_stone);
		Seg(p, new Vector3(-0.85f, 1.25f, -Radius - 0.1f), new Vector3(0.3f, 2.5f, 1.0f), 0, true);
		Seg(p, new Vector3(0.85f, 1.25f, -Radius - 0.1f), new Vector3(0.3f, 2.5f, 1.0f), 0, true);
		p.CommitTo(this, "Entry", true);
		// the ceiling: a stone ring with the shaft through it, and the shaft up to the room above
		var c = new MeshKit();
		c.Mat(new StandardMaterial3D { AlbedoTexture = StairwellTextures.CleanConcrete, AlbedoColor = new Color(0.7f, 0.67f, 0.6f), Roughness = 0.9f, VertexColorUseAsAlbedo = true, Uv1Triplanar = true, Uv1WorldTriplanar = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled });
		for (int i = 0; i < 48; i++)
		{
			float a0 = Mathf.Tau * i / 48f, a1 = Mathf.Tau * (i + 1) / 48f;
			Vector3 d0 = new(Mathf.Sin(a0), 0, Mathf.Cos(a0)), d1 = new(Mathf.Sin(a1), 0, Mathf.Cos(a1));
			Vector3 y = Vector3.Up * Height;
			c.Quad(y + d0 * ShaftR, y + d1 * ShaftR, y + d1 * (Radius + 0.4f), y + d0 * (Radius + 0.4f), Vector3.Down);
			// the shaft's inner face
			Vector3 top = Vector3.Up * (Rise + DaisTop);
			c.Quad(y + d0 * ShaftR, top + d0 * ShaftR, top + d1 * ShaftR, y + d1 * ShaftR, -(d0 + d1).Normalized());
		}
		c.CommitTo(this, "Ceiling", false);
		// ribs up into the dark vault
		var ribs = new MeshKit();
		ribs.Mat(_stone);
		ribs.Color = Colors.White * 0.8f;
		for (int i = 0; i < 8; i++)
		{
			float a = Mathf.Tau * i / 8f;
			Vector3 d = new(Mathf.Sin(a), 0, Mathf.Cos(a));
			ribs.Beam(d * (Radius - 0.1f) + Vector3.Up * (Height - 4f), d * (ShaftR + 0.2f) + Vector3.Up * (Height - 0.1f), 0.35f, 0.4f);
		}
		ribs.CommitTo(this, "Ribs", false);
	}

	/// <summary>Eight long arched windows, floor to far overhead, each bricked up from inside to the top of its arch.</summary>
	private void BuildWindows()
	{
		var frame = new MeshKit();
		frame.Mat(_stone);
		var brick = new MeshKit();
		brick.Mat(StationTextures.BrickMat);
		const float w = 1.5f, y0 = 2.2f, y1 = 16f;
		for (int i = 0; i < 8; i++)
		{
			float a = Mathf.Tau * (i + 0.5f) / 8f;
			Vector3 d = new(Mathf.Sin(a), 0, Mathf.Cos(a));
			Vector3 side = new(Mathf.Cos(a), 0, -Mathf.Sin(a));
			Vector3 face = d * (Radius - 0.02f);
			// the bricks: a rectangle and its round arch head, set a little back
			brick.Color = new Color(0.82f, 0.72f, 0.66f) * (0.9f + 0.15f * _rng.Randf());
			Vector3 inset = -d * 0.01f;   // just proud of the wall's face; the frame stands out further
			brick.Quad(face + inset - side * w * 0.5f + Vector3.Up * y0, face + inset + side * w * 0.5f + Vector3.Up * y0,
				face + inset + side * w * 0.5f + Vector3.Up * y1, face + inset - side * w * 0.5f + Vector3.Up * y1, -d,
				new Vector2(0, y0), new Vector2(w, y0), new Vector2(w, y1), new Vector2(0, y1));
			const int arcN = 12;
			for (int j = 0; j < arcN; j++)
			{
				float t0 = Mathf.Pi * j / arcN, t1 = Mathf.Pi * (j + 1) / arcN;
				Vector3 p0 = face + inset + Vector3.Up * y1 + (side * Mathf.Cos(t0) + Vector3.Up * Mathf.Sin(t0)) * w * 0.5f;
				Vector3 p1 = face + inset + Vector3.Up * y1 + (side * Mathf.Cos(t1) + Vector3.Up * Mathf.Sin(t1)) * w * 0.5f;
				Vector3 cc = face + inset + Vector3.Up * y1;
				brick.Tri(cc, p0, p1, -d, new Vector2(w * 0.5f, y1), new Vector2(w * 0.5f + Mathf.Cos(t0) * w * 0.5f, y1 + Mathf.Sin(t0) * w * 0.5f), new Vector2(w * 0.5f + Mathf.Cos(t1) * w * 0.5f, y1 + Mathf.Sin(t1) * w * 0.5f));
				// the stone of the arch round it
				Vector3 q0 = face + Vector3.Up * y1 + (side * Mathf.Cos(t0) + Vector3.Up * Mathf.Sin(t0)) * (w * 0.5f + 0.18f);
				Vector3 q1 = face + Vector3.Up * y1 + (side * Mathf.Cos(t1) + Vector3.Up * Mathf.Sin(t1)) * (w * 0.5f + 0.18f);
				frame.Color = Colors.White * 0.95f;
				frame.Beam((p0 - inset).Lerp(q0, 0.5f), (p1 - inset).Lerp(q1, 0.5f), 0.24f, 0.2f, 1f, d);
			}
			// jambs and a deep sill
			frame.Color = Colors.White * 0.95f;
			Seg(frame, face - side * (w * 0.5f + 0.09f) + Vector3.Up * (y0 + y1) * 0.5f, new Vector3(0.2f, y1 - y0, 0.26f), a, false);
			Seg(frame, face + side * (w * 0.5f + 0.09f) + Vector3.Up * (y0 + y1) * 0.5f, new Vector3(0.2f, y1 - y0, 0.26f), a, false);
			Seg(frame, face - d * 0.1f + Vector3.Up * (y0 - 0.08f), new Vector3(w + 0.5f, 0.16f, 0.45f), a, false);
			// mullion bars in front of the bricks, as if there were glass once
			frame.Color = new Color(0.25f, 0.24f, 0.22f);
			frame.Beam(face - d * 0.07f + Vector3.Up * y0, face - d * 0.07f + Vector3.Up * (y1 + w * 0.5f), 0.05f, 0.05f, 1f, d);
			for (float y = y0 + 2.2f; y < y1; y += 2.2f)
				frame.Beam(face - d * 0.07f - side * w * 0.5f + Vector3.Up * y, face - d * 0.07f + side * w * 0.5f + Vector3.Up * y, 0.04f, 0.04f, 1f, d);
		}
		frame.CommitTo(this, "WindowFrames", true);
		var mi = brick.CommitTo(this, "Bricked", false);
		if (mi.Mesh.SurfaceGetMaterial(0) is StandardMaterial3D bm)
		{
			var dup = (StandardMaterial3D)bm.Duplicate();
			dup.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			dup.VertexColorUseAsAlbedo = true;
			mi.MaterialOverride = dup;
		}
	}

	/// <summary>Candle sconces between the windows, and a few higher up to pass on the way up.</summary>
	private void BuildSconces()
	{
		var k = new MeshKit();
		k.Mat(ItemTextures.BrassMat);
		var wax = new MeshKit();
		wax.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.88f, 0.78f), Roughness = 0.7f });
		var flameMat = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.8f, 0.45f), EmissionEnabled = true, Emission = new Color(1f, 0.65f, 0.3f), EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
		foreach (var (y, count, energy, shadows) in new[] { (2.3f, 8, 1.1f, false), (13f, 4, 1.6f, false), (23f, 4, 1.6f, false), (30f, 4, 1.4f, false) })
			for (int i = 0; i < count; i++)
			{
				float a = Mathf.Tau * i / count + (count == 4 ? Mathf.Pi * 0.25f * (y > 20f ? 1f : 0f) : 0f);
				if (y < 5f && Mathf.Abs(Mathf.AngleDifference(a, Mathf.Pi)) < 0.2f) continue;   // not over the way in
				Vector3 d = new(Mathf.Sin(a), 0, Mathf.Cos(a));
				Vector3 at = d * (Radius - 0.25f) + Vector3.Up * y;
				k.Color = new Color(0.7f, 0.55f, 0.32f);
				k.Beam(d * (Radius - 0.02f) + Vector3.Up * (y - 0.15f), at, 0.04f, 0.04f);
				k.Cylinder(at, at + Vector3.Up * 0.04f, 0.09f, 0.08f, 10, true);
				wax.Cylinder(at + Vector3.Up * 0.04f, at + Vector3.Up * 0.22f, 0.025f, 0.025f, 8, true);
				AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.018f, Height = 0.05f, RadialSegments = 6, Rings = 3 }, Position = at + Vector3.Up * 0.25f, MaterialOverride = flameMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
				AddChild(new OmniLight3D { Position = at + Vector3.Up * 0.3f - d * 0.1f, LightColor = new Color(1f, 0.72f, 0.42f), LightEnergy = energy, OmniRange = 7.5f, OmniAttenuation = 1.3f, ShadowEnabled = shadows });
			}
		k.CommitTo(this, "Sconces", false);
		// the drum's warm fill, so the bricked windows and the height read (soft, shadowless)
		AddChild(new OmniLight3D { Name = "Fill", Position = new Vector3(0, 7f, 0), LightColor = new Color(1f, 0.8f, 0.58f), LightEnergy = 0.9f, OmniRange = 16f, OmniAttenuation = 0.8f, ShadowEnabled = false });
		AddChild(new OmniLight3D { Name = "FillHigh", Position = new Vector3(0, 21f, 0), LightColor = new Color(1f, 0.8f, 0.58f), LightEnergy = 0.7f, OmniRange = 15f, OmniAttenuation = 0.8f, ShadowEnabled = false });
		wax.CommitTo(this, "Candles", false);
	}

	/// <summary>The dais: a low round stone step, sloped at its foot, with iron posts round its rim. It
	/// moves (an AnimatableBody) and carries the player up.</summary>
	private void BuildDais()
	{
		Dais = new Node3D { Name = "Dais" };
		AddChild(Dais);
		var k = new MeshKit();
		k.Mat(_stone);
		k.Color = new Color(0.85f, 0.82f, 0.75f);
		k.Cylinder(new Vector3(0, 0, 0), new Vector3(0, DaisTop, 0), DaisFoot, DaisR, 48, true);
		k.Cylinder(new Vector3(0, -4f, 0), new Vector3(0, 0.001f, 0), DaisFoot, DaisFoot, 48, false);   // the column under it, seen on the way up
		k.CommitTo(Dais, "Stone", true);
		// a ring of inlay and a worn sigil in the middle
		var inlay = new MeshKit();
		inlay.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.4f, 0.25f), Roughness = 0.5f, MetallicSpecular = 0.6f, Metallic = 0.5f });
		for (int i = 0; i < 48; i++)
		{
			float a0 = Mathf.Tau * i / 48f, a1 = Mathf.Tau * (i + 1) / 48f;
			foreach (float r in new[] { 1.75f, 0.7f })
				inlay.Beam(new Vector3(Mathf.Sin(a0) * r, DaisTop + 0.003f, Mathf.Cos(a0) * r), new Vector3(Mathf.Sin(a1) * r, DaisTop + 0.003f, Mathf.Cos(a1) * r), 0.05f, 0.006f);
		}
		for (int i = 0; i < 8; i++)
		{
			float a = Mathf.Tau * i / 8f;
			inlay.Beam(new Vector3(Mathf.Sin(a) * 0.7f, DaisTop + 0.003f, Mathf.Cos(a) * 0.7f), new Vector3(Mathf.Sin(a) * 1.75f, DaisTop + 0.003f, Mathf.Cos(a) * 1.75f), 0.04f, 0.006f);
		}
		inlay.CommitTo(Dais, "Inlay", false);
		// iron posts round the rim (the web's anchors)
		var posts = new MeshKit();
		posts.Mat(StairwellTextures.SteelMat);
		posts.Color = new Color(0.25f, 0.24f, 0.23f);
		for (int i = 0; i < 8; i++)
		{
			float a = Mathf.Tau * (i + 0.5f) / 8f;
			Vector3 at = new(Mathf.Sin(a) * (DaisR - 0.12f), DaisTop, Mathf.Cos(a) * (DaisR - 0.12f));
			posts.Cylinder(at, at + Vector3.Up * 1.05f, 0.035f, 0.03f, 8, true);
			posts.Blob(at + Vector3.Up * 1.08f, Vector3.One * 0.05f, i, 0.05f);
		}
		posts.CommitTo(Dais, "Posts", true);
		_daisBody = new AnimatableBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0, SyncToPhysics = false };
		_daisBody.SetMeta("surface", "stone");
		var pts = new List<Vector3>();
		for (int i = 0; i < 32; i++)
		{
			float a = Mathf.Tau * i / 32f;
			pts.Add(new Vector3(Mathf.Sin(a) * DaisFoot, 0, Mathf.Cos(a) * DaisFoot));
			pts.Add(new Vector3(Mathf.Sin(a) * DaisR, DaisTop, Mathf.Cos(a) * DaisR));
			pts.Add(new Vector3(Mathf.Sin(a) * DaisFoot, -0.4f, Mathf.Cos(a) * DaisFoot));
		}
		_daisBody.AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = pts.ToArray() } });
		Dais.AddChild(_daisBody);
	}

	/// <summary>Old web over the whole dais: sheets slung between the posts, a tent of it drawn up to a
	/// point over the middle, and a grey mat of it on the stone. It can't be pushed through.</summary>
	private void BuildWebs()
	{
		_webs = new Node3D { Name = "Webs" };
		Dais.AddChild(_webs);
		var k = new MeshKit();
		k.Mat(StationTextures.WebMat);
		k.Color = Colors.White;
		var anchors = new List<Vector3>();
		for (int i = 0; i < 8; i++)
		{
			float a = Mathf.Tau * (i + 0.5f) / 8f;
			anchors.Add(new Vector3(Mathf.Sin(a) * (DaisR - 0.12f), DaisTop + 1.05f, Mathf.Cos(a) * (DaisR - 0.12f)));
		}
		Vector3 peak = new(0, DaisTop + 2.4f, 0);
		for (int i = 0; i < 8; i++)
		{
			Vector3 a = anchors[i], b = anchors[(i + 1) % 8];
			Vector3 fa = a with { Y = DaisTop + 0.02f }, fb = b with { Y = DaisTop + 0.02f };
			Vector3 outward = (((a + b) * 0.5f) with { Y = 0 }).Normalized();
			// the side sheet, sagging, and the tent up to the peak
			Vector3 sag = (a + b) * 0.5f + Vector3.Down * 0.25f + outward * 0.1f;
			k.Card(fa, fb, b, a, outward, new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0));
			k.Card(a, sag, peak, peak, outward, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
			k.Card(sag, b, peak, peak, outward, new Vector2(0.5f, 1), new Vector2(1, 1), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
			// strands off to the floor
			Vector3 foot = outward * (DaisFoot + 0.4f + _rng.Randf() * 0.5f) + Vector3.Up * 0.01f;
			k.Card(a, a + Vector3.Up * 0.05f, foot + Vector3.Up * 0.05f, foot, outward.Cross(Vector3.Up), new Vector2(0, 0), new Vector2(0, 0.1f), new Vector2(1, 0.1f), new Vector2(1, 0));
		}
		// the mat over the top
		k.Card(new Vector3(-1.6f, DaisTop + 0.01f, -1.6f), new Vector3(-1.6f, DaisTop + 0.01f, 1.6f), new Vector3(1.6f, DaisTop + 0.01f, 1.6f), new Vector3(1.6f, DaisTop + 0.01f, -1.6f), Vector3.Up,
			new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
		_webMeshes.Add(k.CommitTo(_webs, "Web", false));
		_barrier = new StaticBody3D { Name = "WebBarrier", CollisionLayer = 1, CollisionMask = 0 };
		_barrier.AddChild(new CollisionShape3D { Position = new Vector3(0, 1.2f, 0), Shape = new CylinderShape3D { Radius = DaisFoot - 0.1f, Height = 2.4f } });
		AddChild(_barrier);
		WebUse = new PickupInteractable
		{
			Name = "WebUse", PickRadius = DaisFoot, MaxDistance = 3.5f, Position = new Vector3(0, 1.0f, 0),
			PromptFor = p => p?.Inventory is { } inv && inv.HasTool(ToolKind.Lighter) ? "Burn the webs" : "Thick with old web. It won't tear.",
		};
		WebUse.Interacted += OnWebs;
		AddChild(WebUse);
	}

	/// <summary>The little room at the top: round, low, stone, a lamp; a way on (the demo ends here).</summary>
	private void BuildTopRoom()
	{
		float y0 = Rise + DaisTop;
		var k = new MeshKit();
		k.Mat(_stone);
		const int n = 28;
		float segW = Mathf.Tau * (TopR + 0.3f) / n + 0.05f;
		for (int i = 0; i < n; i++)
		{
			float a = Mathf.Tau * i / n;
			Vector3 dir = new(Mathf.Sin(a), 0, Mathf.Cos(a));
			bool way = Mathf.Abs(Mathf.AngleDifference(a, 0f)) < 0.15f;   // an arch on +Z, the way on
			if (way) { Seg(k, dir * (TopR + 0.3f) + Vector3.Up * (y0 + (2.4f + TopH + 0.2f) * 0.5f), new Vector3(segW, TopH + 0.2f - 2.4f, 0.6f), a, true); continue; }
			Seg(k, dir * (TopR + 0.3f) + Vector3.Up * (y0 + TopH * 0.5f), new Vector3(segW, TopH + 0.4f, 0.6f), a, true);
		}
		k.Color = Colors.White * 0.9f;
		k.Cylinder(new Vector3(0, y0 + TopH, 0), new Vector3(0, y0 + TopH + 0.3f, 0), TopR + 0.6f, TopR + 0.6f, 32, true);
		// the floor ring round the shaft
		for (int i = 0; i < 48; i++)
		{
			float a0 = Mathf.Tau * i / 48f, a1 = Mathf.Tau * (i + 1) / 48f;
			Vector3 d0 = new(Mathf.Sin(a0), 0, Mathf.Cos(a0)), d1 = new(Mathf.Sin(a1), 0, Mathf.Cos(a1));
			Vector3 y = Vector3.Up * y0;
			k.Color = new Color(0.72f, 0.69f, 0.63f);
			k.Quad(y + d0 * (DaisR + 0.02f), y + d0 * (TopR + 0.4f), y + d1 * (TopR + 0.4f), y + d1 * (DaisR + 0.02f), Vector3.Up);
		}
		k.CommitTo(this, "TopRoom", true);
		// the way on: a dark stone stair going up, out of sight
		var st = new MeshKit();
		st.Mat(_stone);
		for (int s = 0; s < 10; s++)
			Seg(st, new Vector3(0, y0 + 0.09f * (s + 1), TopR + 0.5f + s * 0.28f), new Vector3(1.2f, 0.18f * (s + 1), 0.28f), 0, false);
		// its narrow stone throat, climbing out of the light
		Seg(st, new Vector3(-0.75f, y0 + 2.6f, TopR + 1.9f), new Vector3(0.3f, 5.2f, 3.2f), 0, false);
		Seg(st, new Vector3(0.75f, y0 + 2.6f, TopR + 1.9f), new Vector3(0.3f, 5.2f, 3.2f), 0, false);
		Seg(st, new Vector3(0, y0 + 5.1f, TopR + 1.9f), new Vector3(1.8f, 0.3f, 3.2f), 0, false);
		Seg(st, new Vector3(0, y0 + 2.6f, TopR + 3.5f), new Vector3(1.8f, 5.2f, 0.3f), 0, false);
		st.CommitTo(this, "Stair", true);
		_body.AddChild(new CollisionShape3D { Position = new Vector3(0, y0 + 1.2f, TopR + 0.5f), Shape = new BoxShape3D { Size = new Vector3(1.4f, 2.4f, 0.4f) } });
		AddChild(new OmniLight3D { Position = new Vector3(0, y0 + 2.8f, 0), LightColor = new Color(1f, 0.85f, 0.65f), LightEnergy = 1.3f, OmniRange = 8f, ShadowEnabled = true });
		AddChild(new OmniLight3D { Position = new Vector3(0, y0 + 2.2f, TopR + 2.5f), LightColor = new Color(0.75f, 0.8f, 0.95f), LightEnergy = 0.5f, OmniRange = 4f });
		// the floor's collision (a box round the shaft's top; switched on once the dais is up, so it
		// doesn't stop the dais on its way)
		_topFloor = new StaticBody3D { Name = "TopFloor", CollisionLayer = 1, CollisionMask = 0 };
		foreach (var (c, s) in new[]
		{
			(new Vector3(0, y0 - 0.25f, (DaisR + TopR + 0.4f) * 0.5f), new Vector3(TopR * 2f + 0.8f, 0.5f, TopR + 0.4f - DaisR)),
			(new Vector3(0, y0 - 0.25f, -(DaisR + TopR + 0.4f) * 0.5f), new Vector3(TopR * 2f + 0.8f, 0.5f, TopR + 0.4f - DaisR)),
			(new Vector3((DaisR + TopR + 0.4f) * 0.5f, y0 - 0.25f, 0), new Vector3(TopR + 0.4f - DaisR, 0.5f, DaisR * 2f)),
			(new Vector3(-(DaisR + TopR + 0.4f) * 0.5f, y0 - 0.25f, 0), new Vector3(TopR + 0.4f - DaisR, 0.5f, DaisR * 2f)),
		})
			_topFloor.AddChild(new CollisionShape3D { Position = c, Shape = new BoxShape3D { Size = s } });
		AddChild(_topFloor);
		SetTopFloor(false);
	}

	private void SetTopFloor(bool on)
	{
		foreach (var ch in _topFloor.GetChildren()) if (ch is CollisionShape3D cs) cs.Disabled = !on;
	}

	// ------------------------------------------------------------------ the beats

	private void OnEnter(PlayerController player)
	{
		if (_checkpointed || player == null) return;
		if (StoryManager.Instance is { } s && s.Current >= Checkpoint.Act19Finished) return;
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act19Finished);
		GD.Print("[story] Act 19 done: through the bookcase - Act 20, the round room");
		EnsureLighter(player);
	}

	/// <summary>The lighter is never used up, so the player has had it since the station. An old save
	/// without it would be stuck at the web: a spare waits on the sill by the way in, just in case.</summary>
	public void EnsureLighter(PlayerController player)
	{
		if (WebsBurned || player?.Inventory is not { } inv || inv.HasTool(ToolKind.Lighter) || HasNode("SpareLighter")) return;
		float a = Mathf.Pi + Mathf.Pi / 8f;
		Vector3 d = new(Mathf.Sin(a), 0, Mathf.Cos(a));
		AddChild(new Pickup
		{
			Name = "SpareLighter", Kind = ToolKind.Lighter, UseSpot = false, SnapToSurface = false, TakenId = "round_room_lighter",
			TakenLine = "A lighter someone left on the sill.", Position = d * (Radius - 0.25f) + Vector3.Up * 2.22f, Rotation = new Vector3(0, a, 0),
		});
	}

	private void OnWebs(PlayerController player)
	{
		if (WebsBurned || player?.Inventory is not { } inv || !inv.HasTool(ToolKind.Lighter)) return;
		WebsBurned = true;
		WebUse.Enabled = false;
		_ = Cutscene.Run(this, ct => BurnWebs(player, ct), lockInput: true);
	}

	private async Task BurnWebs(PlayerController player, CancellationToken ct)
	{
		Vector3 near = ToLocal(player.GlobalPosition) with { Y = 0 };
		Vector3 spot = near.LengthSquared() > 0.01f ? near.Normalized() * (DaisFoot - 0.3f) + Vector3.Up * 0.9f : new Vector3(0, 0.9f, -DaisFoot);
		Sfx("lighter_flick", ToGlobal(spot), 0f, 1f);
		await Cutscene.Wait(this, 0.6, ct);
		// it catches where the flame touches, and runs round both ways and up the tent
		var fires = new List<(FireVfx fx, float delay)>();
		float a0 = Mathf.Atan2(spot.X, spot.Z);
		for (int i = 0; i < 11; i++)
		{
			// round the rim both ways from where it caught, then up the tent to the peak
			float off = i == 0 ? 0f : ((i + 1) / 2) * (Mathf.Pi / 4.5f) * (i % 2 == 0 ? 1f : -1f);
			float a = a0 + off;
			bool tent = i >= 9;
			Vector3 at = tent ? new Vector3(Mathf.Sin(a0 + i) * 0.5f, DaisTop + 1.3f + (i - 9) * 0.5f, Mathf.Cos(a0 + i) * 0.5f)
				: new Vector3(Mathf.Sin(a) * (DaisR - 0.15f), DaisTop + 0.35f, Mathf.Cos(a) * (DaisR - 0.15f));
			var fx = new FireVfx { Name = $"WebFire{i}", Extent = new Vector3(0.45f, 0.75f, 0.45f), FlameScale = 0.32f, Smoke = i % 3 == 0, SmokeAmount = 0.35f, LightRange = 8f, LightEnergy = 2.2f, LightShadows = false, EmitLight = i == 0 || i == 9, Seed = 30 + i, Position = at };
			AddChild(fx);
			fx.Intensity = 0f;
			fires.Add((fx, tent ? 1.0f + (i - 9) * 0.5f : Mathf.Abs(off) * 0.55f));
		}
		Sfx("web_burn", ToGlobal(spot), 3f, 0.9f);
		double t = 0;
		const double seconds = 4.2;
		while (t < seconds)
		{
			await Cutscene.Frame(this, ct);
			t += GetProcessDeltaTime();
			foreach (var (fx, delay) in fires)
			{
				float u = Mathf.Clamp(((float)t - delay) / 2.4f, 0f, 1f);
				fx.Intensity = Mathf.Sin(u * Mathf.Pi) * 0.9f;
			}
			float w = Mathf.Clamp((float)(t / seconds), 0f, 1f);
			foreach (var g in _webMeshes) g.Transparency = Mathf.Clamp(w * 1.25f, 0f, 1f);
			_webs.Scale = new Vector3(1f - 0.25f * w, 1f - 0.7f * w, 1f - 0.25f * w);
		}
		foreach (var (fx, _) in fires) fx.QueueFree();
		ClearWebs();
		StoryManager.Instance?.SetFlag(StoryManager.Flag.RoundRoomWebBurned);
		await Cutscene.Wait(this, 0.4, ct);
		GD.Print("[story] Act 20: the webs burn away - the dais is clear");
	}

	private void ClearWebs()
	{
		WebsBurned = true;
		_webs.Visible = false;
		if (WebUse != null) WebUse.Enabled = false;
		if (_barrier != null) { _barrier.QueueFree(); _barrier = null; }
	}

	private void OnCentre(PlayerController player)
	{
		if (!WebsBurned || Rising || Arrived || player == null) return;
		Rising = true;
		_ = Cutscene.Run(this, ct => Ascend(player, ct), lockInput: true, freezeBody: true);
	}

	/// <summary>Where the dais is on its way up: 0 on the floor, 1 at the top.</summary>
	private void PlaceDais(float u) => Dais.Position = new Vector3(0, Rise * u, 0);

	/// <summary>Twenty seconds up: a grinding start, a slow smooth climb past the bricked windows and the
	/// candles, up the shaft into the room above, a settling stop. Gentle: no shake, slow looks only.</summary>
	private async Task Ascend(PlayerController player, CancellationToken ct)
	{
		GD.Print("[story] Act 20: the dais begins to rise");
		// in the middle, facing the way the room above opens
		Vector3 startLocal = ToLocal(player.GlobalPosition);
		Sfx("lift_start", ToGlobal(Vector3.Up * DaisTop), 0f, 1f);
		var loop = LoopPlayer("lift_loop");
		double t = 0;
		const float settle = 1.4f, lead = 1.2f;
		float climb = AscentSeconds - settle - lead;
		Vector3 offset = startLocal - Dais.Position;
		while (t < AscentSeconds)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			float u = Mathf.Clamp(((float)t - lead) / climb, 0f, 1f);
			float eased = u * u * (3f - 2f * u);            // smoothstep: eases away and eases in
			PlaceDais(eased);
			// the player rides it, slowly drawn to the middle
			var at = offset.Lerp(new Vector3(0, offset.Y, 0), Mathf.Min(1f, (float)t / 4f));
			player.GlobalPosition = ToGlobal(Dais.Position + at);
			if (loop != null)
			{
				loop.GlobalPosition = ToGlobal(Dais.Position + Vector3.Up * 0.5f);
				float speed = 6f * u * (1f - u);
				loop.VolumeDb = Mathf.Lerp(-30f, -6f, Mathf.Clamp(speed * 1.2f + ((float)t > lead ? 0.3f : 0f), 0f, 1f));
				loop.PitchScale = 0.85f + 0.2f * speed;
			}
			// a slow look up the drum to the shaft as it starts, then level again for the room above
			var rig = player.CameraRig;
			float wantPitch = (float)t < 11f ? 0.55f : 0f;
			rig.SetPitch(Mathf.Lerp(rig.Pitch, wantPitch, Mathf.Min(1f, dt * 0.5f)));
			if ((float)t > 12f)
			{
				Vector3 to = ToGlobal(new Vector3(0, 0, TopR)) - rig.Camera.GlobalPosition;
				player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.AngleDifference(rig.Yaw, Mathf.Atan2(-to.X, -to.Z)) * Mathf.Min(1f, dt * 0.8f), 0));
			}
		}
		PlaceDais(1f);
		player.GlobalPosition = ToGlobal(Dais.Position + new Vector3(0, offset.Y, 0));
		loop?.Stop();
		loop?.QueueFree();
		Sfx("lift_stop", ToGlobal(Dais.Position + Vector3.Up * 0.3f), 0f, 1f);
		SetTopFloor(true);
		Arrived = true;
		Rising = false;
		_checkpointed = true;
		player.Velocity = Vector3.Zero;
		StoryBeat.ReachCheckpoint(player, Checkpoint.Act20Finished);
		GD.Print("[story] Act 20 done: up through the ceiling into the room above - the end of the demo");
		_ = Cutscene.Run(this, async ct2 =>
		{
			await Cutscene.Wait(this, 1.5, ct2);
			await StoryBeat.Caption(this, "Up. Always up, from here.", 0.8f, 2.6f, 1.2f, ct2);
			await Cutscene.Wait(this, 1.0, ct2);
			var fader = StoryBeat.Fader(this);
			if (fader != null) await fader.Fade(1f, 2.5f, ct2);
			if (GetTree().GetFirstNodeInGroup("act11_ending") is Act11Ending ending) await ending.Credits(fader, ct2);
		});
	}

	// ------------------------------------------------------------------ sound

	private AudioStreamPlayer3D LoopPlayer(string name)
	{
		string path = $"res://assets/audio/sfx/{name}.wav";
		if (!ResourceLoader.Exists(path)) return null;
		var stream = GD.Load<AudioStream>(path);
		if (stream is AudioStreamWav w) { w = (AudioStreamWav)w.Duplicate(); w.LoopMode = AudioStreamWav.LoopModeEnum.Forward; w.LoopEnd = Mathf.RoundToInt(w.GetLength() * w.MixRate); stream = w; }
		var p = new AudioStreamPlayer3D { Stream = stream, Bus = "Events", VolumeDb = -30f, UnitSize = 6f, MaxDistance = 40f };
		AddChild(p);
		p.Play();
		return p;
	}

	private void Sfx(string name, Vector3 at, float db, float pitch)
	{
		string path = $"res://assets/audio/sfx/{name}_01.wav";
		if (!ResourceLoader.Exists(path)) path = $"res://assets/audio/sfx/{name}.wav";
		if (!ResourceLoader.Exists(path)) return;
		var s = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = db, PitchScale = pitch, UnitSize = 5f, MaxDistance = 40f };
		AddChild(s);
		s.GlobalPosition = at;
		s.Finished += s.QueueFree;
		s.Play();
	}
}
