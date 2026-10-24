using Godot;

namespace ProjectDS.World;

/// <summary>
/// A strip of duct tape over a seam, cut down its middle with a knife (2026-10-23, the owner: the knife cutting "look
/// horrible ... You can visibly see the knife drag across the tape and the tape being animated to split open in the middle
/// across the knife dragging", with a photo of a cutter run down the tape on a box's seam). Before: the tape just shrank.
///
/// In its own space: the tape runs along +X from 0 to <see cref="Length"/>, <see cref="Width"/> across Y (the seam under
/// its middle, y = 0), its face toward +Z. <see cref="SetProgress"/> moves the cut's front along it; behind the front the
/// tape is parted down the middle, the two halves drawn back from the seam and lifting off it as they curl (more the
/// further behind the blade), a ragged notch at the front; ahead of it the tape lies whole. A knife (<see cref="ShowKnife"/>)
/// rides the front: its point in the seam, laid back along the cut, sawing a little as it goes.
/// </summary>
public partial class TapeStrip : Node3D
{
	[Export] public float Length = 0.3f;
	[Export] public float Width = 0.048f;
	/// <summary>The part of the strip the drag cuts (fractions of its length): its first cut starts at From.</summary>
	[Export] public float CutFrom = 0f, CutTo = 1f;
	public Material TapeMaterial;
	/// <summary>The knife's size (1: a box cutter's own; bigger reads in a close-up).</summary>
	[Export] public float KnifeScale = 1.5f;

	private MeshInstance3D _mi;
	private Node3D _knife;
	private float _front;   // metres along it: cut behind this
	private float _start;   // where the cut began
	private double _clock;

	public float Front => _front;

