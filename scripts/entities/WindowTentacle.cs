using System.Collections.Generic;
using Godot;
using ProjectDS.World;

namespace ProjectDS.Entities;

/// <summary>
/// One limb of the thing in the lake (<see cref="LakeCreature"/>, made the same way with
/// <see cref="TentacleKit"/>: grimy octopus flesh, ring suckers, bloodshot eyes that turn to stare at
/// the camera, and the toothed maw at its tip), reaching in through a window. Act 13, room 2 (the owner):
/// it is what breaks the window - it presses against the glass, bursts through, writhes in the room for
/// a moment, and drags itself back out.
///
/// Built along its own +Y (the joints' axis). The owner places it so +Y points in through the window,
/// with its root far enough outside that all of it starts hidden behind the wall;
/// <see cref="Reach"/> (0 = all outside, 1 = <see cref="ReachMetres"/> of it through the opening) slides
/// it along that axis, and <see cref="Writhe"/> sets how much the travelling wave bends its joints.
/// </summary>
public partial class WindowTentacle : Node3D
{
	public const int Segments = 12;
	[Export] public float Length = 5.2f;
	[Export] public float BaseRadius = 0.36f;
	[Export] public float ReachMetres = 2.4f;
	[Export] public int Seed = 1313;

	/// <summary>0 (all of it outside) .. 1 (<see cref="ReachMetres"/> in), eased by the owner.</summary>
	public float Reach { get; set; }
	/// <summary>How hard it writhes (0 still .. 1 thrashing).</summary>
	public float Writhe { get; set; }
	/// <summary>A point (world) the tip curls toward while it is in.</summary>
	public Vector3? Aim { get; set; }

	private Node3D _root;
	private readonly List<Node3D> _joints = new();
	private readonly List<Node3D> _eyes = new();
	private RandomNumberGenerator _rng;
	private double _t;
	private float _segLen;

	public override void _Ready()
	{
		_rng = new RandomNumberGenerator { Seed = (ulong)Seed };
		Build();
	}

	private static float Radius(float baseR, float f) => Mathf.Lerp(baseR, Mathf.Max(0.07f, baseR * 0.32f), Mathf.Pow(f, 1.5f));

	private void Build()
	{
		var skin = TentacleKit.Flesh(null, 1.1f);
		_root = new Node3D { Name = "Limb" };
		AddChild(_root);
		_segLen = Length / Segments;
		Node3D parent = _root;
		for (int i = 0; i < Segments; i++)
		{
			float f0 = (float)i / Segments, f1 = (float)(i + 1) / Segments;
			float r0 = Radius(BaseRadius, f0), r1 = Radius(BaseRadius, f1);
			var joint = new Node3D { Name = $"J{i}", Position = i == 0 ? Vector3.Zero : Vector3.Up * _segLen };
			parent.AddChild(joint);
			var k = new MeshKit();
			k.Mat(skin);
			k.Color = Colors.White;
			k.Cylinder(Vector3.Zero, Vector3.Up * _segLen, r0, r1, 12, false, 1.5f);
			k.Blob(Vector3.Up * _segLen, Vector3.One * r1 * 1.04f, i * 7 + 3, 0.05f, false);
			if (i < Segments - 1) TentacleKit.Suckers(k, _segLen, r0, r1, 2);
			k.CommitTo(joint, "Seg", false);
			_joints.Add(joint);
			parent = joint;
		}
		TentacleKit.Maw(_joints[Segments - 1], Radius(BaseRadius, 1f), BaseRadius * 1.4f, skin, Seed).Position = Vector3.Up * _segLen;
		// eyes up the part that comes in, crowded toward its thick end, never on the sucker side (+Z)
		for (int e = 0; e < 9; e++)
		{
			float f = _rng.RandfRange(0.3f, 0.85f);
			int seg = Mathf.Clamp((int)(f * Segments), 0, Segments - 1);
			float r = Radius(BaseRadius, f);
			float ang = Mathf.Pi * 0.5f + _rng.RandfRange(0.8f, Mathf.Tau - 0.8f);
			var socket = new Node3D { Name = $"Eye{e}", Position = new Vector3(Mathf.Cos(ang) * r, (f * Segments - seg) * _segLen, Mathf.Sin(ang) * r) };
			_joints[seg].AddChild(socket);
			LakeCreature.BuildEye(socket);
			socket.Scale = Vector3.One * Mathf.Min(r * 0.55f, TentacleKit.EyeSize(BaseRadius * 0.5f, BaseRadius * 0.2f, f));
			_eyes.Add(socket);
		}
		Visible = false;
	}

	public override void _Process(double delta)
	{
		if (!Visible) return;
		_t += delta;
		// slide along its axis: at Reach 0 the tip is just outside the root's far end of the window
		_root.Position = Vector3.Up * (Mathf.Clamp(Reach, 0f, 1.2f) * ReachMetres - Length);
		float w = Writhe;
		for (int i = 0; i < _joints.Count; i++)
		{
			float f = (float)i / Segments;
			// a wave travelling down it, stronger toward the tip; a lazy curl toward the sucker side
			// (every joint's bend adds to the ones before it, so each is small: the whole limb curls by ~30 degrees)
			float wave = Mathf.Sin((float)_t * 7.5f - i * 0.8f) * (0.015f + 0.05f * f) * w;
			float side = Mathf.Cos((float)_t * 5.3f - i * 0.6f) * (0.012f + 0.04f * f) * w;
			_joints[i].Rotation = new Vector3(wave, 0, side);
		}
		// the tip turns toward the aim (the player), a little
		if (Aim is Vector3 aim && _joints.Count > 2)
		{
			var tip = _joints[^2];
			Vector3 to = tip.GlobalPosition.DirectionTo(aim);
			Vector3 along = tip.GlobalBasis.Y.Normalized();
			Vector3 axis = along.Cross(to);
			if (axis.LengthSquared() > 1e-5f)
			{
				float ang = Mathf.Min(along.AngleTo(to), 0.6f) * 0.5f * Mathf.Clamp(Reach, 0f, 1f);
				tip.GlobalBasis = new Basis(axis.Normalized(), ang) * tip.GlobalBasis;
			}
		}
		// every eye stares at the camera
		var cam = GetViewport().GetCamera3D();
		if (cam != null)
			foreach (var eye in _eyes)
			{
				Vector3 d = cam.GlobalPosition - eye.GlobalPosition;
				if (d.LengthSquared() < 1e-4f) continue;
				var s = eye.Scale;
				eye.GlobalBasis = Basis.LookingAt(d.Normalized(), Vector3.Up).Scaled(s.X * Vector3.One);
			}
	}
}
