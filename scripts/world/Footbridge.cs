using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// Plank footbridge. With AutoPlace it finds where the trail crosses the
/// stream (terrain.TryGetStreamCrossing), lines up with the trail and sets
/// each end flush with the ground. Built along local -Z, centred on the origin.
/// Heavy square posts run down into the banks and the stream bed; rocks pile
/// up at both abutments and in the water below; a low routed sign stands at
/// the near end. Deck collision is tagged surface = "wood".
/// </summary>
[Tool]
[GlobalClass]
public partial class Footbridge : Node3D
{
	[Export] public bool AutoPlace = true;
	[Export] public float Length = 9f;
	[Export] public float Width = 1.3f;
	[Export] public float RailHeight = 0.95f;
	/// <summary>Target spacing of the rail posts along each side.</summary>
	[Export] public float PostSpacing = 1.9f;
	[Export] public bool Rocks = true;
	[Export] public bool Sign = true;
	[Export] public string[] SignBoards = { "Blackfern Trail >", "Cullen Creek" };
	[Export] public int Seed = 7;

	private ForestTerrain _terrain;

	public override void _Ready()
	{
		float rise = 0f;
		if (!Engine.IsEditorHint() && AutoPlace)
		{
			_terrain = GroundSnap.FindTerrain(this);
			if (_terrain != null && _terrain.TryGetStreamCrossing(out Vector3 pos, out Vector3 dir, out float s))
			{
				Vector3 a = _terrain.TrailPoint(s - Length * 0.5f, out _);
				Vector3 b = _terrain.TrailPoint(s + Length * 0.5f, out _);
				Vector3 flat = new Vector3(b.X - a.X, 0, b.Z - a.Z).Normalized();
				Vector3 mid = (a + b) * 0.5f;
				float yaw = Mathf.Atan2(-flat.X, -flat.Z);
				GlobalTransform = new Transform3D(Basis.FromEuler(new Vector3(0, yaw, 0)), mid);
				rise = b.Y - a.Y;  // far end (-Z) minus near end (+Z)
				Length = new Vector2(b.X - a.X, b.Z - a.Z).Length();
			}
		}
		else if (!Engine.IsEditorHint()) _terrain = GroundSnap.FindTerrain(this);
		Build(rise);
	}

	private float Rand(int i, float lo, float hi)
	{
		unchecked
		{
			uint h = (uint)(Seed * 7919 + i * 104729 + 17);
			h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
			return lo + (hi - lo) * ((h & 0xFFFF) / 65535f);
		}
	}

