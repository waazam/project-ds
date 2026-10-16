using System;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-15, Dan: "all doors should be closed before entering and they always open to the next area, not a black
/// room") A doorway that looks into the place it leads to when that place is built somewhere else (the bunker's
/// hallway, the sewer, the station's lobby): a second camera stands at <see cref="Target"/> exactly where the eye
/// would be had the doorway been cut there, and what it sees is drawn into the opening. The real rooms, their own
/// lamps and the world's fog, not a black box.
///
/// Local frame (this node and <see cref="Target"/> alike): the opening's foot at the origin, the viewer on +Z looking
/// toward -Z through a <see cref="Size"/> rectangle. Whatever stands just behind the far opening (that side's shut
/// door, the wall it's set in) goes on <see cref="HiddenLayer"/>: the main view still draws it, this camera doesn't.
/// It only renders while <see cref="On"/>, the eye is in front of it and within <see cref="Range"/>, and the opening
/// is on screen; the rest of the time it costs nothing.
/// </summary>
public partial class DoorPortal : Node3D
{
	/// <summary>Render layer of what the far camera must not see (the far side's door and the wall round it).</summary>
	public const uint HiddenLayer = 1u << 15;

	/// <summary>The far opening (same frame as this node's).</summary>
	public Node3D Target { get; set; }
	/// <summary>Width and height of the opening.</summary>
	public Vector2 Size { get; set; } = new(1f, 2.1f);
	/// <summary>Draws only while true (a door that is open; a hallway that is still there).</summary>
	public bool On { get; set; } = true;
	public float Range { get; set; } = 45f;
	/// <summary>Optional: asked every frame for <see cref="On"/> (a door's state kept by someone else).</summary>
	public Func<bool> OnWhen { get; set; }
	/// <summary>Optional: asked every frame for <see cref="Target"/> (where it leads can change with the story).</summary>
	public Func<Node3D> TargetWhen { get; set; }
	/// <summary>Raised when the eye passes through the opening (front to back, inside its rectangle).</summary>
	public event Action<PlayerController> Crossed;

	/// <summary>For tests and the shots: it drew this frame.</summary>
	public bool Rendering { get; private set; }
	/// <summary>Where the far camera stands now (world), e.g. for lamps that light only near the eye.</summary>
	public Vector3 FarEye => _cam?.GlobalPosition ?? Vector3.Zero;

	private SubViewport _vp;
	private Camera3D _cam;
	private MeshInstance3D _quad;
	private Godot.Environment _env, _envSource;
	private int _envAge = 1000;
	private float _lastSide = 1f;
	private static Shader _shader;

	private const string ShaderCode = @"shader_type spatial;
render_mode unshaded, cull_back, fog_disabled, shadows_disabled;
uniform sampler2D view : filter_linear, repeat_disable;
void fragment() {
	ALBEDO = texture(view, SCREEN_UV).rgb;
}
";

	public override void _Ready()
	{
		ProcessPriority = 1000;   // after the player's camera has moved this frame
		_vp = new SubViewport
		{
			Name = "View", RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, UseHdr2D = true,
			HandleInputLocally = false, GuiDisableInput = true, AudioListenerEnable3D = false, PositionalShadowAtlasSize = 2048,
		};
		AddChild(_vp);
		_cam = new Camera3D { Name = "Eye", Attributes = new CameraAttributesPractical { AutoExposureEnabled = false } };
		_vp.AddChild(_cam);
		_cam.Current = true;
		_shader ??= new Shader { Code = ShaderCode };
		var mat = new ShaderMaterial { Shader = _shader };
		mat.SetShaderParameter("view", _vp.GetTexture());
		_quad = new MeshInstance3D
		{
			Name = "Opening", Mesh = new QuadMesh { Size = Size, CenterOffset = new Vector3(0, Size.Y * 0.5f, 0) }, MaterialOverride = mat,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled, Visible = false,
		};
		AddChild(_quad);
	}

	public override void _Process(double delta)
	{
		if (OnWhen != null) On = OnWhen();
		if (TargetWhen != null) Target = On ? TargetWhen() : null;
		var main = GetViewport()?.GetCamera3D();
		bool want = On && Target != null && IsInstanceValid(Target) && Target.IsInsideTree() && main != null && IsVisibleInTree() && main != _cam;
		Vector3 eyeLocal = want ? ToLocal(main.GlobalPosition) : Vector3.Zero;
		if (want) CheckCrossing(eyeLocal);
		want = want && eyeLocal.Z > -0.02f && eyeLocal.Length() < Range && OnScreen(main);
		if (want != Rendering)
		{
			Rendering = want;
			_quad.Visible = want;
			_vp.RenderTargetUpdateMode = want ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
		}
		if (!want) return;
		Follow(main);
	}

