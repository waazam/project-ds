using Godot;
using ProjectDS.World;

namespace ProjectDS.Player;

/// <summary>
/// The tool seen doing its job (2026-10-11, the owner's list: "Hand-offs in cutscenes: when a keycard or a crowbar is used,
/// show it in the hand doing the action"). Brought up in the right hand for the moment it's used, toward what it's used on,
/// and put away again:
/// <list type="bullet">
/// <item><b>a keycard</b> held up to the reader and swiped down through it;</item>
/// <item><b>a key</b> pushed in and turned a quarter;</item>
/// <item><b>the crowbar</b> worked in under the lid and levered down, once for each nail;</item>
/// <item><b>the lighter</b> raised and lit, held to the wick or the web a moment;</item>
/// <item><b>the knife</b> drawn across, twice;</item>
/// <item>anything else (a chalice, a bookmark): reached out and let go.</item>
/// </list>
/// The tool is built here from its own mesh (<see cref="ItemMeshes"/>), so it shows even when the use spends it.
/// </summary>
public partial class HeldUse : HeldItem
{
	protected override Vector3 Rest => new(0.2f, -0.2f, -0.46f);
	protected override Vector3 Lowered => new(0.3f, -0.78f, -0.42f);
	protected override bool Wanted => _t < _len;

	public static bool Busy { get; private set; }
	/// <summary>For tests: what was last used, and how many uses have shown.</summary>
	public static ToolKind Last { get; private set; }
	public static int Shown { get; private set; }

	private enum Motion { Reach, Swipe, Turn, Prise, Flick, Cut }

	private Node3D _pivot, _tool;
	private OmniLight3D _flame;
	private MeshInstance3D _flameMesh;
	private Motion _motion;
	private ToolKind _kind = ToolKind.None;
	private Vector3 _reach;   // (camera space: toward what it's used on, a little)
	private float _t = 99f, _len;
	private int _strokes = 3;

	/// <summary>Shows <paramref name="kind"/> used on <paramref name="at"/> (a world point). <paramref name="seconds"/>, if
	/// given, is how long the use lasts (the crowbar's three nails); otherwise the motion's own.</summary>
	public static void Do(PlayerController player, ToolKind kind, Vector3 at, float seconds = 0f)
	{
		var cam = player?.CameraRig?.Camera;
		if (cam == null || kind == ToolKind.None) return;
		var u = cam.GetNodeOrNull<HeldUse>(nameof(HeldUse)) ?? Attach<HeldUse>(player);
		u?.Begin(kind, at, seconds);
	}

	protected override void Build()
	{
		_pivot = new Node3D { Name = "Pivot" };
		AddChild(_pivot);
		BuildFist(_pivot, Vector3.Zero, new Color(0.09f, 0.075f, 0.065f), new Color(0.07f, 0.075f, 0.07f));
		AddChild(new OmniLight3D { Name = "HandLight", LightColor = new Color(1f, 0.8f, 0.6f), LightEnergy = 0.35f, OmniRange = 0.6f, ShadowEnabled = false, LightCullMask = HeldLayer, Position = new Vector3(-0.1f, 0.1f, 0.1f) });
		SetLayer(this);
		if (_kind != ToolKind.None) Dress();
	}