	private void Build(float rise)
	{
		var old = GetNodeOrNull("Generated");
		if (old != null) { RemoveChild(old); old.QueueFree(); }
		var gen = new Node3D { Name = "Generated" };
		AddChild(gen);

		// Everything is built flat, then pitched so the deck meets the ground at both ends.
		float pitch = Mathf.Atan2(rise, Length);
		float L = Length / Mathf.Cos(pitch);
		var tilt = new Transform3D(Basis.FromEuler(new Vector3(pitch, 0, 0)), Vector3.Zero);
		var k = new MeshKit { Xf = tilt };
		var deckMat = PropTextures.DeckMat;
		var postMat = PropTextures.WetPostMat;
		float hw = Width * 0.5f, deckTop = 0.04f, plankT = 0.06f;
		float postW = 0.14f, postX = hw + postW * 0.5f + 0.01f;

		// ground depth below a point of the (tilted) bridge frame, for posts that run into the bank
		float DepthBelow(float x, float z)
		{
			if (_terrain == null) return 0.9f;
			Vector3 w = GlobalTransform * (tilt * new Vector3(x, 0, z));
			return Mathf.Clamp(w.Y - _terrain.HeightAt(w.X, w.Z) + 0.35f, 0.5f, 4.5f);
		}

		// stringers: squared timbers under the deck, a bit past each end
		k.Color = new Color(0.75f, 0.72f, 0.7f);
		float strY = deckTop - plankT - 0.12f;
		foreach (float x in new[] { -hw + 0.14f, 0f, hw - 0.14f })
			k.Mat(postMat).Box(new Vector3(x, strY, 0), new Vector3(0.15f, 0.24f, L + 0.7f), 1.4f);
		// sill logs the stringers rest on at each bank
		foreach (float e in new[] { 1f, -1f })
			k.Mat(postMat).Box(new Vector3(0, strY - 0.2f, e * (L * 0.5f + 0.1f)), new Vector3(Width + 0.7f, 0.22f, 0.26f), 1.4f);

		// planks across the deck, uneven and slightly skewed
		int n = Mathf.Max(4, Mathf.FloorToInt(L / 0.22f));
		float pitchZ = L / n;
		for (int i = 0; i < n; i++)
		{
			float z = L * 0.5f - pitchZ * (i + 0.5f);
			float shade = Rand(i, 0.72f, 1.05f);
			k.Color = new Color(shade, shade * 0.97f, shade * 0.93f);
			float extra = Rand(i + 100, 0f, 0.12f);
			float shift = Rand(i + 200, -0.05f, 0.05f);
			var rot = Basis.FromEuler(new Vector3(0, Rand(i + 300, -0.015f, 0.015f), Rand(i + 400, -0.01f, 0.01f)));
			k.Mat(deckMat).Box(new Vector3(shift, deckTop - plankT * 0.5f + Rand(i + 500, -0.004f, 0.004f), z),
				new Vector3(Width + 0.12f + extra, plankT, pitchZ - 0.022f), 1.3f, rot);
		}

		// posts, top rail, mid rail
		int seg = Mathf.Max(2, Mathf.CeilToInt((L - 0.3f) / PostSpacing));
		var posts = new List<float>();
		for (int i = 0; i <= seg; i++) posts.Add(Mathf.Lerp(L * 0.5f - 0.15f, -L * 0.5f + 0.15f, i / (float)seg));
		float railTop = RailHeight;
		int pi = 0;
		foreach (float side in new[] { -1f, 1f })
		{
			float x = side * postX;
			foreach (float z in posts)
			{
				float depth = DepthBelow(x, z);
				float shade = Rand(900 + pi++, 0.8f, 1.0f);
				k.Color = new Color(shade, shade * 0.97f, shade * 0.95f);
				k.Mat(postMat);
				SignKit.Post(k, new Vector3(x, -depth, z), railTop + depth - 0.02f, postW, 1.4f, 0.05f);
			}
			k.Color = new Color(0.82f, 0.8f, 0.77f);
			// top rail: a flat board across the post tops
			k.Mat(deckMat).Box(new Vector3(x - side * 0.01f, railTop + 0.045f, 0), new Vector3(0.15f, 0.05f, L + 0.12f), 1.3f);
			// mid rail: a plank on the inside face of the posts
			k.Box(new Vector3(x - side * (postW * 0.5f + 0.02f), railTop * 0.5f, 0), new Vector3(0.04f, 0.13f, L - 0.05f), 1.3f);
			// kick board along the deck edge
			k.Color = new Color(0.6f, 0.58f, 0.55f);
			k.Box(new Vector3(x - side * (postW * 0.5f + 0.02f), deckTop + 0.05f, 0), new Vector3(0.04f, 0.1f, L - 0.05f), 1.3f);
		}
		k.Color = Colors.White;
		k.CommitTo(gen, "BridgeMesh");

		if (Engine.IsEditorHint()) return;
		var deck = new StaticBody3D { Name = "Deck", CollisionLayer = 1, CollisionMask = 0 };
		deck.SetMeta("surface", "wood");
		gen.AddChild(deck);
		// deck box extends past the ends and dips into the ground so there is no lip to catch on
		deck.AddChild(new CollisionShape3D
		{
			Transform = tilt * new Transform3D(Basis.Identity, new Vector3(0, deckTop - 0.2f, 0)),
			Shape = new BoxShape3D { Size = new Vector3(Width + 0.1f, 0.4f, L + 0.6f) },
		});
		var rails = new StaticBody3D { Name = "Rails", CollisionLayer = 1, CollisionMask = 0 };
		rails.SetMeta("surface", "wood");
		gen.AddChild(rails);
		foreach (float x in new[] { -postX, postX })
			rails.AddChild(new CollisionShape3D
			{
				Transform = tilt * new Transform3D(Basis.Identity, new Vector3(x, RailHeight * 0.5f + 0.1f, 0)),
				Shape = new BoxShape3D { Size = new Vector3(postW + 0.02f, RailHeight + 0.2f, L) },
			});

		if (_terrain != null)
		{
			if (Rocks) BuildRocks(gen, tilt, L, pitch);
			if (Sign) BuildSign(gen, L);
		}

		var crossing = new BridgeCrossEvent { CollisionLayer = 0, CollisionMask = 2, Monitorable = false };
		gen.AddChild(crossing);
		crossing.AddChild(new CollisionShape3D
		{
			Transform = tilt * new Transform3D(Basis.Identity, new Vector3(0, deckTop + 1.0f, 0)),
			Shape = new BoxShape3D { Size = new Vector3(Width + 0.6f, 2.4f, L * 0.92f) },
		});
	}

