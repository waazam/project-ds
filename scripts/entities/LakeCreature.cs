using System.Collections.Generic;
using Godot;
using ProjectDS.World;

namespace ProjectDS.Entities;

/// <summary>
/// Act 12's lake creature: never a body, just tentacles - thick, wet limbs studded with clusters
/// of small bloodshot eyeballs - that breach the surface around the boat, thrash for a few
/// seconds, and sink again. Built entirely from MeshKit primitives (chained tapering cylinders per
/// limb, jittered blobs for the eyes), the same way every creature in this game is built.
///
/// <see cref="Breach"/> rises it around a world point; it thrashes for the given hold time, sinks,
/// and frees itself. Rising/sinking is a uniform scale from the tentacle's own root (at the water
/// line) toward its tip, which reads as growing out of / sinking back into the water without
/// needing a clip plane on the water shader.
/// </summary>
public partial class LakeCreature : Node3D
{
	[Export] public int TentacleCount = 5;
	[Export] public float MaxHeight = 6.5f;
	[Export] public float SpreadRadius = 4.5f;
	[Export] public float RiseSeconds = 2.2f;
	[Export] public float SinkSeconds = 2.2f;
	[Export] public int Seed = 6013;

	/// <summary>For tests: true from the moment it starts rising until it has fully sunk.</summary>
	public bool Breaching => _phase != Phase.Gone;
	/// <summary>For tests: 0 (submerged) to 1 (fully risen).</summary>
	public float Life { get; private set; }

	private readonly List<Node3D> _tentacles = new();
	private enum Phase { Gone, Rising, Holding, Sinking }
	private Phase _phase = Phase.Gone;
	private double _t, _holdSeconds;

	public override void _Ready()
	{
		Build();
		Visible = false;
	}

