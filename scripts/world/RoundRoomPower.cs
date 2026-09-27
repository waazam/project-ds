using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.UI;
using ProjectDS.World.StairwellParts;

namespace ProjectDS.World;

/// <summary>
/// Act 20's power puzzle (the owner's "Real Act 20"). The round room is lit by bare bulbs in wire
/// cages on a conduit round the drum, and the ring of light round the dais burns a solid, ominous red.
/// Six old knife switches, three on each side wall (the right and the left as you come in), each with
/// three positions: down, middle, up. Facing either wall, the far-left one must be down, the middle
/// one in the middle, the right one up (the journal in the library, found by blacklight, has it as a
/// riddle: "the lefts are feeling down, the rights are all right, and if that's true the middles are
/// doing just fine"). When all six are right, the power surges: the bulbs strain and burst, the room
/// goes dark but for the red ring, then every candle in the drum lights itself, and the ring turns
/// green in a chaser that runs round and round until it is solid green. Only then will the dais rise.
/// No strobing anywhere in it: every light change is a single smooth swell or fade.
/// </summary>
public partial class RoundRoom
{
	public enum Lever { Down, Middle, Up }

	private const float SwitchScale = 1.45f;

	public sealed class Switch
	{
		public int Index;
		/// <summary>On the +X wall (the left as you come in) or the -X wall (the right).</summary>
		public bool OnPlusX;
		/// <summary>-1 the left one (facing the wall), 0 the middle, 1 the right.</summary>
		public int Slot;
		public Lever Pos;
		public Lever Want => Slot < 0 ? Lever.Down : Slot == 0 ? Lever.Middle : Lever.Up;
		public Node3D Root, Pivot;
		public PickupInteractable Use;
	}

	/// <summary>Where the lever sits for each position (radians about the plate's horizontal axis).</summary>
	private static float LeverAngle(Lever p) => p switch { Lever.Up => -1.2f, Lever.Down => 1.2f, _ => 0f };

	public IReadOnlyList<Switch> Switches => _switches;
	public bool Powered { get; private set; }
	public bool Surging { get; private set; }
	public bool AllSwitchesRight => _switches.Count == 6 && _switches.TrueForAll(s => s.Pos == s.Want);

	private readonly List<Switch> _switches = new();
	private readonly List<(MeshInstance3D glass, OmniLight3D light, float energy)> _bulbs = new();
	private readonly List<(MeshInstance3D flame, OmniLight3D light, float energy, float y, float angle)> _candles = new();
	private ShaderMaterial _ringMat, _haloMat;
	private OmniLight3D _ringLight, _fill, _fillHigh;
	private AudioStreamPlayer3D _buzz;
	private float _clock, _sag;
	private ulong _lastRedRemark;

	private static readonly Color BulbColour = new(1f, 0.93f, 0.8f), CandleColour = new(1f, 0.72f, 0.42f);
	private static readonly Color Red = new(1f, 0.06f, 0.04f), Green = new(0.18f, 1f, 0.32f);

	// ------------------------------------------------------------------ building

	/// <summary>The six switches, three to a side wall, a conduit from each up to a ring round the drum.</summary>
	private void BuildSwitches()
	{
		// a scramble to start from: nothing already where it should be
		Lever[] start = { Lever.Up, Lever.Down, Lever.Down, Lever.Middle, Lever.Up, Lever.Down };
		int n = 0;
		foreach (bool plusX in new[] { true, false })
		{
			float c = plusX ? Mathf.Pi * 0.5f : Mathf.Pi * 1.5f;
			foreach (int slot in new[] { -1, 0, 1 })
			{
				// angles increase to the viewer's left when facing the wall, so the left one is at +45 degrees
				float a = c - slot * Mathf.Pi * 0.25f;
				var sw = new Switch { Index = n, OnPlusX = plusX, Slot = slot, Pos = start[n] };
				BuildSwitch(sw, a);
				_switches.Add(sw);
				n++;
			}
		}
		BuildConduit();
	}

	private static Basis WallBasis(float a)
	{
		Vector3 d = new(Mathf.Sin(a), 0, Mathf.Cos(a));
		Vector3 z = -d, y = Vector3.Up, x = y.Cross(z).Normalized();
		return new Basis(x, y, z);
	}