	/// <summary>Rocks piled at both abutments and scattered through the stream bed.</summary>
	private void BuildRocks(Node3D gen, Transform3D tilt, float L, float pitch)
	{
		var toLocal = GlobalTransform.AffineInverse();
		var k = new MeshKit();
		var rock = ProcTextures.RockMat;
		var body = new StaticBody3D { Name = "Rocks", CollisionLayer = 1, CollisionMask = 0 };
		body.SetMeta("surface", "rock");
		gen.AddChild(body);
		float hw = Width * 0.5f;
		int id = 0;

		// deck underside height (local, untilted) at local z
		float DeckBottom(float z) => -z * Mathf.Tan(pitch) - 0.45f;

		// world point with Y = rock centre height
		void Rock(Vector3 world, float r, float squash, bool collide, float wet)
		{
			Vector3 lp = toLocal * world;
			// keep clear of the deck and the walking line
			if (Mathf.Abs(lp.X) < hw + 0.35f + r && lp.Y + r * squash > DeckBottom(lp.Z)) return;
			if (Mathf.Abs(lp.X) < hw + 0.2f && Mathf.Abs(lp.Z) > L * 0.5f - 0.2f) return;
			float shade = Rand(3000 + id, 0.95f, 1.3f) * (1f - wet * 0.3f);
			k.Color = new Color(shade, shade * 1.01f, shade * 0.97f);
			k.Mat(rock).Blob(lp, new Vector3(r * Rand(3100 + id, 0.9f, 1.25f), r * squash, r * Rand(3200 + id, 0.85f, 1.15f)),
				Seed * 31 + id, 0.22f, true, 0.9f, 0.45f);
			id++;
			if (collide && r > 0.3f)
				body.AddChild(new CollisionShape3D { Position = lp, Shape = new SphereShape3D { Radius = r * squash * 0.95f } });
		}
		Vector3 OnGround(Vector3 w, float r, float squash, float sink) { w.Y = _terrain.HeightAt(w.X, w.Z) - r * squash * sink; return w; }
		Vector3 W(float x, float z) => GlobalTransform * new Vector3(x, 0, z);

		// abutments: a heap either side of each end, outside the rails
		int a = 0;
		foreach (float e in new[] { 1f, -1f })
			foreach (float side in new[] { -1f, 1f })
				for (int j = 0; j < 3; j++, a++)
				{
					float r = Rand(a * 7 + 11, 0.28f, 0.55f);
					float x = side * (hw + 0.45f + r + Rand(a * 7 + 12, 0f, 0.5f));
					float z = e * (L * 0.5f - 0.9f + j * 0.75f + Rand(a * 7 + 13, -0.2f, 0.2f));
					Rock(OnGround(W(x, z), r, 0.7f, 0.3f), r, 0.7f, true, 0.1f);
				}

		// stream: rocks breaking the water up- and downstream of the bridge, boulders along the banks
		var stream = _terrain.Stream;
		if (stream != null && stream.Points.Count > 1)
		{
			Vector3 o = _terrain.GlobalPosition;
			Vector3 c = GlobalPosition;
			stream.Closest(new Vector2(c.X - o.X, c.Z - o.Z), out float s0);
			// half-width of the water on one side: where the bank rises out of it
			float Half(Vector2 p, Vector2 nrm, float water)
			{
				for (float d = 0.25f; d < 8f; d += 0.25f)
				{
					Vector2 q = p + nrm * d;
					if (_terrain.HeightAt(q.X + o.X, q.Y + o.Z) > water) return d;
				}
				return 8f;
			}
			for (int i = 0; i < 22; i++)
			{
				float s = s0 + Rand(4000 + i, -8f, 8f);
				Vector2 p = stream.At(s, out Vector2 t);
				Vector2 nrm = new Vector2(-t.Y, t.X).Normalized();
				float water = _terrain.WaterLevel(s);
				float side = Rand(4050 + i, 0f, 1f) < 0.5f ? -1f : 1f;
				float half = Half(p, nrm * side, water);
				bool bank = i % 3 == 0;
				float r = bank ? Rand(4200 + i, 0.45f, 0.8f) : Rand(4200 + i, 0.18f, 0.45f);
				float lat = bank ? half + Rand(4100 + i, -0.3f, 0.5f) : Rand(4100 + i, 0f, Mathf.Max(0.1f, half - 0.25f));
				Vector2 q = p + nrm * side * lat;
				var w = new Vector3(q.X + o.X, 0, q.Y + o.Z);
				float ground = _terrain.HeightAt(w.X, w.Z);
				float squash = bank ? 0.65f : 0.6f;
				// break the surface: the top sits 30-70 % of the rock's height above the water
				w.Y = Mathf.Max(ground - r * squash * 0.3f, water - r * squash * Rand(4300 + i, 0.3f, 0.6f));
				Rock(w, r, squash, bank, bank ? 0.4f : 1f);
			}
		}
		if (!k.IsEmpty) k.CommitTo(gen, "RockMesh");
		if (body.GetChildCount() == 0) body.QueueFree();
	}

	/// <summary>Low routed sign at the near (+Z) end, on the left as you arrive, pointing at the bridge.</summary>
	private void BuildSign(Node3D gen, float L)
	{
		var sign = new SignPost
		{
			Name = "BridgeSign",
			Style = SignPost.SignStyle.Low,
			Boards = SignBoards,
			LeanDegrees = new Vector2(5f, 3f),
			ClearRadius = 1.1f,
			Seed = Seed + 2,
		};
		gen.AddChild(sign);
		Vector3 p = GlobalTransform * new Vector3(-(Width * 0.5f + 1.25f), 0, L * 0.5f + 1.4f);
		p.Y = _terrain.HeightAt(p.X, p.Z) - 0.03f;
		sign.GlobalTransform = new Transform3D(GlobalBasis * Basis.FromEuler(new Vector3(0, Mathf.DegToRad(22f), 0)), p);
	}
}