	private void Build()
	{
		var rng = new RandomNumberGenerator { Seed = (ulong)Seed };
		for (int i = 0; i < TentacleCount; i++)
		{
			float a = Mathf.Tau * i / TentacleCount + rng.RandfRange(-0.25f, 0.25f);
			float radius = rng.RandfRange(0.8f, SpreadRadius);
			var root = new Node3D { Name = $"Tentacle{i}", Position = new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius) };
			AddChild(root);
			BuildTentacle(root, rng, rng.RandfRange(0.8f, 1.3f), rng.RandiRange(2, 4));
			_tentacles.Add(root);
		}
	}

	private void BuildTentacle(Node3D root, RandomNumberGenerator rng, float heightScale, int eyeCount)
	{
		var k = new MeshKit();
		var skin = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.09f, 0.07f), Roughness = 0.2f, Metallic = 0.15f };
		k.Mat(skin);
		const int segs = 7;
		var pts = new List<Vector3>();
		float curl = rng.RandfRange(0.5f, 1.0f) * (rng.Randf() < 0.5f ? 1f : -1f);
		float lean = rng.RandfRange(-0.25f, 0.25f);
		for (int i = 0; i <= segs; i++)
		{
			float t = (float)i / segs;
			float h = t * MaxHeight * heightScale;
			float bend = curl * t * t;
			pts.Add(new Vector3(Mathf.Sin(bend) * h * 0.3f + lean * h, h, Mathf.Cos(bend * 0.7f) * h * 0.2f - h * 0.15f));
		}
		for (int i = 0; i < segs; i++)
		{
			float t0 = (float)i / segs, t1 = (float)(i + 1) / segs;
			float r0 = Mathf.Lerp(0.5f, 0.08f, t0), r1 = Mathf.Lerp(0.5f, 0.08f, t1);
			k.Color = new Color(0.04f + 0.03f * t0, 0.08f + 0.02f * t0, 0.05f);
			k.Cylinder(pts[i], pts[i + 1], r0, r1, 7, i == 0, 1f);
		}
		k.Color = Colors.White;
		k.CommitTo(root, "TentacleMesh", false);

		var eyeMat = new StandardMaterial3D { AlbedoColor = new Color(0.82f, 0.78f, 0.74f), Roughness = 0.4f };
		var irisMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.5f, 0.02f, 0.02f),
			EmissionEnabled = true,
			Emission = new Color(0.9f, 0.05f, 0.03f),
			EmissionEnergyMultiplier = 1.6f,
		};
		var veinMat = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.05f, 0.05f) };
		for (int e = 0; e < eyeCount; e++)
		{
			int seg = Mathf.Clamp(1 + e * (segs - 2) / Mathf.Max(1, eyeCount), 1, segs - 1);
			Vector3 basePos = pts[seg];
			float ang = rng.RandfRange(0f, Mathf.Tau);
			Vector3 outDir = new Vector3(Mathf.Cos(ang), 0.15f, Mathf.Sin(ang)).Normalized();
			float r0 = Mathf.Lerp(0.5f, 0.08f, (float)seg / segs);
			Vector3 eyePos = basePos + outDir * (r0 + 0.05f);
			float eyeR = rng.RandfRange(0.08f, 0.15f);

			var eb = new MeshKit { Xf = new Transform3D(Basis.Identity, eyePos) };
			eb.Mat(eyeMat);
			eb.Color = Colors.White;
			eb.Blob(Vector3.Zero, Vector3.One * eyeR, Seed + e * 7 + seg, 0.08f, false);
			eb.Mat(irisMat);
			eb.Blob(outDir * eyeR * 0.65f, Vector3.One * eyeR * 0.45f, Seed + e * 13 + seg, 0.1f, false);
			eb.Mat(veinMat);
			for (int v = 0; v < 3; v++)
			{
				float va = rng.RandfRange(0f, Mathf.Tau);
				Vector3 vd = new Vector3(Mathf.Cos(va), Mathf.Sin(va) * 0.6f, 0.4f).Normalized();
				eb.Cylinder(vd * eyeR * 0.1f, vd * eyeR * 0.9f, 0.006f, 0.002f, 3, false);
			}
			eb.Color = Colors.White;
			eb.CommitTo(root, $"Eye{e}", false);
		}
	}

	/// <summary>Rises around <paramref name="center"/> (world), thrashes for <paramref name="holdSeconds"/>,
	/// then sinks and frees itself. Fires once; call on a fresh instance each breach.</summary>
	public void Breach(Vector3 center, double holdSeconds)
	{
		GlobalPosition = center;
		Visible = true;
		_phase = Phase.Rising;
		_t = 0;
		_holdSeconds = holdSeconds;
	}

	public override void _Process(double delta)
	{
		if (_phase == Phase.Gone) return;
		_t += delta;
		switch (_phase)
		{
			case Phase.Rising:
				Life = Mathf.Min(1f, (float)(_t / Mathf.Max(RiseSeconds, 0.01f)));
				if (Life >= 1f) { _phase = Phase.Holding; _t = 0; }
				break;
			case Phase.Holding:
				Life = 1f;
				if (_t >= _holdSeconds) { _phase = Phase.Sinking; _t = 0; }
				break;
			case Phase.Sinking:
				Life = Mathf.Max(0f, 1f - (float)(_t / Mathf.Max(SinkSeconds, 0.01f)));
				if (Life <= 0f) { _phase = Phase.Gone; Visible = false; QueueFree(); return; }
				break;
		}
		float sway = (float)(_t + Life * 3.0);
		for (int i = 0; i < _tentacles.Count; i++)
		{
			var root = _tentacles[i];
			float phase = i * 1.7f;
			root.Scale = Vector3.One * Mathf.Max(0.03f, Life);
			root.Rotation = new Vector3(
				Mathf.Sin(sway * 1.3f + phase) * 0.14f * Life,
				0f,
				Mathf.Sin(sway * 1.6f + phase * 1.3f) * 0.16f * Life);
		}
	}
}
