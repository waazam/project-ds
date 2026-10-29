using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// Act 1's wayfinding (2026-10-23, the owner, lost past the footbridge on the first walk: "i want the player to intuitively
/// walk the path"). No compass in Act 1, so the woods themselves show the way, as a real park's trails do:
/// <list type="bullet">
/// <item><b>Blazes.</b> Painted marks on the trees beside each way, every ~14 m, a colour to a trail (the Blackfern trail
/// white, the Overlook Loop yellow, the Falls Path blue), shown only on the ways open on this walk. Two stacked where a
/// walk turns off the trail (the old trail-crew sign for a turn).</item>
/// <item><b>A landmark.</b> A timber viewing platform on the overlook's hill, its mast and pennant above the brush.</item>
/// <item><b>Signs.</b> One just past the footbridge pointing up to the Overlook Loop; the fork's own sign (the scene's).</item>
/// <item><b>A soft pull back.</b> Wandered well off every open way for a while: a bird calls from the way, ahead of them;
/// still off it, a thought, once a walk: "The trail's back that way."</item>
/// </list>
/// (The path itself reads more plainly too: the ferns kept back from its edges, ForestScatter; less leaf over the dirt,
/// terrain.gdshader.)
/// </summary>
public partial class Act1Walks
{
	private static readonly Color BlazeWhite = new(0.93f, 0.91f, 0.86f), BlazeYellow = new(0.96f, 0.78f, 0.12f), BlazeBlue = new(0.18f, 0.42f, 0.92f);
	/// <summary>Metres between blazes along a way.</summary>
	[Export] public float BlazeEvery = 14f;
	/// <summary>Off every open way by this much (m) for <see cref="StrayAfter"/> s: the pull back begins.</summary>
	[Export] public float StrayDistance = 9f, StrayAfter = 9f;

	private readonly List<(Node3D node, int stages)> _blazes = new();
	private bool _blazed;
	private double _nextBlazeTry;
	private float _strayT;
	private double _nextStrayCall;
	private int _thoughtWalk;
	private Texture2D _blazeTex;

	/// <summary>For tests: blazes painted, and the pull back's calls and thought so far.</summary>
	public int BlazeCount => _blazes.Count;
	public int StrayCalls { get; private set; }
	public bool StrayThought { get; private set; }
	public Node3D Platform { get; private set; }

	/// <summary>Walks (bit 1, 2, 4 = walk 1, 2, 3) a way's blazes show on, and their colour.</summary>
	private static (int stages, Color c)? BlazeFor(Way w) => w switch
	{
		Way.Back or Way.Mid => (1 | 2 | 4, BlazeWhite),
		Way.North => (4, BlazeWhite),
		Way.Spur or Way.Ridge => (1 | 2, BlazeYellow),
		Way.FallsA or Way.FallsB => (1 | 2, BlazeBlue),
		_ => null,
	};

	/// <summary>Once the forest's trees are in (they're scattered after the level readies): the blazes, the platform, the sign.</summary>
	private void UpdateWayfinding(PlayerController player, float dt)
	{
		if (!_blazed && _clock >= _nextBlazeTry)
		{
			_nextBlazeTry = _clock + 1.0;
			var trees = TreeTrunks();
			if (trees.Count > 0 || _clock > 20.0)
			{
				_blazed = true;
				PaintBlazes(trees);
				BuildPlatform();
				BridgeSign();
				ShowBlazes(_builtStage < 0 ? Stage : _builtStage);
			}
		}
		if (player != null) StrayPull(player, dt);
	}

	// ------------------------------------------------------------------ blazes

	/// <summary>Every tree's trunk (world xz, and its radius) from the forest's scatter.</summary>
	private List<(Vector2 p, float r)> TreeTrunks()
	{
		var list = new List<(Vector2, float)>();
		var root = GetTree().CurrentScene;
		if (root == null) return list;
		foreach (var inst in root.FindChildren("Instances", "Node3D", true, false))
			foreach (var child in inst.GetChildren())
			{
				if (child is not MultiMeshInstance3D mmi || mmi.Multimesh is not { } mm) continue;
				string n = mmi.Name.ToString();
				if (!(n.StartsWith("fir_") || n.StartsWith("decid"))) continue;
				if (n.StartsWith("fir_young")) continue;   // (saplings: too thin to paint)
				float baseR = n.StartsWith("fir_giant") ? 0.52f : n.StartsWith("fir_tall") ? 0.42f : n.StartsWith("fir_spire") ? 0.34f : n.StartsWith("decid") ? 0.2f : 0.31f;
				for (int i = 0; i < mm.InstanceCount; i++)
				{
					var t = mmi.GlobalTransform * mm.GetInstanceTransform(i);
					list.Add((new Vector2(t.Origin.X, t.Origin.Z), baseR * t.Basis.X.Length()));
				}
			}
		return list;
	}