	private void Begin(ToolKind kind, Vector3 at, float seconds)
	{
		_kind = kind;
		_motion = kind switch
		{
			ToolKind.Keycard201 or ToolKind.Keycard202 or ToolKind.Keycard203 => Motion.Swipe,
			ToolKind.Key or ToolKind.FontKey or ToolKind.PantryKey or ToolKind.DiningKey => Motion.Turn,
			ToolKind.Crowbar => Motion.Prise,
			ToolKind.Lighter => Motion.Flick,
			ToolKind.Knife => Motion.Cut,
			_ => Motion.Reach,
		};
		_len = seconds > 0f ? seconds : _motion switch { Motion.Prise => 2.3f, Motion.Flick => 2.0f, Motion.Cut => 1.7f, Motion.Turn => 1.5f, _ => 1.3f };
		_strokes = _motion == Motion.Prise ? Mathf.Max(1, Mathf.RoundToInt((_len - 0.6f) / 0.55f)) : 2;
		_t = 0f;
		// toward the thing, in the camera's space: a nudge of the hand that way (never across the view)
		var cam = Player?.CameraRig?.Camera;
		var local = cam != null ? cam.ToLocal(at) : new Vector3(0, 0, -1);
		var dir = local.LengthSquared() > 0.0001f ? local.Normalized() : new Vector3(0, 0, -1);
		_reach = new Vector3(Mathf.Clamp(dir.X * 0.12f, -0.16f, 0.06f), Mathf.Clamp(dir.Y * 0.12f, -0.08f, 0.1f), -0.1f);
		Busy = true;
		Last = kind;
		Shown++;
		GD.Print($"[held] {kind}: {_motion}");
		if (_pivot != null) Dress();
	}