	/// <summary>The far camera takes the eye's place relative to the far opening, with the eye's lens.</summary>
	private void Follow(Camera3D main)
	{
		var root = GetViewport();
		var size = (Vector2I)root.GetVisibleRect().Size;
		if (_vp.Size != size) _vp.Size = size;
		_vp.Scaling3DMode = root.Scaling3DMode;
		_vp.Scaling3DScale = root.Scaling3DScale;
		_vp.Msaa3D = root.Msaa3D;
		_vp.ScreenSpaceAA = root.ScreenSpaceAA;
		_vp.UseTaa = root.UseTaa;
		_cam.GlobalTransform = Map * main.GlobalTransform;
		_cam.Fov = main.Fov;
		_cam.KeepAspect = main.KeepAspect;
		_cam.Far = main.Far;
		_cam.CullMask = main.CullMask & ~(HiddenLayer | HeldItem.HeldLayer | PlayerBody.BodyLayer);
		// The near plane just short of the opening's nearest corner: nothing on this side of the far wall gets drawn.
		var inv = main.GlobalTransform.AffineInverse();
		float hw = Size.X * 0.5f, near = float.MaxValue;
		foreach (var c in new[] { new Vector3(-hw, 0, 0), new Vector3(hw, 0, 0), new Vector3(-hw, Size.Y, 0), new Vector3(hw, Size.Y, 0) })
			near = Mathf.Min(near, -(inv * ToGlobal(c)).Z);
		_cam.Near = Mathf.Clamp(near - 0.03f, 0.02f, 50f);
		SyncEnvironment(main);
	}

	/// <summary>The world's own sky, fog and light (followed as the mood eases), but untoned: the opening is drawn into the
	/// main view, and that tones it once like everything else.</summary>
	private void SyncEnvironment(Camera3D main)
	{
		var src = main.Environment ?? GetWorld3D()?.Environment ?? GetWorld3D()?.FallbackEnvironment;
		if (src == null) { _cam.Environment = null; return; }
		if (src != _envSource || ++_envAge >= 8)
		{
			_envSource = src;
			_envAge = 0;
			_env = (Godot.Environment)src.Duplicate();
			_env.TonemapMode = Godot.Environment.ToneMapper.Linear;
			_env.TonemapExposure = 1f;
			_env.TonemapWhite = 1f;
			_env.GlowEnabled = false;
			_env.AdjustmentEnabled = false;
			_cam.Environment = _env;
		}
	}

	/// <summary>From this side of the opening to the far side (world to world).</summary>
	public Transform3D Map => Target.GlobalTransform * GlobalTransform.AffineInverse();

	/// <summary>Puts the player through: the same place, heading and speed relative to the far opening.</summary>
	public void Carry(PlayerController player, float clear = 0f)
	{
		if (player == null || Target == null) return;
		var map = Map;
		Vector3 vel = map.Basis * player.Velocity;
		Vector3 at = map * player.GlobalPosition;
		// clear of the far door's wall: the body's radius past the opening, never inside the wall behind it
		float z = Target.ToLocal(at).Z;
		if (clear > 0f && z > -clear) at = Target.ToGlobal(Target.ToLocal(at) with { Z = -clear });
		float turn = Target.GlobalRotation.Y - GlobalRotation.Y;
		player.Teleport(at, player.CameraRig.Yaw + turn);
		player.Velocity = vel;
		if (_cam != null) _cam.GlobalTransform = map * _cam.GlobalTransform;
	}

	private void CheckCrossing(Vector3 eyeLocal)
	{
		float side = eyeLocal.Z;
		bool inside = Mathf.Abs(eyeLocal.X) < Size.X * 0.5f + 0.1f && eyeLocal.Y > -0.5f && eyeLocal.Y < Size.Y + 0.3f;
		if (_lastSide >= 0f && side < 0f && inside && Crossed != null && StoryBeat.Player(this) is { } p)
		{
			_lastSide = side;
			Crossed.Invoke(p);
			return;
		}
		_lastSide = side;
	}

	/// <summary>Whether any of the opening is inside the eye's frustum (or the eye is right at it).</summary>
	private bool OnScreen(Camera3D main)
	{
		float hw = Size.X * 0.5f;
		Vector3[] pts =
		{
			ToGlobal(new Vector3(-hw, 0, 0)), ToGlobal(new Vector3(hw, 0, 0)),
			ToGlobal(new Vector3(-hw, Size.Y, 0)), ToGlobal(new Vector3(hw, Size.Y, 0)),
		};
		if (main.GlobalPosition.DistanceTo(ToGlobal(new Vector3(0, Size.Y * 0.5f, 0))) < Size.Length()) return true;
		foreach (var plane in main.GetFrustum())
		{
			bool allOut = true;
			foreach (var p in pts) if (!plane.IsPointOver(p)) { allOut = false; break; }
			if (allOut) return false;
		}
		return true;
	}

	/// <summary>Puts a node and everything visual under it on <see cref="HiddenLayer"/> only.</summary>
	public static void Hide(Node n)
	{
		if (n == null) return;
		if (n is VisualInstance3D v && n is not Light3D) v.Layers = HiddenLayer;
		foreach (var c in n.GetChildren()) Hide(c);
	}
}
