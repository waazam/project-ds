using Godot;
using ProjectDS.Systems;

namespace ProjectDS.Player;

/// <summary>
/// The lens pulls focus (2026-10-10c, the cinematic brief: "dynamic depth-of-field (DoF) or smart focus pulling based on
/// player proximity to objects of interest"). A ray down the middle of the view finds what's being looked at; the focus
/// eases onto it the way a focus puller's hand does (quick to close, slower to rack out, never a snap), and beyond it the
/// picture softens a little, more the nearer the focus: lean in to a note on a table and the room behind it goes soft;
/// look down a long corridor and almost nothing does. Up close (under a metre and a half, picking something up, reading),
/// what's nearer than it softens too. Gentle always: the far blur never more than a touch, so the dark still reads.
/// <see cref="GameSettings.FocusPull"/> turns it off. It's on the camera's own attributes, so it costs nothing elsewhere.
/// </summary>
public partial class FocusPull : Node
{
	private Camera3D _cam;
	private CameraAttributesPractical _attr;
	private float _focus = 8f;
	private PhysicsRayQueryParameters3D _q;

	public static void Attach(Camera3D cam)
	{
		if (cam == null || cam.GetNodeOrNull("FocusPull") != null) return;
		cam.AddChild(new FocusPull { Name = "FocusPull", _cam = cam });
	}

	public override void _Ready()
	{
		_attr = _cam.Attributes as CameraAttributesPractical ?? new CameraAttributesPractical();
		_cam.Attributes = _attr;
		// (the cheapest blur: it's only ever a touch, and it must cost next to nothing)
		RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.VeryLow, false);
		RenderingServer.CameraAttributesSetDofBlurBokehShape(RenderingServer.DofBokehShape.Box);
	}

	public override void _Process(double delta)
	{
		bool on = (GameSettings.Instance?.FocusPull ?? true) && !(GameSettings.Instance?.Trailer ?? false) && _cam.Current;
		if (!on)
		{
			_attr.DofBlurFarEnabled = false;
			_attr.DofBlurNearEnabled = false;
			return;
		}
		float dt = (float)delta;
		var from = _cam.GlobalPosition;
		var dir = -_cam.GlobalBasis.Z;
		_q ??= PhysicsRayQueryParameters3D.Create(Vector3.Zero, Vector3.Zero, 1);
		_q.From = from;
		_q.To = from + dir * 60f;
		var hit = _cam.GetWorld3D().DirectSpaceState.IntersectRay(_q);
		float want = hit.Count > 0 ? Mathf.Max(0.35f, from.DistanceTo((Vector3)hit["position"])) : 60f;
		// (quick onto something near, slower out to the distance: a focus puller's hand)
		float rate = want < _focus ? 7f : 2.2f;
		_focus = Mathf.Lerp(_focus, want, 1f - Mathf.Exp(-dt * rate));
		_attr.DofBlurFarEnabled = true;
		_attr.DofBlurFarDistance = _focus + Mathf.Max(2.5f, _focus * 0.9f);
		_attr.DofBlurFarTransition = Mathf.Max(3f, _focus * 2.5f);
		_attr.DofBlurNearEnabled = _focus < 1.5f;
		_attr.DofBlurNearDistance = _focus * 0.55f;
		_attr.DofBlurNearTransition = _focus * 0.4f;
		// a touch, never a smear: more when the focus is near (a close look), next to none at a distance
		_attr.DofBlurAmount = Mathf.Lerp(0.07f, 0.015f, Mathf.Clamp((_focus - 1f) / 14f, 0f, 1f));
	}
}
