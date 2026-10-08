using Godot;

namespace ProjectDS.Player;

/// <summary>
/// Something held in the hand and seen (the lantern first; the compass and the lighter to follow, 2026-10-07): a node on
/// the camera that's raised into view when it's wanted and lowered out of it when not (eased, half a second each way),
/// bobbing with the stride and lagging a touch behind the view's turns, so it has weight. Each kind says where it's held
/// (<see cref="Rest"/>, <see cref="Lowered"/>), when it's wanted, and animates itself on top.
/// </summary>
public abstract partial class HeldItem : Node3D
{
	protected PlayerController Player { get; private set; }
	/// <summary>0 lowered out of view .. 1 up where it's held.</summary>
	public float Raise { get; private set; }
	/// <summary>Where it's held right now (a kind may move it: the lantern lifted to gather its beam).</summary>
	protected Vector3 Hold;
	protected abstract Vector3 Rest { get; }
	protected abstract Vector3 Lowered { get; }
	protected abstract bool Wanted { get; }
	protected abstract void Build();
	protected abstract void Animate(float dt);
	protected virtual void OnHidden() { }

	private float _stride, _lagYaw, _lagPitch, _prevYaw, _prevPitch;
	private bool _built;

	/// <summary>The camera's viewfinder up to the eye (the hands are busy with it).</summary>
	protected bool CameraUp => Player?.GetNodeOrNull<CameraTool>("CameraTool") is { IsRaised: true };

	public static T Attach<T>(PlayerController player) where T : HeldItem, new()
	{
		var cam = player.CameraRig?.Camera;
		if (cam == null) return null;
		var item = new T { Name = typeof(T).Name };
		item.Player = player;
		cam.AddChild(item);
		return item;
	}

	public override void _Process(double delta)
	{
		if (Player == null) return;
		if (!_built) { _built = true; Build(); Hold = Rest; Visible = false; }
		float dt = Mathf.Min((float)delta, 0.05f);
		bool want = Wanted;
		Raise = Mathf.MoveToward(Raise, want ? 1f : 0f, dt / 0.5f);
		bool show = Raise > 0.01f;
		if (Visible != show) { Visible = show; if (!show) OnHidden(); }
		if (!show) return;
		Animate(dt);
		float r = Mathf.SmoothStep(0f, 1f, Raise);
		// the stride: up and down twice a cycle, side to side once; less crouched, none standing still
		float speed = Player.IsOnFloor() ? Player.GroundSpeed : 0f;
		_stride += dt * speed * 1.35f;
		float amp = Mathf.Clamp(speed / 3.5f, 0f, 1f) * (1f - 0.5f * Player.CrouchAmount);
		var bob = new Vector3(Mathf.Sin(_stride * Mathf.Pi) * 0.012f, -Mathf.Abs(Mathf.Cos(_stride * Mathf.Pi)) * 0.018f, 0f) * amp;
		// a touch of lag behind the view's turns (it has weight), eased back
		var rig = Player.CameraRig;
		float yaw = rig?.Yaw ?? 0f, pitch = rig?.Camera?.Rotation.X ?? 0f;
		_lagYaw = Mathf.Lerp(_lagYaw + Mathf.AngleDifference(yaw, _prevYaw) * 0.6f, 0f, 1f - Mathf.Exp(-dt * 9f));
		_lagPitch = Mathf.Lerp(_lagPitch + (_prevPitch - pitch) * 0.6f, 0f, 1f - Mathf.Exp(-dt * 9f));
		_prevYaw = yaw; _prevPitch = pitch;
		_lagYaw = Mathf.Clamp(_lagYaw, -0.08f, 0.08f); _lagPitch = Mathf.Clamp(_lagPitch, -0.06f, 0.06f);
		Position = Lowered.Lerp(Hold, r) + bob + new Vector3(_lagYaw * 0.35f, _lagPitch * 0.3f, 0f);
		Rotation = new Vector3(_lagPitch * 0.5f, _lagYaw * 0.6f, 0f);
	}

	/// <summary>The render layer the held things are on (out of the lantern's own light: see <see cref="SetLayer"/>).</summary>
	public const uint HeldLayer = 1u << 19;

	/// <summary>Puts everything drawn under <paramref name="n"/> on the held layer only.</summary>
	protected static void SetLayer(Node n)
	{
		if (n is VisualInstance3D v && n is not Light3D) v.Layers = HeldLayer;
		foreach (var c in n.GetChildren()) SetLayer(c);
	}

	/// <summary>A gloved fist (knuckles, a thumb round the front) and the cuff of a sleeve going down and back out of
	/// view, at <paramref name="at"/> (the grip).</summary>
	protected static void BuildFist(Node3D parent, Vector3 at, Color glove, Color sleeve)
	{
		var k = new World.MeshKit();
		// (matte, no sheen: lit from beside, a glossy sleeve showed a pale streak and read as a pole)
		k.Mat(new StandardMaterial3D { ResourceName = "glove", AlbedoColor = glove, Roughness = 1f, MetallicSpecular = 0f });
		k.Color = Colors.White;
		k.Blob(at + new Vector3(0, 0.005f, 0), new Vector3(0.042f, 0.036f, 0.034f), 4101, 0.12f, true, 1f);
		for (int i = 0; i < 4; i++)
			k.Blob(at + new Vector3(-0.024f + i * 0.016f, 0.0f, -0.03f), new Vector3(0.011f, 0.014f, 0.012f), 4110 + i, 0.08f, true, 1f);
		k.Blob(at + new Vector3(0.03f, 0.012f, -0.018f), new Vector3(0.012f, 0.022f, 0.012f), 4120, 0.08f, true, 1f);
		k.Mat(new StandardMaterial3D { ResourceName = "sleeve", AlbedoColor = sleeve * 0.6f, Roughness = 1f, MetallicSpecular = 0f });
		// (the wrist and the forearm dropping steeply out of the bottom of the view: a long one, straight back, read as a pole)
		k.Cylinder(at + new Vector3(-0.004f, 0.012f, 0.022f), at + new Vector3(-0.02f, -0.1f, 0.06f), 0.028f, 0.034f, 8, true);
		k.Cylinder(at + new Vector3(-0.02f, -0.1f, 0.06f), at + new Vector3(-0.04f, -0.36f, 0.1f), 0.034f, 0.04f, 8, true);
		var mi = k.CommitTo(parent, "Hand", false);
		mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
	}
}