	/// <summary>The tool in the fist, held the way it's used.</summary>
	private void Dress()
	{
		_tool?.QueueFree();
		_flame = null; _flameMesh = null;
		_tool = new Node3D { Name = "Tool" };
		_pivot.AddChild(_tool);
		var mesh = new Node3D { Name = "Mesh" };
		_tool.AddChild(mesh);
		ItemMeshes.Build(_kind, mesh);
		// (each lies as it does on its shelf: turned here to how a hand holds it)
		switch (_motion)
		{
			case Motion.Swipe:   // the card upright, edge on to the reader, half out of the fingers
				_tool.Position = new Vector3(0.0f, 0.035f, -0.04f);
				_tool.Rotation = new Vector3(Mathf.Pi * 0.5f, -0.3f + Mathf.Pi * 0.5f, 0f);
				break;
			case Motion.Turn:    // the key's blade forward, its bow in the fingers
				_tool.Position = new Vector3(0.0f, 0.0f, -0.02f);
				_tool.Rotation = new Vector3(0f, Mathf.Pi * 0.5f - 0.5f, Mathf.Pi * 0.5f);
				mesh.Position = new Vector3(0.05f, -0.083f, 0f);
				break;
			case Motion.Prise:   // the bar along the forearm, its claw forward and down
				_tool.Position = new Vector3(0.0f, -0.01f, -0.02f);
				_tool.Rotation = new Vector3(-0.35f, Mathf.Pi * 0.5f, 0f);
				_tool.Scale = Vector3.One;   // (2026-10-14g: its own 61 cm, as picked up; was 0.9 of an 81 cm bar)
				break;
			case Motion.Flick:   // the lighter upright, lid up
				_tool.Position = new Vector3(0.0f, 0.03f, -0.03f);
				_tool.Rotation = new Vector3(0f, -0.3f, 0f);
				_tool.Scale = Vector3.One;   // (2026-10-14g: its own size, as in the hand day to day; was 1.25)
				_flame = new OmniLight3D { Name = "Flame", LightColor = new Color(1f, 0.62f, 0.3f), LightEnergy = 0f, OmniRange = 2.2f, ShadowEnabled = false, Position = new Vector3(0, 0.08f, 0) };
				_tool.AddChild(_flame);
				_flameMesh = new MeshInstance3D
				{
					Name = "FlameMesh", Position = new Vector3(0, 0.076f, 0), Visible = false,
					Mesh = new SphereMesh { Radius = 0.009f, Height = 0.036f, RadialSegments = 8, Rings = 4 },
					MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.7f, 0.3f), EmissionEnabled = true, Emission = new Color(1f, 0.55f, 0.2f), EmissionEnergyMultiplier = 2.5f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
				};
				_tool.AddChild(_flameMesh);
				break;
			case Motion.Cut:     // the blade forward and a little in
				_tool.Position = new Vector3(0.0f, 0.0f, -0.02f);
				_tool.Rotation = new Vector3(0f, Mathf.Pi * 0.5f - 0.4f, Mathf.Pi * 0.5f);
				mesh.Position = new Vector3(0.04f, -0.035f, 0f);
				break;
			default:
				_tool.Position = new Vector3(0.0f, 0.03f, -0.04f);
				break;
		}
		SetLayer(_tool);
		foreach (var n in _tool.FindChildren("*", "GeometryInstance3D", true, false))
			((GeometryInstance3D)n).CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
	}

	public override void _Process(double delta)
	{
		if (_t < _len)
		{
			_t += (float)delta;
			if (_t >= _len) Busy = false;
		}
		base._Process(delta);
	}

	public override void _ExitTree() => Busy = false;

	protected override void OnHidden()
	{
		_tool?.QueueFree();
		_tool = null;
		_kind = ToolKind.None;
	}

	protected override void Animate(float dt)
	{
		if (_pivot == null) return;
		float t = _t, len = _len;
		// in toward the thing over the first third of a second, out over the last
		float inOut = Mathf.SmoothStep(0f, 1f, t / 0.35f) * Mathf.SmoothStep(0f, 1f, (len - t) / 0.4f);
		var off = _reach * inOut;
		var rot = Vector3.Zero;
		float u = Mathf.Clamp((t - 0.35f) / Mathf.Max(0.1f, len - 0.75f), 0f, 1f);   // (0..1 through the action itself)
		switch (_motion)
		{
			case Motion.Swipe:
				// up to the slot, then down through it in one smooth pull
				off += new Vector3(0, 0.03f - 0.09f * Mathf.SmoothStep(0.25f, 0.6f, u), -0.02f * inOut);
				rot.X = -0.15f * Mathf.SmoothStep(0.25f, 0.6f, u);
				break;
			case Motion.Turn:
				off += new Vector3(0, 0, -0.03f * Mathf.SmoothStep(0f, 0.3f, u));
				rot.Z = -Mathf.Pi * 0.5f * Mathf.SmoothStep(0.4f, 0.75f, u);
				break;
			case Motion.Prise:
			{
				// a stroke per nail: worked in, then levered down hard and eased back
				float s = u * _strokes;
				float ph = s - Mathf.Floor(s);
				float lever = u >= 1f ? 0f : Mathf.Sin(Mathf.Clamp(ph / 0.45f, 0f, 1f) * Mathf.Pi * 0.5f) * (1f - Mathf.SmoothStep(0.55f, 1f, ph));
				off += new Vector3(0, -0.06f * inOut - 0.05f * lever, -0.04f * inOut);
				rot.X = -0.5f * lever;
				rot.Z = 0.08f * lever;
				break;
			}
			case Motion.Flick:
			{
				bool lit = u > 0.12f && u < 0.95f;
				if (_flameMesh != null) _flameMesh.Visible = lit;
				if (_flame != null) _flame.LightEnergy = lit ? 0.9f + 0.1f * Mathf.Sin(t * 23f) * Mathf.Sin(t * 7f) : 0f;
				off += new Vector3(-0.08f * inOut, 0.1f * inOut, 0.02f * inOut);
				rot.X = -0.08f * Mathf.SmoothStep(0f, 0.12f, u);   // (the thumb's flick: a small kick)
				break;
			}
			case Motion.Cut:
			{
				float s = Mathf.Sin(u * Mathf.Tau * 2f - Mathf.Pi * 0.5f) * 0.5f + 0.5f;
				off += new Vector3(-0.1f * s, -0.02f * s, -0.02f * inOut);
				rot.Y = 0.25f * s;
				break;
			}
			default:
				off += new Vector3(0, 0, -0.06f * Mathf.SmoothStep(0f, 0.5f, u));
				break;
		}
		Hold = Rest + off;
		_pivot.Rotation = rot;
	}
}
