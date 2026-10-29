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
/// <see cref="GameSettings.DepthOfField"/> sets how much (off, low, medium, high: 2026-10-29, the friend's playtest, the depth
/// of field "looks horrible": low, the default, is a quarter of the old blur, past the focus only, and starts further back).
/// It's on the camera's own attributes, so it costs nothing elsewhere.
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
		// (a cheap blur, but a round one: the very-low box blur smeared the 360 lines into blocks (2026-10-29))
		RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.Low, false);
		RenderingServer.CameraAttributesSetDofBlurBokehShape(RenderingServer.DofBokehShape.Circle);
	}

	public override void _Process(double delta)
	{
		int level = GameSettings.Instance?.DepthOfField ?? 1;
		bool on = level > 0 && !(GameSettings.Instance?.Trailer ?? false) && _cam.Current;
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
		// the level: low a hint that starts well past the focus, medium half the old blur, high the old look
		float strength = level switch { 1 => 0.25f, 2 => 0.5f, _ => 1f };
		float pushBack = level switch { 1 => 2f, 2 => 1.4f, _ => 1f };
		_attr.DofBlurFarEnabled = true;
		_attr.DofBlurFarDistance = _focus + Mathf.Max(2.5f, _focus * 0.9f) * pushBack;
		_attr.DofBlurFarTransition = Mathf.Max(3f, _focus * 2.5f) * pushBack;
		_attr.DofBlurNearEnabled = _focus < 1.5f && level >= 2;
		_attr.DofBlurNearDistance = _focus * 0.55f;
		_attr.DofBlurNearTransition = _focus * 0.4f;
		// a touch, never a smear: more when the focus is near (a close look), next to none at a distance
		_attr.DofBlurAmount = Mathf.Lerp(0.07f, 0.015f, Mathf.Clamp((_focus - 1f) / 14f, 0f, 1f)) * strength;
	}
}