	private void PaintBlazes(List<(Vector2 p, float r)> trees)
	{
		_blazeTex ??= BlazeTexture();
		int painted = 0, posts = 0;
		for (int w = 0; w < WayCount; w++)
		{
			if (_ways[w] is not { } line || line.Points.Count < 2 || BlazeFor((Way)w) is not { } spec) continue;
			for (float s = 6f; s < line.Length - 3f; s += BlazeEvery)
			{
				var at = line.At(s, out Vector2 dir);
				// the nearest trunk beside the way here, near enough to be seen from it and not standing in it
				int best = -1; float bestD = float.MaxValue;
				for (int i = 0; i < trees.Count; i++)
				{
					float d = trees[i].p.DistanceTo(at);
					if (d < 1.6f + trees[i].r || d > 6f) continue;
					if (d < bestD) { bestD = d; best = i; }
				}
				// two where a walk turns off the trail (its first blaze)
				bool turn = s < 7f && (Way)w is Way.Spur or Way.FallsA;
				Node3D mark = best >= 0 ? Blaze(trees[best].p, trees[best].r, at, spec.c, turn) : BlazePost(at, dir, spec.c, turn);
				if (best >= 0) painted++; else posts++;
				AddChild(mark);
				_blazes.Add((mark, spec.stages));
			}
		}
		GD.Print($"[story] Act 1 wayfinding: {painted} blazes on trees, {posts} on posts");
	}

	/// <summary>A painted blaze on a trunk, on the side facing the way (a decal cast onto the bark).</summary>
	private Node3D Blaze(Vector2 trunk, float r, Vector2 wayPoint, Color c, bool turn)
	{
		var root = new Node3D { Name = "Blaze" };
		Vector2 toWay2 = (wayPoint - trunk).Normalized();
		var n = new Vector3(toWay2.X, 0, toWay2.Y);
		float ground = _terrain.HeightAt(trunk.X, trunk.Y);
		int count = turn ? 2 : 1;
		for (int k = 0; k < count; k++)
		{
			var d = new Decal
			{
				Size = new Vector3(0.2f, 1.4f, 0.36f), TextureAlbedo = _blazeTex, TextureEmission = _blazeTex, Modulate = c,
				EmissionEnergy = 0.35f, NormalFade = 0.5f, UpperFade = 0.1f, LowerFade = 0.1f, CullMask = 1,
				DistanceFadeEnabled = true, DistanceFadeBegin = 60f, DistanceFadeLength = 15f,
			};
			root.AddChild(d);
			// the decal's Y along the outward normal (it casts back down it onto the bark), its Z up the trunk
			Vector3 x = n.Cross(Vector3.Up).Normalized();
			d.GlobalTransform = new Transform3D(new Basis(x, n, Vector3.Up), new Vector3(trunk.X, ground + 1.75f + k * 0.5f, trunk.Y) + n * r * 0.6f);
		}
		return root;
	}

	/// <summary>Where no tree stands by the way: a short marker post, its top painted the trail's colour.</summary>
	private Node3D BlazePost(Vector2 at, Vector2 dir, Color c, bool turn)
	{
		var side = new Vector2(-dir.Y, dir.X) * 2.2f;
		var p = at + side;
		var root = new Node3D { Name = "BlazePost", Position = new Vector3(p.X, _terrain.HeightAt(p.X, p.Y), p.Y) };
		var k = new MeshKit();
		k.Mat(PropTextures.PostMat);
		k.Color = Colors.White;
		k.Box(new Vector3(0, 0.5f, 0), new Vector3(0.11f, 1.1f, 0.11f));
		k.Mat(new StandardMaterial3D { AlbedoColor = c, Roughness = 0.7f, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 0.25f });
		k.Box(new Vector3(0, 0.92f, 0), new Vector3(0.115f, 0.16f, 0.115f));
		if (turn) k.Box(new Vector3(0, 0.7f, 0), new Vector3(0.115f, 0.12f, 0.115f));
		k.CommitTo(root, "Post", true);
		return root;
	}

