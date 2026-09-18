using Godot;

namespace ProjectDS.World;

/// <summary>
/// Plank footbridge. With AutoPlace it finds where the trail crosses the
/// stream (terrain.TryGetStreamCrossing), lines up with the trail and sets
/// each end flush with the ground. Built along local -Z, centred on the origin.
/// Deck collision is tagged surface = "wood".
/// </summary>
[Tool]
[GlobalClass]
public partial class Footbridge : Node3D
{
	[Export] public bool AutoPlace = true;
	[Export] public float Length = 9f;
	[Export] public float Width = 1.3f;
	[Export] public float RailHeight = 0.95f;

	public override void _Ready()
	{
		float rise = 0f;
		if (!Engine.IsEditorHint() && AutoPlace)
		{
			var terrain = GroundSnap.FindTerrain(this);
			if (terrain != null && terrain.TryGetStreamCrossing(out Vector3 pos, out Vector3 dir, out float s))
			{
				Vector3 a = terrain.TrailPoint(s - Length * 0.5f, out _);
				Vector3 b = terrain.TrailPoint(s + Length * 0.5f, out _);
				Vector3 flat = new Vector3(b.X - a.X, 0, b.Z - a.Z).Normalized();
				Vector3 mid = (a + b) * 0.5f;
				float yaw = Mathf.Atan2(-flat.X, -flat.Z);
				GlobalTransform = new Transform3D(Basis.FromEuler(new Vector3(0, yaw, 0)), mid);
				rise = b.Y - a.Y;  // far end (-Z) minus near end (+Z)
				Length = new Vector2(b.X - a.X, b.Z - a.Z).Length();
			}
		}
		Build(rise);
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
		var wood = ProcTextures.WoodMat;
		float hw = Width * 0.5f, deckTop = 0.04f, plankT = 0.06f;

		// stringers (logs) under the deck
		k.Color = new Color(0.8f, 0.75f, 0.7f);
		foreach (float x in new[] { -hw + 0.15f, hw - 0.15f })
			k.Mat(ProcTextures.BarkMat).Cylinder(new Vector3(x, deckTop - plankT - 0.14f, L * 0.5f + 0.4f),
				new Vector3(x, deckTop - plankT - 0.14f, -L * 0.5f - 0.4f), 0.15f, 0.14f, 7, true, 1f);
		// planks
		int n = Mathf.FloorToInt(L / 0.21f);
		for (int i = 0; i < n; i++)
		{
			float z = L * 0.5f - 0.105f - i * (L / n);
			float shade = 0.75f + 0.25f * Mathf.Abs(Mathf.Sin(i * 12.9898f));
			k.Color = new Color(shade, shade * 0.97f, shade * 0.93f);
			k.Mat(wood).Box(new Vector3(0, deckTop - plankT * 0.5f, z), new Vector3(Width + (i % 3 == 0 ? 0.06f : 0f), plankT, L / n - 0.015f), 1.5f);
		}
		// rails: posts at ends + middle, top rail and mid rail
		k.Color = new Color(0.8f, 0.76f, 0.72f);
		float[] postZ = { L * 0.5f - 0.1f, 0f, -L * 0.5f + 0.1f };
		foreach (float x in new[] { -hw - 0.04f, hw + 0.04f })
		{
			foreach (float z in postZ)
				k.Box(new Vector3(x, RailHeight * 0.5f - 0.1f, z), new Vector3(0.1f, RailHeight + 0.2f, 0.1f), 2f);
			k.Beam(new Vector3(x, RailHeight, L * 0.5f), new Vector3(x, RailHeight, -L * 0.5f), 0.08f, 0.07f, 2f);
			k.Beam(new Vector3(x, RailHeight * 0.5f, L * 0.5f), new Vector3(x, RailHeight * 0.5f, -L * 0.5f), 0.06f, 0.06f, 2f);
		}
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
		gen.AddChild(rails);
		foreach (float x in new[] { -hw - 0.04f, hw + 0.04f })
			rails.AddChild(new CollisionShape3D
			{
				Transform = tilt * new Transform3D(Basis.Identity, new Vector3(x, RailHeight * 0.5f + 0.1f, 0)),
				Shape = new BoxShape3D { Size = new Vector3(0.1f, RailHeight + 0.2f, L) },
			});
	}
}
