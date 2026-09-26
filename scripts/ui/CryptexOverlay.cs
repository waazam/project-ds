using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.StationParts;

namespace ProjectDS.UI;

/// <summary>
/// Leaning over Room 2's cryptex (Act 13): a close, fixed view of the rings, A/D to pick a ring,
/// W/S to turn it (held keys repeat), Esc (or E) to straighten up. The world keeps going while the
/// player is bent over it — the room is filling up — so this never pauses anything; it only swaps
/// the camera and takes the movement keys. Same modal pattern as <see cref="TapeCutOverlay"/>.
/// </summary>
public partial class CryptexOverlay : CanvasLayer
{
	public static CryptexOverlay Instance { get; private set; }
	public bool IsOpen { get; private set; }
	public Cryptex Box { get; private set; }

	private PlayerController _player;
	private Camera3D _cam;
	private Control _root;
	private Label _hint;
	private System.Action _onClose;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		Layer = 19;
		_root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);
		_hint = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		_hint.AddThemeFontOverride("font", UiKit.Mono);
		_hint.AddThemeFontSizeOverride("font_size", 11);
		_hint.AddThemeColorOverride("font_color", new Color(0.85f, 0.82f, 0.74f));
		_hint.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
		_hint.AddThemeConstantOverride("shadow_offset_x", 1);
		_hint.AddThemeConstantOverride("shadow_offset_y", 1);
		_hint.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		_hint.OffsetTop = -44; _hint.OffsetBottom = -16;
		_hint.Text = "A / D  choose a ring      W / S  turn it      E  straighten up";
		_root.AddChild(_hint);
	}

	public void Open(PlayerController player, Cryptex box, System.Action onClose = null)
	{
		if (IsOpen || player == null || box == null) return;
		_player = player;
		Box = box;
		_onClose = onClose;
		IsOpen = true;
		player.PlayerInput.BeginModal();
		var playerCam = player.CameraRig?.Camera;
		if (playerCam != null) playerCam.Current = false;
		_cam = new Camera3D { Fov = 42f };
		Cutscene.SceneRoot(this).AddChild(_cam);
		Vector3 focus = box.GlobalPosition;
		// looking down onto the reading line along the top, where the letters that count sit between the guides
		Vector3 eye = box.ToGlobal(new Vector3(0, 0.3f, 0.13f));
		_cam.GlobalTransform = new Transform3D(Basis.LookingAt(focus - eye, Vector3.Up), eye);
		_cam.Current = true;
		_root.Visible = true;
	}

	public void Close()
	{
		if (!IsOpen) return;
		IsOpen = false;
		_root.Visible = false;
		if (_player != null && GodotObject.IsInstanceValid(_player))
		{
			_player.PlayerInput.EndModal();
			var cam = _player.CameraRig?.Camera;
			if (cam != null) cam.Current = true;
		}
		if (_cam != null && GodotObject.IsInstanceValid(_cam)) _cam.QueueFree();
		_cam = null;
		_player = null;
		var cb = _onClose;
		_onClose = null;
		cb?.Invoke();
	}

	public override void _Input(InputEvent e)
	{
		if (!IsOpen || Box == null || !GodotObject.IsInstanceValid(Box)) return;
		if (e.IsActionPressed("ui_cancel") || e.IsActionPressed("pause") || e.IsActionPressed("interact")) { Close(); GetViewport().SetInputAsHandled(); return; }
		if (e.IsActionPressed("move_left", true)) { Box.Select(Box.Selected - 1); Handled(); }
		else if (e.IsActionPressed("move_right", true)) { Box.Select(Box.Selected + 1); Handled(); }
		else if (e.IsActionPressed("move_forward", true)) { Box.Turn(-1); Handled(); }
		else if (e.IsActionPressed("move_back", true)) { Box.Turn(1); Handled(); }
	}

	private void Handled() => GetViewport().SetInputAsHandled();
}