	private void BuildSwitch(Switch sw, float a)
	{
		Vector3 d = new(Mathf.Sin(a), 0, Mathf.Cos(a));
		// a big old industrial switch: everything below is drawn at 1:1 and the whole thing scaled up
		var root = new Node3D { Name = $"Switch{sw.Index}", Transform = new Transform3D(WallBasis(a).Scaled(Vector3.One * SwitchScale), d * (Radius - 0.005f) + Vector3.Up * 1.35f) };
		AddChild(root);
		sw.Root = root;
		var bakelite = new StandardMaterial3D { AlbedoColor = new Color(0.11f, 0.085f, 0.065f), Roughness = 0.45f, MetallicSpecular = 0.6f };
		var black = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.055f), Roughness = 0.55f };
		var copper = new StandardMaterial3D { AlbedoColor = new Color(0.72f, 0.42f, 0.25f), Metallic = 0.8f, Roughness = 0.35f };
		var steel = StairwellTextures.SteelMat;
		var red = new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.06f, 0.05f), Roughness = 0.35f, MetallicSpecular = 0.7f };
		Box(root, "Plate", new Vector3(0, 0, 0.02f), new Vector3(0.34f, 0.56f, 0.04f), bakelite);
		foreach (var (x, y) in new[] { (-0.14f, 0.25f), (0.14f, 0.25f), (-0.14f, -0.25f), (0.14f, -0.25f) })
			root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 0.012f, RadialSegments = 8 }, Position = new Vector3(x, y, 0.044f), Rotation = new Vector3(Mathf.Pi * 0.5f, 0, 0), MaterialOverride = steel });
		// the jaws the blades close into, top and bottom, and the hinge blocks
		foreach (float y in new[] { 0.19f, -0.19f })
			foreach (float x in new[] { -0.1f, 0.1f })
			{
				Box(root, "Jaw", new Vector3(x - 0.014f, y, 0.07f), new Vector3(0.008f, 0.07f, 0.06f), copper);
				Box(root, "Jaw", new Vector3(x + 0.014f, y, 0.07f), new Vector3(0.008f, 0.07f, 0.06f), copper);
				Box(root, "JawFoot", new Vector3(x, y, 0.045f), new Vector3(0.04f, 0.08f, 0.012f), black);
			}
		foreach (float x in new[] { -0.1f, 0.1f }) Box(root, "Hinge", new Vector3(x, 0, 0.065f), new Vector3(0.04f, 0.06f, 0.05f), black);
		// the warning stickers: a yellow triangle with a black bolt, above and below the hinge
		foreach (float y in new[] { 0.105f, -0.105f }) root.AddChild(Warning(new Vector3(0, y, 0.041f), 0.085f));
		// the lever: two black arms, a crossbar with the danger sign, the red grip
		var pivot = new Node3D { Name = "Pivot", Position = new Vector3(0, 0, 0.075f), Rotation = new Vector3(LeverAngle(sw.Pos), 0, 0) };
		root.AddChild(pivot);
		sw.Pivot = pivot;
		foreach (float x in new[] { -0.1f, 0.1f })
		{
			Box(pivot, "Arm", new Vector3(x, 0, 0.19f), new Vector3(0.022f, 0.032f, 0.38f), black);
			Box(pivot, "Blade", new Vector3(x, 0, 0.01f), new Vector3(0.006f, 0.05f, 0.07f), copper);
		}
		Box(pivot, "Crossbar", new Vector3(0, 0, 0.2f), new Vector3(0.25f, 0.018f, 0.075f), black);
		foreach (float side in new[] { 1f, -1f })
		{
			// the sign on both faces of the crossbar (it faces you with the lever up or down)
			// (the text reads upright on whichever face is toward you: toward the pivot, which is up with the lever down)
			var face = new Node3D { Position = new Vector3(0, side * 0.0095f, 0.2f), Rotation = new Vector3(side * -Mathf.Pi * 0.5f, 0, 0) };
			pivot.AddChild(face);
			face.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(0.1f, 0.055f) }, Position = new Vector3(0, 0, 0.0005f), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.9f, 0.86f), Roughness = 0.6f } });
			face.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(0.09f, 0.017f) }, Position = new Vector3(0, 0.014f, 0.001f), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.05f, 0.05f), Roughness = 0.6f } });
			face.AddChild(new Label3D { Text = "DANGER", FontSize = 20, PixelSize = 0.0008f, Modulate = Colors.White, OutlineSize = 0, Shaded = true, Position = new Vector3(0, 0.014f, 0.0015f) });
			face.AddChild(new Label3D { Text = "HIGH\nVOLTAGE", FontSize = 18, PixelSize = 0.0008f, LineSpacing = -4f, Modulate = Colors.Black, OutlineSize = 0, Shaded = true, Position = new Vector3(0, -0.009f, 0.0015f) });
		}
		pivot.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.022f, Height = 0.2f, RadialSegments = 12 }, Position = new Vector3(0, 0, 0.37f), Rotation = new Vector3(0, 0, Mathf.Pi * 0.5f), MaterialOverride = red });
		foreach (float x in new[] { -0.115f, 0.115f })
			pivot.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 0.035f, RadialSegments = 12 }, Position = new Vector3(x, 0, 0.37f), Rotation = new Vector3(0, 0, Mathf.Pi * 0.5f), MaterialOverride = black });
		sw.Use = new PickupInteractable
		{
			Name = "Use", PickRadius = 0.2f, MaxDistance = 2.6f, Position = new Vector3(0, 0, 0.3f),
			PromptFor = _ => "Throw the switch",
			CanUse = _ => !Powered && !Surging,
		};
		sw.Use.Interacted += p => OnThrow(sw, p);
		pivot.AddChild(sw.Use);
		// the plate and its jaws are solid (only those: anything deeper would stand between your eye and the grip)
		_body.AddChild(new CollisionShape3D { Transform = new Transform3D(WallBasis(a), root.Position + WallBasis(a).Z * 0.05f * SwitchScale), Shape = new BoxShape3D { Size = new Vector3(0.36f, 0.6f, 0.1f) * SwitchScale } });
	}

	private static MeshInstance3D Box(Node3D parent, string name, Vector3 at, Vector3 size, Material mat)
	{
		var m = new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = size }, Position = at, MaterialOverride = mat };
		parent.AddChild(m);
		return m;
	}

	/// <summary>A yellow warning triangle with a black border and a lightning bolt, facing +Z, <paramref name="size"/> across.</summary>
	private static MeshInstance3D Warning(Vector3 at, float size)
	{
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		void Tri(Vector2 a, Vector2 b, Vector2 c, Color col, float z)
		{
			st.SetColor(col); st.SetNormal(Vector3.Back);
			st.AddVertex(new Vector3(a.X, a.Y, z)); st.AddVertex(new Vector3(c.X, c.Y, z)); st.AddVertex(new Vector3(b.X, b.Y, z));
		}
		float h = size * 0.866f;
		Vector2 top = new(0, h * 0.62f), bl = new(-size * 0.5f, -h * 0.38f), br = new(size * 0.5f, -h * 0.38f);
		Tri(top, bl, br, new Color(0.05f, 0.05f, 0.05f), 0f);
		Vector2 ctr = new(0, 0);
		Tri(ctr + (top - ctr) * 0.8f, ctr + (bl - ctr) * 0.8f, ctr + (br - ctr) * 0.8f, new Color(0.98f, 0.78f, 0.08f), 0.0006f);
		// the bolt: two slanted wedges
		float s = size;
		Tri(new Vector2(0.03f * s, 0.3f * s), new Vector2(-0.1f * s, -0.02f * s), new Vector2(0.04f * s, 0.01f * s), Colors.Black, 0.0012f);
		Tri(new Vector2(-0.04f * s, -0.01f * s), new Vector2(0.1f * s, 0.02f * s), new Vector2(-0.03f * s, -0.28f * s), Colors.Black, 0.0012f);
		return new MeshInstance3D
		{
			Name = "Warning", Mesh = st.Commit(), Position = at,
			MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.5f, CullMode = BaseMaterial3D.CullModeEnum.Disabled },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
	}

	/// <summary>Grey conduit from each switch up the wall to a ring round the drum, and caged bulbs hung off it.</summary>
	private void BuildConduit()
	{
		var k = new MeshKit();
		k.Mat(StairwellTextures.SteelMat);
		k.Color = new Color(0.42f, 0.42f, 0.4f);
		const float ringY = 3.35f, ringR = Radius - 0.2f;
		foreach (var sw in _switches)
		{
			var xf = sw.Root.Transform;
			// up the wall beside the candle over the switch, then out to the ring on a short elbow
			Vector3 foot = xf * new Vector3(0.12f, 0.28f, 0.02f);
			Vector3 up = (xf * new Vector3(0.12f, 0f, 0.02f)) with { Y = ringY - 0.12f };
			Vector3 dIn = (-(up with { Y = 0 })).Normalized();
			Vector3 joint = up + Vector3.Up * 0.12f + dIn * 0.17f;
			k.Cylinder(foot, up, 0.014f, 0.014f, 6, false);
			k.Cylinder(up, joint, 0.014f, 0.014f, 6, false);
			k.Cylinder(joint + Vector3.Down * 0.04f, joint + Vector3.Up * 0.04f, 0.035f, 0.035f, 8, true);   // the junction box
		}
		const int n = 64;
		for (int i = 0; i < n; i++)
		{
			float a0 = Mathf.Tau * i / n, a1 = Mathf.Tau * (i + 1) / n;
			k.Cylinder(new Vector3(Mathf.Sin(a0) * ringR, ringY, Mathf.Cos(a0) * ringR), new Vector3(Mathf.Sin(a1) * ringR, ringY, Mathf.Cos(a1) * ringR), 0.014f, 0.014f, 6, false);
			if (i % 4 == 0)
			{
				// a standoff bracket back to the wall
				Vector3 d = new(Mathf.Sin(a0), 0, Mathf.Cos(a0));
				k.Cylinder(d * ringR + Vector3.Up * ringY, d * (Radius - 0.01f) + Vector3.Up * ringY, 0.008f, 0.008f, 4, false);
			}
		}
		// the bulbs: a drop, a socket, a bare bulb in a wire cage
		var glassMat = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.96f, 0.85f), EmissionEnabled = true, Emission = BulbColour, EmissionEnergyMultiplier = 2.4f, Roughness = 0.2f };
		for (int i = 0; i < 8; i++)
		{
			float a = Mathf.Tau * i / 8f + 0.18f;
			Vector3 d = new(Mathf.Sin(a), 0, Mathf.Cos(a));
			Vector3 top = d * ringR + Vector3.Up * ringY, bulb = top + Vector3.Down * 0.36f;
			k.Cylinder(top, top + Vector3.Down * 0.2f, 0.01f, 0.01f, 6, false);
			k.Cylinder(top + Vector3.Down * 0.2f, top + Vector3.Down * 0.29f, 0.03f, 0.026f, 10, true);
			// the cage: four wires round the bulb, a hoop at its waist
			for (int w = 0; w < 4; w++)
			{
				float b = Mathf.Tau * w / 4f + 0.4f;
				Vector3 o = new Vector3(Mathf.Sin(b), 0, Mathf.Cos(b)) * 0.07f;
				k.Beam(top + Vector3.Down * 0.28f, bulb + o, 0.005f, 0.005f);
				k.Beam(bulb + o, bulb + Vector3.Down * 0.09f, 0.005f, 0.005f);
			}
			for (int w = 0; w < 8; w++)
			{
				float b0 = Mathf.Tau * w / 8f, b1 = Mathf.Tau * (w + 1) / 8f;
				k.Beam(bulb + new Vector3(Mathf.Sin(b0), 0, Mathf.Cos(b0)) * 0.07f, bulb + new Vector3(Mathf.Sin(b1), 0, Mathf.Cos(b1)) * 0.07f, 0.005f, 0.005f);
			}
			var glass = new MeshInstance3D { Name = $"Bulb{i}", Mesh = new SphereMesh { Radius = 0.045f, Height = 0.11f, RadialSegments = 10, Rings = 6 }, Position = bulb + Vector3.Up * 0.01f, MaterialOverride = glassMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
			AddChild(glass);
			var light = new OmniLight3D { Name = $"BulbLight{i}", Position = bulb - d * 0.05f, LightColor = BulbColour, LightEnergy = 1.35f, OmniRange = 9f, OmniAttenuation = 1.2f, ShadowEnabled = false };
			AddChild(light);
			_bulbs.Add((glass, light, 1.35f));
		}
		k.CommitTo(this, "Conduit", false);
		// their buzz
		var buzz = "res://assets/audio/sfx/bulb_buzz_loop.wav";
		if (ResourceLoader.Exists(buzz))
		{
			var stream = GD.Load<AudioStream>(buzz);
			if (stream is AudioStreamWav w) { w = (AudioStreamWav)w.Duplicate(); w.LoopMode = AudioStreamWav.LoopModeEnum.Forward; w.LoopEnd = Mathf.RoundToInt(w.GetLength() * w.MixRate); stream = w; }
			_buzz = new AudioStreamPlayer3D { Name = "BulbBuzz", Stream = stream, Bus = "Events", VolumeDb = -20f, UnitSize = 6f, MaxDistance = 30f, Position = new Vector3(0, ringY, 0) };
			AddChild(_buzz);
		}
	}

	/// <summary>The ring round the dais (on its sloped foot, so it rides up with it) and its glow on the floor round it.</summary>
	private void BuildRing()
	{
		_ringMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/dais_ring.gdshader") };
		_haloMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/dais_ring_halo.gdshader") };
		const int n = 96;
		// on the slope: r = DaisFoot - (DaisFoot - DaisR) * s, y = DaisTop * s
		float Slope(float s, out float y) { y = DaisTop * s; return DaisFoot - (DaisFoot - DaisR) * s; }
		var nrm = new Vector2(DaisTop, DaisFoot - DaisR).Normalized();   // (outward, up)
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		for (int i = 0; i < n; i++)
		{
			float u0 = (float)i / n, u1 = (float)(i + 1) / n;
			float a0 = Mathf.Tau * u0, a1 = Mathf.Tau * u1;
			float r0 = Slope(0.07f, out float y0), r1 = Slope(0.24f, out float y1);
			Vector3 P(float a, float r, float y) => new Vector3(Mathf.Sin(a) * (r + nrm.X * 0.008f), y + nrm.Y * 0.008f, Mathf.Cos(a) * (r + nrm.X * 0.008f));
			Vector3 a = P(a0, r0, y0), b = P(a1, r0, y0), c = P(a1, r1, y1), d = P(a0, r1, y1);
			st.SetUV(new Vector2(u0, 0)); st.AddVertex(a);
			st.SetUV(new Vector2(u1, 0)); st.AddVertex(b);
			st.SetUV(new Vector2(u1, 1)); st.AddVertex(c);
			st.SetUV(new Vector2(u0, 0)); st.AddVertex(a);
			st.SetUV(new Vector2(u1, 1)); st.AddVertex(c);
			st.SetUV(new Vector2(u0, 1)); st.AddVertex(d);
		}
		Dais.AddChild(new MeshInstance3D { Name = "Ring", Mesh = st.Commit(), MaterialOverride = _ringMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		var h = new SurfaceTool();
		h.Begin(Mesh.PrimitiveType.Triangles);
		for (int i = 0; i < n; i++)
		{
			float u0 = (float)i / n, u1 = (float)(i + 1) / n;
			float a0 = Mathf.Tau * u0, a1 = Mathf.Tau * u1;
			const float ri = DaisFoot - 0.02f, ro = DaisFoot + 0.75f, y = 0.006f;
			Vector3 a = new(Mathf.Sin(a0) * ri, y, Mathf.Cos(a0) * ri), b = new(Mathf.Sin(a1) * ri, y, Mathf.Cos(a1) * ri);
			Vector3 c = new(Mathf.Sin(a1) * ro, y, Mathf.Cos(a1) * ro), d = new(Mathf.Sin(a0) * ro, y, Mathf.Cos(a0) * ro);
			h.SetUV(new Vector2(u0, 0)); h.AddVertex(a);
			h.SetUV(new Vector2(u1, 0)); h.AddVertex(b);
			h.SetUV(new Vector2(u1, 1)); h.AddVertex(c);
			h.SetUV(new Vector2(u0, 0)); h.AddVertex(a);
			h.SetUV(new Vector2(u1, 1)); h.AddVertex(c);
			h.SetUV(new Vector2(u0, 1)); h.AddVertex(d);
		}
		AddChild(new MeshInstance3D { Name = "RingGlow", Mesh = h.Commit(), MaterialOverride = _haloMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
		_ringLight = new OmniLight3D { Name = "RingLight", Position = new Vector3(0, 0.3f, 0), LightColor = Red, LightEnergy = 1.6f, OmniRange = 6.5f, OmniAttenuation = 1.4f, ShadowEnabled = false };
		Dais.AddChild(_ringLight);
		SetRing(0f, 0f, 1f);
	}

	private void SetRing(float head, float tail, float intensity)
	{
		foreach (var m in new[] { _ringMat, _haloMat })
		{
			m?.SetShaderParameter("head", head);
			m?.SetShaderParameter("tail", tail);
			m?.SetShaderParameter("intensity", intensity);
		}
		if (_ringLight != null)
		{
			_ringLight.LightColor = Red.Lerp(Green, Mathf.Clamp(tail, 0f, 1f));
			_ringLight.LightEnergy = 1.6f * intensity;
		}
	}

	/// <summary>Sets the room's lights for the power being off (bulbs) or back (candles).</summary>
	private void ApplyPower(bool powered)
	{
		foreach (var (glass, light, energy) in _bulbs)
		{
			light.Visible = !powered;
			light.LightEnergy = energy;
			if (powered) glass.MaterialOverride = DeadBulb;
		}
		foreach (var (flame, light, energy, _, _) in _candles)
		{
			flame.Visible = powered;
			flame.Scale = Vector3.One;
			light.Visible = powered;
			light.LightEnergy = energy;
		}
		if (_fill != null)
		{
			_fill.LightColor = powered ? new Color(1f, 0.8f, 0.58f) : new Color(0.85f, 0.87f, 0.9f);
			_fill.LightEnergy = powered ? 0.9f : 0.35f;
		}
		if (_fillHigh != null) { _fillHigh.Visible = powered; _fillHigh.LightEnergy = 0.7f; }
		if (_buzz != null) { if (powered) _buzz.Stop(); else if (!_buzz.Playing && IsInsideTree()) _buzz.Play(); }
		SetRing(0f, powered ? 1f : 0f, 1f);
		if (powered) foreach (var sw in _switches) { sw.Pos = sw.Want; sw.Pivot.Rotation = new Vector3(LeverAngle(sw.Pos), 0, 0); }
	}

	private static StandardMaterial3D _deadBulb;
	private static StandardMaterial3D DeadBulb => _deadBulb ??= new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.15f, 0.14f), Roughness = 0.3f, MetallicSpecular = 0.8f };

	// ------------------------------------------------------------------ the lights, alive

	public override void _Process(double delta)
	{
		if (_candles.Count == 0) return;
		_clock += (float)delta;
		float t = _clock;
		if (!Surging)
		{
			if (!Powered)
			{
				// the bulbs: a slow, uneven breath of the supply, never a flicker
				for (int i = 0; i < _bulbs.Count; i++)
				{
					var (_, light, energy) = _bulbs[i];
					float w = 0.94f + 0.04f * Mathf.Sin(t * 0.9f + i * 1.7f) + 0.02f * Mathf.Sin(t * 2.3f + i * 0.6f);
					light.LightEnergy = energy * w * (1f - 0.45f * Mathf.Sin(_sag * Mathf.Pi));
				}
				_sag = Mathf.MoveToward(_sag, 0f, (float)delta / 0.9f);
				// the red ring breathes, slow
				SetRing(0f, 0f, 0.9f + 0.1f * Mathf.Sin(t * 1.4f));
			}
			else
			{
				// candlelight: a soft, low wobble on each flame
				for (int i = 0; i < _candles.Count; i++)
				{
					var (flame, light, energy, _, _) = _candles[i];
					float w = 0.92f + 0.05f * Mathf.Sin(t * 2.1f + i * 2.3f) + 0.03f * Mathf.Sin(t * 3.7f + i * 0.9f);
					light.LightEnergy = energy * w;
					flame.Scale = new Vector3(1f, 0.9f + 0.12f * w, 1f);
				}
			}
		}
	}

	// ------------------------------------------------------------------ the switches

	private void OnThrow(Switch sw, PlayerController player)
	{
		if (Powered || Surging) return;
		sw.Pos = sw.Pos switch { Lever.Down => Lever.Middle, Lever.Middle => Lever.Up, _ => Lever.Down };
		var tw = CreateTween();
		tw.TweenProperty(sw.Pivot, "rotation:x", LeverAngle(sw.Pos), 0.22f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		AudioDirector.OneShot(this, "lever_throw", 3, sw.Pivot.GlobalPosition, -2f);
		GD.Print($"[story] Act 20: switch {sw.Index + 1} ({(sw.OnPlusX ? "left wall" : "right wall")}, {(sw.Slot < 0 ? "left" : sw.Slot == 0 ? "middle" : "right")}) thrown {sw.Pos}");
		if (AllSwitchesRight) { Surging = true; _ = Cutscene.Run(this, ct => Surge(player, ct), lockInput: true); }
		else
		{
			// the bulbs sag with the load and come back: one slow dip, no flicker
			_sag = 1f;
		}
	}

	/// <summary>The power back: the bulbs strain and burst, the dark, the candles lighting round the
	/// drum, the ring charging from red to green. About ten seconds, the player held but free to look.</summary>
	private async Task Surge(PlayerController player, CancellationToken ct)
	{
		GD.Print("[story] Act 20: all six switches right - the power surges");
		await Cutscene.Wait(this, 0.45, ct);
		var centre = ToGlobal(new Vector3(0, 3.2f, 0));
		AudioDirector.OneShot(this, "power_surge", 1, centre, 2f, "Events", 10f, 0f);
		// the bulbs strain: one long swell, the buzz climbing
		double t = 0;
		const double swell = 2.4;
		while (t < swell)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			float u = (float)(t / swell);
			float k = 1f + 1.6f * u * u;
			foreach (var (_, light, energy) in _bulbs) light.LightEnergy = energy * k;
			if (_buzz != null) { _buzz.VolumeDb = Mathf.Lerp(-20f, -6f, u); _buzz.PitchScale = 1f + 0.35f * u; }
			SetRing(0f, 0f, 1f + 0.5f * u);
			LookToward(player, CentreWorld + Vector3.Up * 2.5f, dt * 0.6f);
		}
		// they go, one after another round the room from the nearest: each just goes out, once
		var order = new List<int>();
		for (int i = 0; i < _bulbs.Count; i++) order.Add(i);
		Vector3 me = player.GlobalPosition;
		order.Sort((a, b) => _bulbs[a].light.GlobalPosition.DistanceSquaredTo(me).CompareTo(_bulbs[b].light.GlobalPosition.DistanceSquaredTo(me)));
		foreach (int i in order)
		{
			var (glass, light, _) = _bulbs[i];
			var tw = CreateTween();
			tw.TweenProperty(light, "light_energy", 0f, 0.12f);
			tw.TweenCallback(Callable.From(() => light.Visible = false));
			glass.MaterialOverride = DeadBulb;
			AudioDirector.OneShot(this, "bulb_pop", 1, glass.GlobalPosition, 0f, "Events", 4f, 0.12f);
			await Cutscene.Wait(this, 0.13, ct);
		}
		_buzz?.Stop();
		// the room drops away to the red of the ring
		var fade = CreateTween();
		if (_fill != null) fade.TweenProperty(_fill, "light_energy", 0f, 0.8f);
		t = 0;
		while (t < 1.4)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			SetRing(0f, 0f, Mathf.Lerp(1.5f, 0.75f, Mathf.Min(1f, (float)t / 0.8f)));
			LookToward(player, CentreWorld + Vector3.Up * 1.5f, dt * 0.6f);
		}
		// the candles light themselves: round the bottom of the drum from the way in, then up it
		var lit = new List<(int i, float at)>();
		for (int i = 0; i < _candles.Count; i++)
		{
			var c = _candles[i];
			float round = Mathf.Abs(Mathf.AngleDifference(c.angle, Mathf.Pi)) / Mathf.Pi;   // 0 by the door, 1 opposite
			float tier = c.y < 5f ? 0f : c.y < 15f ? 1f : c.y < 25f ? 2f : 3f;
			lit.Add((i, round * 1.1f + tier * 0.7f));
		}
		if (_fill != null) { _fill.LightColor = new Color(1f, 0.8f, 0.58f); }
		if (_fillHigh != null) { _fillHigh.LightEnergy = 0f; _fillHigh.Visible = true; }
		foreach (var (_, light, _, _, _) in _candles) { light.LightEnergy = 0f; light.Visible = true; }
		var started = new HashSet<int>();
		t = 0;
		const double lighting = 3.6;
		int sounds = 0;
		while (t < lighting + 0.5)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			foreach (var (i, at) in lit)
			{
				var (flame, light, energy, _, _) = _candles[i];
				float u = Mathf.Clamp(((float)t - at) / 0.45f, 0f, 1f);
				if (u <= 0f) continue;
				if (started.Add(i))
				{
					flame.Visible = true;
					if (sounds++ % 2 == 0) AudioDirector.OneShot(this, "candle_ignite", 3, flame.GlobalPosition, -4f, "Events", 4f, 0.1f);
				}
				float e = u * u * (3f - 2f * u);
				flame.Scale = new Vector3(e, e, e);
				light.LightEnergy = energy * e;
			}
			float f = Mathf.Clamp((float)(t / lighting), 0f, 1f);
			if (_fill != null) _fill.LightEnergy = 0.9f * f;
			if (_fillHigh != null) _fillHigh.LightEnergy = 0.7f * Mathf.Clamp(f * 1.4f - 0.4f, 0f, 1f);
			LookToward(player, CentreWorld + Vector3.Up * 1.2f, dt * 0.5f);
		}
		// and the ring: a green light chasing round, its tail longer every lap, until it's all green
		AudioDirector.OneShot(this, "ring_charge", 1, CentreWorld, 0f, "Events", 8f, 0f);
		t = 0;
		const double chase = 4.2;
		float head = 0f;
		while (t < chase)
		{
			await Cutscene.Frame(this, ct);
			float dt = (float)GetProcessDeltaTime();
			t += dt;
			float u = (float)(t / chase);
			head += dt * (0.45f + 0.9f * u);          // about two and a half laps, quickening
			float tail = Mathf.Clamp(0.06f + u * u * 1.0f, 0f, 1f);
			SetRing(head, tail, 1.1f + 0.2f * u);
			LookToward(player, CentreWorld + Vector3.Up * 0.6f, dt * 0.5f);
		}
		SetRing(0f, 1f, 1.2f);
		var settle = CreateTween();
		settle.TweenMethod(Callable.From<float>(v => SetRing(0f, 1f, v)), 1.2f, 1f, 1.2f);
		Powered = true;
		Surging = false;
		StoryManager.Instance?.SetFlag(StoryManager.Flag.RoundRoomPowered);
		GD.Print("[story] Act 20: candlelight, and the ring is green - the dais will rise");
		await Cutscene.Wait(this, 0.6, ct);
	}

	/// <summary>A slow, gentle turn of the view toward a point (the owner's motion comfort: nothing sharp).</summary>
	private static void LookToward(PlayerController player, Vector3 at, float k)
	{
		var rig = player?.CameraRig;
		if (rig?.Camera == null) return;
		Vector3 to = at - rig.Camera.GlobalPosition;
		float yaw = Mathf.Atan2(-to.X, -to.Z);
		float pitch = Mathf.Atan2(to.Y, new Vector2(to.X, to.Z).Length());
		k = Mathf.Clamp(k, 0f, 1f);
		player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.AngleDifference(rig.Yaw, yaw) * k, 0));
		rig.SetPitch(Mathf.Lerp(rig.Pitch, pitch, k));
	}

	/// <summary>Stepping into the middle before the power is back: nothing, and the ring stays red.</summary>
	private void RemarkRed()
	{
		if (Time.GetTicksMsec() - _lastRedRemark < 8000) return;
		_lastRedRemark = Time.GetTicksMsec();
		_ = Subtitle.Instance?.Show("Nothing. The ring stays red.", 0.4f, 2.4f, 0.8f);
	}

	// ------------------------------------------------------------------ for tests

	/// <summary>Where to stand to throw a switch, and the grip to look at.</summary>
	public Vector3 SwitchStandWorld(int i) => _switches[i].Root.ToGlobal(new Vector3(0, -1.3f / SwitchScale, 1.2f / SwitchScale));
	public Vector3 SwitchGripWorld(int i) => _switches[i].Use.GlobalPosition;
}