	/// <summary>A blaze's paint: a brushed oblong, its edges ragged, thinner here and there.</summary>
	private static Texture2D BlazeTexture()
	{
		const int w = 32, h = 64;
		var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		var noise = new FastNoiseLite { Seed = 512, Frequency = 0.18f };
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
				float edge = Mathf.Max(Mathf.Abs(u) * 1.0f, Mathf.Abs(v) * 1.0f) + noise.GetNoise2D(x, y) * 0.12f;
				float a = 1f - Mathf.SmoothStep(0.72f, 0.86f, edge);
				float dry = 0.82f + 0.18f * noise.GetNoise2D(x * 3f + 40f, y * 0.6f);
				img.SetPixel(x, y, new Color(dry, dry, dry, a));
			}
		img.GenerateMipmaps();
		return ImageTexture.CreateFromImage(img);
	}

	private void ShowBlazes(int stage)
	{
		int bit = 1 << (Mathf.Clamp(stage, 1, 3) - 1);
		foreach (var (node, stages) in _blazes) if (IsInstanceValid(node)) node.Visible = (stages & bit) != 0;
		if (Platform != null && IsInstanceValid(Platform)) Platform.Visible = true;
	}

	// ------------------------------------------------------------------ the overlook's platform

	/// <summary>A timber viewing platform at the overlook (the Overlook spur's end): a deck on posts up a ramp, a rail
	/// round the far sides, and a mast with a faded pennant standing over the brush to be seen coming up the hill.</summary>
	private void BuildPlatform()
	{
		if (_spur == null || _spur.Points.Count < 2) return;
		Vector2 end = _spur.At(_spur.Length, out Vector2 dir);
		Vector2 c2 = end + dir * 2.5f;
		float y = _terrain.HeightAt(c2.X, c2.Y);
		var root = new Node3D { Name = "OverlookPlatform", Position = new Vector3(c2.X, y, c2.Y) };
		// facing back down the spur (its steps toward whoever comes up it)
		root.Rotation = new Vector3(0, Mathf.Atan2(-dir.X, -dir.Y), 0);
		AddChild(root);
		Platform = root;
		GD.Print($"[story] Act 1 wayfinding: the overlook's platform at {root.GlobalPosition} (the spur from {_spur.At(0f, out _)} to {end})");
		const float half = 1.8f, deck = 0.75f;
		var k = new MeshKit();
		k.Color = Colors.White;
		k.Mat(PropTextures.PostMat);
		foreach (var (px, pz) in new[] { (-half, -half), (half, -half), (-half, half), (half, half) })
			k.Box(new Vector3(px * 0.92f, (deck + 1.1f) * 0.5f - 0.3f, pz * 0.92f), new Vector3(0.16f, deck + 1.1f + 0.6f, 0.16f));
		k.Mat(PropTextures.DeckMat);
		k.Box(new Vector3(0, deck, 0), new Vector3(half * 2f, 0.1f, half * 2f), 0.6f);
		// (2026-10-29, the owner's friend's playtest: walking up it "clips into the platform's foundation") a ramp up on the
		// near side (+Z: back down the spur) in place of the three steps, which floated or sank with the hill and left the
		// deck's edge to walk into: it runs from the deck's top down to the ground where it lands, however the hill falls
		float top = deck + 0.05f;
		var (rampLen, foot) = RampFit(root, half, top);
		bool ramp = rampLen > 0f;
		if (ramp) RampMesh(k, half, top, rampLen, foot);
		// the skirt: boards round the deck's sides down into the ground, so there is nothing under it to see or catch on
		k.Mat(PropTextures.PostMat);
		float low = Mathf.Min(0f, Mathf.Min(LocalGround(root, -half, -half), Mathf.Min(LocalGround(root, half, -half), Mathf.Min(LocalGround(root, -half, half), LocalGround(root, half, half))))) - 0.25f;
		float sh = top - 0.06f - low;
		foreach (var (px, pz, sx, sz) in new[] { (0f, -half, half * 2f, 0.04f), (-half, 0f, 0.04f, half * 2f), (half, 0f, 0.04f, half * 2f), (0f, half, half * 2f, 0.04f) })
			k.Box(new Vector3(px, low + sh * 0.5f, pz), new Vector3(sx, sh, sz), 0.6f);
		// the rail on the far side and the two ends
		k.Mat(PropTextures.PostMat);
		k.Box(new Vector3(0, deck + 1.0f, -half * 0.92f), new Vector3(half * 2f, 0.08f, 0.08f));
		k.Box(new Vector3(-half * 0.92f, deck + 1.0f, 0), new Vector3(0.08f, 0.08f, half * 2f));
		k.Box(new Vector3(half * 0.92f, deck + 1.0f, 0), new Vector3(0.08f, 0.08f, half * 2f));
		// the mast, and a pennant faded to rust (seen over the brush on the way up)
		k.Cylinder(new Vector3(half * 0.92f, deck, -half * 0.92f), new Vector3(half * 0.92f, deck + 10f, -half * 0.92f), 0.08f, 0.05f, 8);
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.32f, 0.12f), Roughness = 0.9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled });
		Vector3 m = new(half * 0.92f, deck + 9.8f, -half * 0.92f);
		k.Quad(m, m + new Vector3(-1.9f, -0.4f, 0.15f), m + new Vector3(-1.9f, -0.75f, 0.15f), m + new Vector3(0, -1.3f, 0), Vector3.Back);
		k.CommitTo(root, "Mesh", true);
		var body = new StaticBody3D { Name = "Body", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "wood");
		// the deck solid down to the skirt's foot, and the ramp one smooth slab (its top the planks' line, its foot below the
		// ground) running a little under the deck's edge, so the foot meets nothing on the way up
		body.AddChild(new CollisionShape3D { Position = new Vector3(0, (top + low) * 0.5f, 0), Shape = new BoxShape3D { Size = new Vector3(half * 2f, top - low, half * 2f) } });
		if (ramp) body.AddChild(RampCollision(half, top, rampLen, foot));
		foreach (var (px, pz, sx, sz) in new[] { (0f, -half * 0.92f, half * 2f, 0.1f), (-half * 0.92f, 0f, 0.1f, half * 2f), (half * 0.92f, 0f, 0.1f, half * 2f) })
			body.AddChild(new CollisionShape3D { Position = new Vector3(px, deck + 0.55f, pz), Shape = new BoxShape3D { Size = new Vector3(sx, 1.1f, sz) } });
		root.AddChild(body);
	}

	/// <summary>Ground height under the platform's local (x, z), relative to its origin.</summary>
	private float LocalGround(Node3D root, float x, float z)
	{
		var w = root.GlobalTransform * new Vector3(x, 0f, z);
		return _terrain.HeightAt(w.X, w.Z) - root.GlobalPosition.Y;
	}

	/// <summary>The platform's ramp: how long (m, out from the deck's edge at z = half), and the height of the ground where
	/// it lands (local). As gentle as a park's ramp (about 1 in 3.3) whatever the hill does under it; none when the
	/// ground is near the deck's top there.</summary>
	private (float len, float foot) RampFit(Node3D root, float half, float top)
	{
		const float slope = 0.3f;   // rise over run (about 17 degrees)
		float len = 2f, foot = LocalGround(root, 0f, half + len);
		for (int i = 0; i < 4; i++)
		{
			float rise = top - foot;
			if (rise < 0.12f) return (0f, foot);
			len = Mathf.Clamp(rise / slope, 0.6f, 6f);
			foot = LocalGround(root, 0f, half + len);
		}
		return (len, foot);
	}

	private const float RampW = 1.3f;

	/// <summary>The ramp's look: two stringers, planks across, a cleat every few planks for the grip, posts at its foot.</summary>
	private static void RampMesh(MeshKit k, float half, float top, float len, float foot)
	{
		float rise = top - foot;
		float run = Mathf.Sqrt(len * len + rise * rise);
		var down = new Vector3(0f, -rise, len) / run;   // down the ramp
		var tilt = new Basis(Vector3.Right, Mathf.Atan2(rise, len));   // (its Z down the slope, its Y off the planks)
		Vector3 At(float t, float x, float up) => new Vector3(x, top, half) + down * t + tilt.Y * up;
		k.Mat(PropTextures.PostMat);
		foreach (float x in new[] { -RampW * 0.5f + 0.05f, RampW * 0.5f - 0.05f })
			k.Box(At(run * 0.5f, x, -0.11f), new Vector3(0.07f, 0.16f, run + 0.04f), 0.6f, tilt);
		k.Mat(PropTextures.DeckMat);
		int planks = Mathf.Max(2, Mathf.RoundToInt(run / 0.16f));
		for (int i = 0; i < planks; i++)
		{
			float t = (i + 0.5f) * run / planks;
			k.Box(At(t, 0f, -0.02f), new Vector3(RampW, 0.04f, run / planks - 0.012f), 0.6f, tilt);
			if (i % 3 == 1) k.Box(At(t, 0f, 0.012f), new Vector3(RampW - 0.2f, 0.025f, 0.035f), 0.6f, tilt);
		}
		k.Mat(PropTextures.PostMat);
		foreach (float x in new[] { -RampW * 0.5f + 0.05f, RampW * 0.5f - 0.05f })
			k.Box(new Vector3(x, foot - 0.1f, half + len - 0.1f), new Vector3(0.1f, 0.3f, 0.1f), 0.6f);
	}

	/// <summary>The ramp's collision: one slab, its top on the planks' line from the deck's edge to below the ground at the
	/// foot, carried a little back under the deck (no lip to walk into).</summary>
	private static CollisionShape3D RampCollision(float half, float top, float len, float foot)
	{
		float rise = top - foot;
		float ang = Mathf.Atan2(rise, len);
		float over = 0.4f, past = 0.6f;   // (under the deck, and on into the ground)
		float run = Mathf.Sqrt(len * len + rise * rise) + over + past;
		const float thick = 0.5f;
		var dir = new Vector3(0f, -Mathf.Sin(ang), Mathf.Cos(ang));
		var n = new Vector3(0f, Mathf.Cos(ang), Mathf.Sin(ang));
		var start = new Vector3(0f, top, half) - dir * over;
		var centre = start + dir * run * 0.5f - n * thick * 0.5f;
		return new CollisionShape3D
		{
			Name = "Ramp", Transform = new Transform3D(new Basis(Vector3.Right, ang), centre),
			Shape = new BoxShape3D { Size = new Vector3(RampW, thick, run) },
		};
	}

	// ------------------------------------------------------------------ the sign past the bridge

	/// <summary>Just past the footbridge, where the Ridge Path comes down: which way is the Overlook Loop (on up the trail
	/// to the fork; the ridge is the loop's way back down).</summary>
	private void BridgeSign()
	{
		if (GetTree().GetFirstNodeInGroup("bridge_marker") is not Node3D bridge || _trail == null) return;
		_trail.Closest(new Vector2(bridge.GlobalPosition.X, bridge.GlobalPosition.Z), out float sb);
		float s = sb + 9f;
		Vector2 p = _trail.At(s, out Vector2 dir);
		Vector2 side = new Vector2(-dir.Y, dir.X) * 2.6f;
		var sign = new SignPost
		{
			Name = "LoopSign", Style = SignPost.SignStyle.Directional,
			Boards = new[] { "Overlook Loop  ^", "Overlook  0.2 mi  ^" }, LeanDegrees = new Vector2(1.2f, -0.6f), Seed = 77,
		};
		AddChild(sign);
		var at = p + side;
		sign.GlobalPosition = new Vector3(at.X, _terrain.HeightAt(at.X, at.Y) - 0.03f, at.Y);
		// its boards facing back down the trail, to whoever comes off the bridge
		sign.Rotation = new Vector3(0, Mathf.Atan2(dir.X, dir.Y) + Mathf.Pi, 0);   // (turned: its boards were seen from behind)
		_blazes.Add((sign, 1 | 2));
	}

	// ------------------------------------------------------------------ the pull back

	private void StrayPull(PlayerController player, float dt)
	{
		var pp = player.GlobalPosition;
		var q = new Vector2(pp.X, pp.Z);
		int stage = _builtStage < 0 ? Stage : _builtStage;
		float nearest = float.MaxValue;
		Polyline2 nearLine = null;
		float nearS = 0f;
		for (int w = 0; w < WayCount; w++)
		{
			if (_ways[w] is not { } line || line.Points.Count < 2 || IsClosed((Way)w)) continue;
			float d = line.Closest(q, out float s);
			if (d < nearest) { nearest = d; nearLine = line; nearS = s; }
		}
		// near the cabin, the lot, or on a way: nothing to pull back from
		bool moving = new Vector2(player.Velocity.X, player.Velocity.Z).Length() > 0.4f;
		if (nearLine == null || nearest < StrayDistance || Cutscene.InputLocks > 0) { _strayT = 0f; return; }
		if (moving) _strayT += dt;
		if (_strayT < StrayAfter) return;
		// a bird calls from the way, ahead of where they'd meet it
		if (_clock >= _nextStrayCall)
		{
			_nextStrayCall = _clock + 7.0;
			Vector2 call = nearLine.At(Mathf.Min(nearS + 8f, nearLine.Length), out _);
			var at = new Vector3(call.X, _terrain.HeightAt(call.X, call.Y) + 3f, call.Y);
			Audio.AudioDirector.OneShot(this, "bird", 5, at, -2f, "Birds", 8f, 0.08f);
			StrayCalls++;
			GD.Print($"[story] Act 1: off the way ({nearest:0} m) - a bird calls from it");
		}
		// still off it: a thought, once a walk
		if (_strayT > StrayAfter + 14f && _thoughtWalk != stage && UI.Subtitle.Instance is { CurrentlyShowing: false } sub)
		{
			_thoughtWalk = stage;
			StrayThought = true;
			_ = sub.Show("The trail's back that way.", 0.8f, 3.5f, 1.2f);
			GD.Print("[story] Act 1: off the way - the thought, the trail's back that way");
		}
	}
}