	public override void _Ready()
	{
		_mi = new MeshInstance3D { Name = "Tape", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild(_mi);
		_start = _front = CutFrom * Length;
		BuildKnife();
		Rebuild();
	}

	/// <summary>0..1 along the cut (<see cref="CutFrom"/> to <see cref="CutTo"/>).</summary>
	public void SetProgress(float p)
	{
		float f = Mathf.Lerp(CutFrom, CutTo, Mathf.Clamp(p, 0f, 1f)) * Length;
		if (Mathf.IsEqualApprox(f, _front)) return;
		_front = f;
		Rebuild();
		PlaceKnife();
	}

	/// <summary>Cut end to end (a Continue past it, or the cut finished: the rest gives).</summary>
	public void CutAll()
	{
		_start = 0f;
		_front = Length + 0.05f;
		Rebuild();
		ShowKnife(false);
	}

	public void ShowKnife(bool on)
	{
		if (_knife == null) return;
		_knife.Visible = on;
		if (on) PlaceKnife();
	}

	public override void _Process(double delta)
	{
		_clock += delta;
		if (_knife is { Visible: true }) PlaceKnife();
	}

	private void Rebuild()
	{
		var k = new MeshKit();
		if (TapeMaterial != null) k.Mat(TapeMaterial);
		k.Color = Colors.White;
		int n = Mathf.Clamp(Mathf.CeilToInt(Length / 0.012f), 4, 120);
		float seg = Length / n, half = Width * 0.5f;
		var up = Vector3.Back;   // (+Z: the face)
		for (int i = 0; i < n; i++)
		{
			float x0 = i * seg, x1 = x0 + seg, xm = (x0 + x1) * 0.5f;
			bool cut = xm < _front && xm > _start - 0.001f;
			if (!cut)
			{
				// the notch: the tape just ahead of the blade pulled into a V toward the point
				float ahead = xm - _front;
				if (ahead >= 0f && ahead < 0.012f && _front > _start + 0.002f)
				{
					float v = (1f - ahead / 0.012f) * 0.004f;
					k.Quad(new Vector3(x0, -half, 0), new Vector3(x1, -half, 0), new Vector3(x1, -v * 0.3f, 0.0005f), new Vector3(x0, -v, 0.0005f), up);
					k.Quad(new Vector3(x0, v, 0.0005f), new Vector3(x1, v * 0.3f, 0.0005f), new Vector3(x1, half, 0), new Vector3(x0, half, 0), up);
					continue;
				}
				k.Quad(new Vector3(x0, -half, 0), new Vector3(x1, -half, 0), new Vector3(x1, half, 0), new Vector3(x0, half, 0), up);
				continue;
			}
			// parted: how far behind the blade, the gap and the lift growing with it
			float behind = Mathf.Clamp((_front - xm) / 0.07f, 0f, 1f);
			float gap = Mathf.Lerp(0.0015f, 0.009f, behind) + 0.0008f * Mathf.Sin(xm * 230f);
			float lift = Mathf.Lerp(0.0008f, 0.007f, behind) * (0.8f + 0.2f * Mathf.Sin(xm * 170f + 1f));
			// each half stuck at its outer edge, its cut edge drawn back and curled up off the seam
			k.Quad(new Vector3(x0, gap, lift), new Vector3(x1, gap, lift), new Vector3(x1, half, 0), new Vector3(x0, half, 0), up);
			k.Quad(new Vector3(x0, -half, 0), new Vector3(x1, -half, 0), new Vector3(x1, -gap, lift), new Vector3(x0, -gap, lift), up);
			// the cut edges (thin walls: the tape's thickness catching the light)
			k.Quad(new Vector3(x0, gap, lift - 0.0006f), new Vector3(x1, gap, lift - 0.0006f), new Vector3(x1, gap, lift), new Vector3(x0, gap, lift), Vector3.Down);
			k.Quad(new Vector3(x0, -gap, lift), new Vector3(x1, -gap, lift), new Vector3(x1, -gap, lift - 0.0006f), new Vector3(x0, -gap, lift - 0.0006f), Vector3.Up);
		}
		_mi.Mesh = k.IsEmpty ? null : k.Commit();
	}

	/// <summary>A box cutter's look: a dark ribbed handle, a short bright blade out of its end.</summary>
	private void BuildKnife()
	{
		_knife = new Node3D { Name = "Knife", Visible = false, Scale = Vector3.One * KnifeScale };
		AddChild(_knife);
		// in the knife's own frame: the point at the origin, the blade back along +X to the handle, its edge down (-Y), its
		// flats facing ±Z
		var k = new MeshKit();
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.91f, 0.94f), Metallic = 0.8f, Roughness = 0.18f, EmissionEnabled = true, Emission = new Color(0.25f, 0.25f, 0.27f) });
		k.Color = Colors.White;
		// the blade: a parallelogram, the point at the origin, its back up and rising to the handle
		const float bl = 0.045f, bh = 0.016f, bt = 0.0012f;
		var a = new Vector3(0, 0, 0); var b = new Vector3(bl, -0.002f, 0); var c = new Vector3(bl, bh, 0); var d = new Vector3(0.012f, 0.006f, 0);
		foreach (float z in new[] { bt, -bt })
			k.Quad(a + Vector3.Back * z, b + Vector3.Back * z, c + Vector3.Back * z, d + Vector3.Back * z, z > 0 ? Vector3.Back : Vector3.Forward);
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.62f, 0.08f), Roughness = 0.55f });
		k.Box(new Vector3(bl + 0.055f, 0.007f, 0), new Vector3(0.11f, 0.024f, 0.017f));
		k.Mat(new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.12f, 0.13f), Roughness = 0.6f });
		k.Box(new Vector3(bl + 0.02f, 0.007f, 0), new Vector3(0.022f, 0.026f, 0.019f));
		k.CommitTo(_knife, "Mesh", false);
	}

	private void PlaceKnife()
	{
		if (_knife == null) return;
		// laid back along the cut at 40 degrees, the point a few mm into the seam at the front; a little sawing
		float saw = 0.003f * Mathf.Sin((float)_clock * 14f);
		var at = new Vector3(_front + saw, 0f, -0.002f);
		var back = new Vector3(-Mathf.Cos(Mathf.DegToRad(40f)), 0f, Mathf.Sin(Mathf.DegToRad(40f)));   // toward the handle
		var x = back.Normalized();
		var y = new Vector3(Mathf.Sin(Mathf.DegToRad(40f)), 0f, Mathf.Cos(Mathf.DegToRad(40f)));          // the blade's back up off the tape
		var z = x.Cross(y).Normalized();
		_knife.Transform = new Transform3D(new Basis(x, y, z), at);
	}
}
