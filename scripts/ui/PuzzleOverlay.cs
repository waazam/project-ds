using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.LibraryParts;

namespace ProjectDS.UI;

/// <summary>
/// Bent over Act 19's puzzle box: a close view down on the table, the box and its loose pieces. A / D
/// picks out a piece (it lifts a little), E takes it; then W A S D moves it over the box, right-click or
/// the mouse wheel turns it, and E sets it in (if it fits; if not it knocks against the rim). Esc puts a
/// held piece back on the table, or straightens up. A piece already in the box can be picked out and
/// taken out again. Same modal pattern as <see cref="CryptexOverlay"/>.
/// </summary>
public partial class PuzzleOverlay : CanvasLayer
{
	public static PuzzleOverlay Instance { get; private set; }
	public bool IsOpen { get; private set; }
	public PuzzleBox Box { get; private set; }
	public int Selected { get; private set; }
	public bool Holding { get; private set; }
	public Vector2I Cursor { get; private set; }
	public int HeldRot { get; private set; }
	public int Bumps { get; private set; }

	private PlayerController _player;
	private Camera3D _cam;
	private Control _root;
	private Label _hint;
	private System.Action<string> _sound;

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
		_root.AddChild(_hint);
	}

	public void Open(PlayerController player, PuzzleBox box, System.Action<string> sound)
	{
		if (IsOpen || player == null || box == null) return;
		_player = player;
		Box = box;
		_sound = sound;
		IsOpen = true;
		Holding = false;
		Selected = Mathf.Max(0, box.Pieces.FindIndex(p => p.At == null));
		player.PlayerInput.BeginModal();
		var playerCam = player.CameraRig?.Camera;
		if (playerCam != null) playerCam.Current = false;
		_cam = new Camera3D { Fov = 40f };
		Cutscene.SceneRoot(this).AddChild(_cam);
		Vector3 focus = box.ToGlobal(new Vector3(0.3f, 0, 0.02f));
		Vector3 eye = box.ToGlobal(new Vector3(0.3f, 0.95f, 0.45f));
		_cam.GlobalTransform = new Transform3D(Basis.LookingAt(focus - eye, Vector3.Up), eye);
		_cam.Current = true;
		_root.Visible = true;
		Refresh();
	}

	public void Close()
	{
		if (!IsOpen) return;
		if (Holding) PutBack();
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
	}

	private PuzzleBox.Piece Current => Box.Pieces[Selected];

	private void Refresh()
	{
		if (Box == null) return;
		foreach (var p in Box.Pieces) Box.Layout(p);
		if (Holding) Box.Layout(Current, Cursor, HeldRot);
		else Current.Node.Position += Vector3.Up * 0.025f;   // the picked-out piece lifts a little
		_hint.Text = Holding
			? "W A S D  move      right-click / wheel  turn      E  set it in      Esc  put it back"
			: "A / D  pick a piece      E  take it      Esc  straighten up";
	}

	private void PutBack()
	{
		Holding = false;
		Box.Layout(Current);
	}

	/// <summary>Take the selected piece (out of the box, if it's in it) and hold it over the box.</summary>
	public void Take()
	{
		if (Box.IsSolved) return;
		var p = Current;
		Cursor = p.At ?? new Vector2I(1, 1);
		HeldRot = p.Rot;
		Box.Lift(p);
		Holding = true;
		_sound?.Invoke("take");
		Refresh();
	}

	public bool SetIn()
	{
		if (!Holding) return false;
		if (!Box.Place(Current, HeldRot, Cursor))
		{
			Bumps++;
			_sound?.Invoke("bump");
			var tw = Current.Node.CreateTween();
			tw.TweenProperty(Current.Node, "position", Current.Node.Position + Vector3.Up * 0.012f, 0.06f);
			tw.TweenProperty(Current.Node, "position", Current.Node.Position, 0.08f);
			return false;
		}
		Holding = false;
		_sound?.Invoke("place");
		if (Box.IsSolved) { Close(); return true; }
		// on to the next loose piece
		int next = Box.Pieces.FindIndex(q => q.At == null);
		if (next >= 0) Selected = next;
		Refresh();
		return true;
	}

	public override void _Input(InputEvent e)
	{
		if (!IsOpen || Box == null || !GodotObject.IsInstanceValid(Box)) return;
		bool handled = true;
		if (e.IsActionPressed("pause") || e.IsActionPressed("ui_cancel"))
		{
			if (Holding) { PutBack(); Refresh(); } else Close();
		}
		else if (!Holding)
		{
			if (e.IsActionPressed("move_left", true) || e.IsActionPressed("item_prev")) { Selected = (Selected + Box.Pieces.Count - 1) % Box.Pieces.Count; _sound?.Invoke("tick"); Refresh(); }
			else if (e.IsActionPressed("move_right", true) || e.IsActionPressed("item_next")) { Selected = (Selected + 1) % Box.Pieces.Count; _sound?.Invoke("tick"); Refresh(); }
			else if (e.IsActionPressed("interact")) Take();
			else handled = false;
		}
		else
		{
			Vector2I move = Vector2I.Zero;
			if (e.IsActionPressed("move_left", true)) move = new Vector2I(-1, 0);
			else if (e.IsActionPressed("move_right", true)) move = new Vector2I(1, 0);
			else if (e.IsActionPressed("move_forward", true)) move = new Vector2I(0, -1);
			else if (e.IsActionPressed("move_back", true)) move = new Vector2I(0, 1);
			if (move != Vector2I.Zero)
			{
				Cursor = new Vector2I(Mathf.Clamp(Cursor.X + move.X, 0, PuzzleBox.N - 1), Mathf.Clamp(Cursor.Y + move.Y, 0, PuzzleBox.N - 1));
				Refresh();
			}
			else if (e.IsActionPressed("focus") || e.IsActionPressed("item_next") || e.IsActionPressed("item_prev"))
			{
				HeldRot = (HeldRot + (e.IsActionPressed("item_prev") ? 3 : 1)) % 4;
				_sound?.Invoke("turn");
				Refresh();
			}
			else if (e.IsActionPressed("interact")) SetIn();
			else handled = false;
		}
		if (handled) GetViewport().SetInputAsHandled();
	}

	// ------------------------------------------------------------------ for tests

	public void TestSelect(int i) { Selected = i; Refresh(); }
	public void TestHold(Vector2I at, int rot) { Take(); Cursor = at; HeldRot = rot; Refresh(); }
}
