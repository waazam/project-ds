using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;
using ProjectDS.World.LibraryParts;

namespace ProjectDS.UI;

/// <summary>
/// Bent over Act 19's puzzle box: a close view down on the table, the box and its loose pieces, the mouse
/// cursor out.
///
/// With the mouse: the piece under the cursor lifts a little; press the left button on it to pick it up (out of
/// the box, if it's in it) and drag it: it follows the cursor over the table and snaps from cell to cell over the
/// box, with a ghost on the cells it would land on (a warm outline where it fits, red where it doesn't, and the
/// piece goes red too). Let go to drop it: into the box if it fits; if not it knocks the rim and goes back where it
/// was, and let go over the table it goes back to its spot there. A click on a piece in the box (no drag) takes it
/// out. Right-click (or the wheel) turns the held piece a quarter turn, or turns the piece under the cursor where it
/// lies, if it still has room there.
///
/// The keys and the pad still do it all: A / D (LB) picks out a piece, E (A) takes it; then W A S D (the stick)
/// moves it over the box, right-click / the wheel / RT turns it, E sets it in (if it fits; if not it knocks against
/// the rim). Esc (Start) puts a held piece back, or straightens up. Same modal pattern as
/// <see cref="CryptexOverlay"/>.
/// </summary>
public partial class PuzzleOverlay : CanvasLayer
{
	public static PuzzleOverlay Instance { get; private set; }
	public bool IsOpen { get; private set; }
	public PuzzleBox Box { get; private set; }
	public int Selected { get; private set; }
	public bool Holding { get; private set; }
	/// <summary>Holding it with the mouse (dragged), not over the box by the keys.</summary>
	public bool Dragging { get; private set; }
	public Vector2I Cursor { get; private set; }
	public int HeldRot { get; private set; }
	public int Bumps { get; private set; }
	/// <summary>Which of the held piece's cubes the cursor holds it by.</summary>
	public int GrabbedCube => _grab;
	/// <summary>Where the dragged piece would land (its first cell's grid spot), or null when it's off the box.</summary>
	public Vector2I? DragSnap => Dragging ? _snap : null;
	/// <summary>Whether the ghost (where the held piece would go) shows it fitting there.</summary>
	public bool GhostFits { get; private set; }

	/// <summary>How high a held piece rides (its cubes' middles, in the box's space): clear of the rim.</summary>
	public const float HeldY = PuzzleBox.Depth + PuzzleBox.Cell * 0.5f + 0.03f;
	private const float SelectLift = 0.025f;

	private PlayerController _player;
	private Camera3D _cam;
	private Control _root;
	private Label _hint;
	private System.Action<string> _sound;
	private Input.MouseModeEnum _mouseWas = Input.MouseModeEnum.Captured;

	// the drag
	private int _grab;
	private Vector2 _mouse, _pressAt;
	private bool _moved;
	private Vector3 _free;            // where the grabbed cube is over the table (box space), off the box
	private Vector2I? _snap;          // where the piece's first cell would land, over the box
	private Vector2I? _fromAt;        // where it was picked up from
	private int _fromRot;

	// the pieces' look: each eases toward where it belongs, never jumps
	private sealed class Vis { public Vector3 Pos; public float Spin; public int Rot; public int Pivot; }
	private readonly Dictionary<PuzzleBox.Piece, Vis> _vis = new();
	private PuzzleBox _visBox;
	private bool _settling;

	// the ghost: up to four squares on the cells the held piece would take
	private readonly List<MeshInstance3D> _ghost = new();
	private StandardMaterial3D _ghostOk, _ghostBad;

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
		_hint.OffsetTop = -50; _hint.OffsetBottom = -12;
		_root.AddChild(_hint);
	}

	public void Open(PlayerController player, PuzzleBox box, System.Action<string> sound)
	{
		if (IsOpen || player == null || box == null) return;
		_player = player;
		Box = box;
		_sound = sound;
		IsOpen = true;
		Holding = Dragging = false;
		Selected = Mathf.Max(0, box.Pieces.FindIndex(p => p.At == null));
		player.PlayerInput.BeginModal();
		PlayerHands.Hold(this);   // the wheel, the shoulders and 1-9 are the puzzle's while it's open, never the item in hand
		var playerCam = player.CameraRig?.Camera;
		if (playerCam != null) playerCam.Current = false;
		_cam = new Camera3D { Fov = 40f };
		Cutscene.SceneRoot(this).AddChild(_cam);
		Vector3 focus = box.ToGlobal(new Vector3(0.3f, 0, 0.02f));
		Vector3 eye = box.ToGlobal(new Vector3(0.3f, 0.95f, 0.45f));
		_cam.GlobalTransform = new Transform3D(Basis.LookingAt(focus - eye, Vector3.Up), eye);
		_cam.Current = true;
		// the cursor out, to pick the pieces up with
		_mouseWas = Input.MouseMode;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		_mouse = GetViewport().GetMousePosition();
		BuildGhost();
		InitVis();
		_root.Visible = true;
		Refresh();
	}

	public void Close()
	{
		if (!IsOpen) return;
		if (Holding) PutBack();
		IsOpen = false;
		_root.Visible = false;
		PlayerHands.Release(this);
		foreach (var g in _ghost) if (GodotObject.IsInstanceValid(g)) g.Visible = false;
		Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
		Input.MouseMode = _mouseWas == Input.MouseModeEnum.Visible ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
		if (_player != null && GodotObject.IsInstanceValid(_player))
		{
			_player.PlayerInput.EndModal();
			var cam = _player.CameraRig?.Camera;
			if (cam != null) cam.Current = true;
		}
		if (_cam != null && GodotObject.IsInstanceValid(_cam)) _cam.QueueFree();
		_cam = null;
		_player = null;
		_settling = true;   // the pieces finish easing into place
	}

	private PuzzleBox.Piece Current => Box.Pieces[Selected];

	private void Refresh()
	{
		if (Box == null) return;
		if (!Holding) _hint.Text = "drag a piece  left mouse      turn it  right-click / wheel      Esc  straighten up\n"
			+ "or:  A / D  pick a piece      E  take it";
		else if (Dragging) _hint.Text = "let go  set it down      right-click / wheel  turn      Esc  put it back";
		else _hint.Text = "W A S D  move      right-click / wheel  turn      E  set it in      Esc  put it back";
	}

	private void PutBack()
	{
		if (Dragging) { Dragging = false; GoHome(Current); return; }
		Holding = false;
		Box.ToRest(Current);
	}

	/// <summary>Take the selected piece (out of the box, if it's in it) and hold it over the box.</summary>
	public void Take()
	{
		if (Box.IsSolved) return;
		if (Dragging) { Dragging = false; GoHome(Current); }
		var p = Current;
		Cursor = p.At ?? new Vector2I(1, 1);
		HeldRot = p.Rot;
		_grab = 0;
		Box.Lift(p);
		Holding = true;
		_sound?.Invoke("take");
		Refresh();
	}

	public bool SetIn()
	{
		if (!Holding || Dragging) return false;
		if (!Box.Place(Current, HeldRot, Cursor))
		{
			Knock();
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

	/// <summary>A piece that won't go: it knocks against the rim (a little hop).</summary>
	private void Knock()
	{
		Bumps++;
		_sound?.Invoke("bump");
		if (_vis.TryGetValue(Current, out var v)) v.Pos += Vector3.Up * 0.015f;
	}

	private void Turn(int dir)
	{
		HeldRot = ((HeldRot + dir) % 4 + 4) % 4;
		_sound?.Invoke("turn");
		if (Dragging) UpdateDrag();
		Refresh();
	}

	// ------------------------------------------------------------------ the mouse

	/// <summary>The mouse moved to <paramref name="at"/> (viewport pixels): the dragged piece follows; otherwise
	/// the piece under the cursor is the one picked out.</summary>
	public void PointerMove(Vector2 at)
	{
		if (!IsOpen) return;
		_mouse = at;
		if (_pressAt.DistanceTo(at) > 3f) _moved = true;
		if (Dragging) { UpdateDrag(); return; }
		if (Holding) return;
		var (pi, _) = PickAt(at);
		if (pi >= 0 && pi != Selected) { Selected = pi; Refresh(); }
		Input.SetDefaultCursorShape(pi >= 0 && !Box.IsSolved ? Input.CursorShape.PointingHand : Input.CursorShape.Arrow);
	}

	/// <summary>The left button went down at <paramref name="at"/>: picks up the piece there (or takes over the one held by the keys).</summary>
	public bool PointerDown(Vector2 at)
	{
		if (!IsOpen || Box.IsSolved) return false;
		_mouse = _pressAt = at;
		_moved = false;
		if (Holding && !Dragging)
		{
			// held by the keys: the mouse takes it from there
			_fromAt = null;
			_fromRot = Current.RestRot;
			Dragging = true;
			_moved = true;
			UpdateDrag();
			Refresh();
			return true;
		}
		if (Holding) return false;
		var (pi, cube) = PickAt(at);
		if (pi < 0) return false;
		Selected = pi;
		var p = Current;
		_fromAt = p.At;
		_fromRot = p.Rot;
		_grab = cube;
		HeldRot = p.Rot;
		Box.Lift(p);
		Holding = Dragging = true;
		_sound?.Invoke("take");
		Input.SetDefaultCursorShape(Input.CursorShape.Drag);
		UpdateDrag();
		Refresh();
		return true;
	}

	/// <summary>The left button came up at <paramref name="at"/>: drops the dragged piece. True if it went into the box.</summary>
	public bool PointerUp(Vector2 at)
	{
		if (!IsOpen || !Dragging) return false;
		_mouse = at;
		UpdateDrag();
		Dragging = false;
		Input.SetDefaultCursorShape(Input.CursorShape.PointingHand);
		var p = Current;
		// a click on a piece in the box, no drag: it comes out, back to its place on the table
		if (!_moved && _fromAt != null)
		{
			Holding = false;
			Box.ToRest(p);
			_sound?.Invoke("clack");
			Refresh();
			return false;
		}
		if (_snap is { } snap)
		{
			if (Box.Place(p, HeldRot, snap))
			{
				Holding = false;
				_sound?.Invoke("place");
				if (Box.IsSolved) { Close(); return true; }
				Refresh();
				return true;
			}
			Knock();
		}
		else if (_moved) _sound?.Invoke("clack");
		GoHome(p);
		Refresh();
		return false;
	}

	/// <summary>Right-click (or the wheel) at <paramref name="at"/>: turns the held piece, or the piece under the
	/// cursor where it lies (if it still has room there). True if something turned.</summary>
	public bool PointerTurn(Vector2 at, int dir)
	{
		if (!IsOpen || Box.IsSolved) return false;
		_mouse = at;
		if (Holding) { Turn(dir); return true; }
		var (pi, _) = PickAt(at);
		if (pi < 0) return false;
		Selected = pi;
		var p = Current;
		bool ok = p.At is { } a ? TurnInBox(p, a, dir) : Box.TurnAtRest(p, dir);
		if (ok) _sound?.Invoke("turn");
		else Knock();
		Refresh();
		return ok;
	}

	/// <summary>Turns a piece in the box about its middle, nudged a cell if it needs to be, if it fits there.</summary>
	private bool TurnInBox(PuzzleBox.Piece p, Vector2I at, int dir)
	{
		int rot = ((p.Rot + dir) % 4 + 4) % 4, was = p.Rot;
		Vector2 Mid(int r, Vector2I a)
		{
			Vector2 m = Vector2.Zero;
			var cells = PuzzleBox.Rotated(p.Shape, r);
			foreach (var c in cells) m += (Vector2)(a + c);
			return m / cells.Length;
		}
		Vector2 mid = Mid(was, at), offset = Mid(rot, Vector2I.Zero);
		var best = new Vector2I(Mathf.RoundToInt(mid.X - offset.X), Mathf.RoundToInt(mid.Y - offset.Y));
		Box.Lift(p);
		foreach (var d in new[] { Vector2I.Zero, new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(-1, -1), new(1, -1), new(-1, 1) })
			if (Box.Place(p, rot, best + d)) return true;
		Box.Place(p, was, at);
		return false;
	}

	/// <summary>A dropped piece that didn't go in: back where it was picked up from (its cell in the box, if it was
	/// in it and still fits there, else its place on the table).</summary>
	private void GoHome(PuzzleBox.Piece p)
	{
		Holding = false;
		if (_fromAt is { } a && Box.Fits(p, _fromRot, a)) Box.Place(p, _fromRot, a);
		else Box.ToRest(p);
		_fromAt = null;
	}

	/// <summary>Where the cursor's ray meets the plane the held piece rides in (the box's space).</summary>
	private Vector3? RayToHeldPlane(Vector2 at)
	{
		if (_cam == null || !GodotObject.IsInstanceValid(_cam)) return null;
		Vector3 o = Box.ToLocal(_cam.ProjectRayOrigin(at));
		Vector3 d = (Box.GlobalBasis.Inverse() * _cam.ProjectRayNormal(at)).Normalized();
		if (Mathf.Abs(d.Y) < 1e-4f) return null;
		float t = (HeldY - o.Y) / d.Y;
		return t > 0 ? o + d * t : null;
	}

	private void UpdateDrag()
	{
		if (RayToHeldPlane(_mouse) is not { } hit) return;
		var p = Current;
		var cells = PuzzleBox.Rotated(p.Shape, HeldRot);
		float half = PuzzleBox.N * PuzzleBox.Cell * 0.5f;
		if (Mathf.Abs(hit.X) <= half + PuzzleBox.Wall && Mathf.Abs(hit.Z) <= half + PuzzleBox.Wall)
		{
			// over the box: the grabbed cube on the cell under the cursor, the whole piece kept inside the box
			var cell = new Vector2I(Mathf.Clamp(Mathf.FloorToInt((hit.X + half) / PuzzleBox.Cell), 0, PuzzleBox.N - 1),
				Mathf.Clamp(Mathf.FloorToInt((hit.Z + half) / PuzzleBox.Cell), 0, PuzzleBox.N - 1));
			int w = 0, h = 0;
			foreach (var c in cells) { w = Mathf.Max(w, c.X + 1); h = Mathf.Max(h, c.Y + 1); }
			var s = cell - cells[_grab];
			_snap = new Vector2I(Mathf.Clamp(s.X, 0, PuzzleBox.N - w), Mathf.Clamp(s.Y, 0, PuzzleBox.N - h));
			Cursor = _snap.Value;
		}
		else
		{
			_snap = null;
			_free = new Vector3(Mathf.Clamp(hit.X, -half - 0.1f, 0.95f), HeldY, Mathf.Clamp(hit.Z, -0.32f, 0.32f));
		}
	}

	/// <summary>The piece and cube under a viewport point (the nearest hit along the cursor's ray), or (-1, -1).</summary>
	private (int piece, int cube) PickAt(Vector2 at)
	{
		if (_cam == null || !GodotObject.IsInstanceValid(_cam) || Box == null) return (-1, -1);
		Vector3 o = Box.ToLocal(_cam.ProjectRayOrigin(at));
		Vector3 d = (Box.GlobalBasis.Inverse() * _cam.ProjectRayNormal(at)).Normalized();
		float best = float.MaxValue;
		(int, int) hit = (-1, -1);
		float r = PuzzleBox.Cell * 0.5f + 0.003f;
		for (int i = 0; i < Box.Pieces.Count; i++)
		{
			var p = Box.Pieces[i];
			if (Holding && i == Selected) continue;
			for (int c = 0; c < p.Cubes.Count; c++)
			{
				Vector3 centre = p.Node.Transform * p.Cubes[c].Position;
				if (RayBox(o, d, centre - Vector3.One * r, centre + Vector3.One * r) is { } t && t < best) { best = t; hit = (i, c); }
			}
		}
		return hit;
	}

	private static float? RayBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max)
	{
		float tmin = 0f, tmax = float.MaxValue;
		for (int a = 0; a < 3; a++)
		{
			if (Mathf.Abs(d[a]) < 1e-6f) { if (o[a] < min[a] || o[a] > max[a]) return null; continue; }
			float t1 = (min[a] - o[a]) / d[a], t2 = (max[a] - o[a]) / d[a];
			if (t1 > t2) (t1, t2) = (t2, t1);
			tmin = Mathf.Max(tmin, t1);
			tmax = Mathf.Min(tmax, t2);
			if (tmin > tmax) return null;
		}
		return tmin;
	}

	// ------------------------------------------------------------------ input

	public override void _Input(InputEvent e)
	{
		if (!IsOpen || Box == null || !GodotObject.IsInstanceValid(Box)) return;
		// the mouse first: every button is the puzzle's while it's open (and none recaptures the cursor)
		if (e is InputEventMouseMotion mm) { PointerMove(mm.Position); return; }
		if (e is InputEventMouseButton mb)
		{
			GetViewport().SetInputAsHandled();
			switch (mb.ButtonIndex)
			{
				case MouseButton.Left when mb.Pressed: PointerDown(mb.Position); break;
				case MouseButton.Left: PointerUp(mb.Position); break;
				case MouseButton.Right when mb.Pressed: PointerTurn(mb.Position, 1); break;
				case MouseButton.WheelDown when mb.Pressed: PointerTurn(mb.Position, 1); break;
				case MouseButton.WheelUp when mb.Pressed: PointerTurn(mb.Position, -1); break;
			}
			return;
		}
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
		else if (Dragging)
		{
			// the mouse has it: the pad's turn still turns it, the rest waits for the drop
			if (e.IsActionPressed("focus") || e.IsActionPressed("item_next") || e.IsActionPressed("item_prev")) Turn(e.IsActionPressed("item_prev") ? -1 : 1);
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
				Turn(e.IsActionPressed("item_prev") ? -1 : 1);
			else if (e.IsActionPressed("interact")) SetIn();
			else handled = false;
		}
		if (handled) GetViewport().SetInputAsHandled();
	}

	// ------------------------------------------------------------------ the look: easing, the ghost, the shadow

	private void InitVis()
	{
		_vis.Clear();
		_visBox = Box;
		foreach (var p in Box.Pieces)
		{
			Box.Layout(p);
			_vis[p] = new Vis { Pos = p.Node.Position + CellVec(PuzzleBox.Rotated(p.Shape, p.Rot)[0]), Rot = p.Rot, Pivot = 0 };
		}
	}

	private void BuildGhost()
	{
		foreach (var g in _ghost) if (GodotObject.IsInstanceValid(g)) g.QueueFree();
		_ghost.Clear();
		_ghostOk ??= GhostMat(GhostTex(true), new Color(1f, 0.78f, 0.45f, 0.95f));
		_ghostBad ??= GhostMat(GhostTex(false), new Color(0.95f, 0.18f, 0.1f, 0.55f));
		for (int i = 0; i < 4; i++)
		{
			var m = new MeshInstance3D
			{
				Name = "Ghost", Mesh = new PlaneMesh { Size = new Vector2(PuzzleBox.Cell - 0.006f, PuzzleBox.Cell - 0.006f) },
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false,
			};
			Box.AddChild(m);
			_ghost.Add(m);
		}
	}

	private static StandardMaterial3D GhostMat(Texture2D tex, Color c) => new()
	{
		AlbedoTexture = tex, AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear, RenderPriority = 1,
	};

	/// <summary>The ghost's square: a soft outline with a faint fill (fits), or a fuller wash (blocked).</summary>
	private static Texture2D GhostTex(bool outline)
	{
		const int S = 32;
		var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
		for (int y = 0; y < S; y++)
			for (int x = 0; x < S; x++)
			{
				float e = Mathf.Min(Mathf.Min(x, S - 1 - x), Mathf.Min(y, S - 1 - y));   // pixels from the edge
				float a = outline ? (e < 3f ? 1f : e < 5f ? 0.5f : 0.12f) : (e < 2f ? 0.9f : 0.55f);
				img.SetPixel(x, y, new Color(1, 1, 1, a));
			}
		img.GenerateMipmaps();
		return ImageTexture.CreateFromImage(img);
	}

	public override void _Process(double delta)
	{
		if (Box == null || !GodotObject.IsInstanceValid(Box)) { _settling = false; return; }
		if (!IsOpen && !_settling) return;
		if (_visBox != Box) InitVis();
		float dt = (float)delta;
		bool still = true;
		PuzzleBox.Piece held = Holding ? Current : null;
		foreach (var p in Box.Pieces)
		{
			if (!_vis.TryGetValue(p, out var v)) continue;
			bool isHeld = p == held;
			int rot = isHeld ? HeldRot : p.Rot, pivot = isHeld ? _grab : 0;
			// a new turn: the cubes are laid out turned (a quarter turn of the grid is +90 degrees about up), so the
			// node starts turned back by as much, looking just as it did, and eases round about the cube it pivots on
			if (rot != v.Rot)
			{
				int steps = ((rot - v.Rot) % 4 + 4) % 4;
				v.Spin -= (steps == 3 ? -1 : steps) * Mathf.Pi * 0.5f;
				v.Rot = rot;
			}
			if (pivot != v.Pivot)
			{
				var cells = PuzzleBox.Rotated(p.Shape, v.Rot);
				v.Pos += new Basis(Vector3.Up, v.Spin) * CellVec(cells[pivot] - cells[v.Pivot]);
				v.Pivot = pivot;
			}
			var cs = PuzzleBox.Rotated(p.Shape, v.Rot);
			for (int i = 0; i < cs.Length; i++) p.Cubes[i].Position = CellVec(cs[i] - cs[pivot]);
			Vector3 target = isHeld ? HeldTarget(p, cs) : RestingTarget(p) + CellVec(cs[pivot]);
			if (!isHeld && IsOpen && !Holding && p == Current && !Box.IsSolved) target += Vector3.Up * SelectLift;
			float k = 1f - Mathf.Exp(-(isHeld && Dragging ? 30f : 16f) * dt);
			v.Pos = v.Pos.Lerp(target, k);
			v.Spin *= Mathf.Exp(-14f * dt);
			if (Mathf.Abs(v.Spin) < 0.002f) v.Spin = 0f;
			if (v.Pos.DistanceSquaredTo(target) > 1e-8f || v.Spin != 0f) still = false;
			else v.Pos = target;
			p.Node.Position = v.Pos;
			p.Node.Rotation = new Vector3(0, v.Spin, 0);
			// held: a shadow under it, and red while it's over a spot it won't fit
			float lift = Mathf.Clamp((v.Pos.Y - PuzzleBox.Cell * 0.5f - 0.02f) / (HeldY - PuzzleBox.Cell * 0.5f - 0.02f), 0f, 1f);
			PuzzleBox.Shadow(p, isHeld ? lift : 0f);
			PuzzleBox.Tint(p, isHeld && GhostShown && !GhostFits ? new Color(1f, 0.5f, 0.45f) : Colors.White);
		}
		UpdateGhost(held);
		if (!IsOpen && still) _settling = false;
	}

	private bool GhostShown => Holding && (!Dragging || _snap != null);

	private static Vector3 CellVec(Vector2I c) => new(c.X * PuzzleBox.Cell, 0, c.Y * PuzzleBox.Cell);

	/// <summary>Where a piece not held sits (its first cell's middle): in the box, or on the table.</summary>
	private static Vector3 RestingTarget(PuzzleBox.Piece p)
		=> p.At is { } a ? PuzzleBox.CellLocal(a.X, a.Y) : p.Rest + Vector3.Up * PuzzleBox.Cell * 0.5f;

	/// <summary>Where the held piece's grabbed cube rides: on its cell over the box, or under the cursor.</summary>
	private Vector3 HeldTarget(PuzzleBox.Piece p, Vector2I[] cells)
	{
		Vector2I? at = Dragging ? _snap : Cursor;
		if (at is { } a)
		{
			Vector3 c = PuzzleBox.CellLocal(a.X + cells[_grab].X, a.Y + cells[_grab].Y);
			return new Vector3(c.X, HeldY, c.Z);
		}
		return _free;
	}

	private void UpdateGhost(PuzzleBox.Piece held)
	{
		bool show = IsOpen && held != null && GhostShown;
		Vector2I at = Dragging && _snap is { } s ? s : Cursor;
		GhostFits = show && Box.Fits(held, HeldRot, at);
		var cells = held != null ? PuzzleBox.Rotated(held.Shape, HeldRot) : System.Array.Empty<Vector2I>();
		for (int i = 0; i < _ghost.Count; i++)
		{
			var g = _ghost[i];
			if (!GodotObject.IsInstanceValid(g)) continue;
			g.Visible = show && i < cells.Length;
			if (!g.Visible) continue;
			Vector2I c = at + cells[i];
			bool inside = c.X >= 0 && c.Y >= 0 && c.X < PuzzleBox.N && c.Y < PuzzleBox.N;
			bool taken = inside && Box.Pieces.Exists(q => q != held && q.At is { } qa && System.Array.Exists(PuzzleBox.Rotated(q.Shape, q.Rot), qc => qa + qc == c));
			Vector3 pos = PuzzleBox.CellLocal(c.X, c.Y);
			pos.Y = !inside ? PuzzleBox.Depth + 0.003f : taken ? PuzzleBox.Floor + PuzzleBox.Cell + 0.002f : PuzzleBox.Floor + 0.002f;
			g.Position = pos;
			g.MaterialOverride = GhostFits ? _ghostOk : _ghostBad;
		}
	}

	// ------------------------------------------------------------------ for tests

	public void TestSelect(int i) { Selected = i; Refresh(); }
	public void TestHold(Vector2I at, int rot) { Take(); Cursor = at; HeldRot = rot; Refresh(); }
	public void TestPutBack() { if (Holding) { PutBack(); Refresh(); } }

	/// <summary>A point in the box's space on screen (viewport pixels, as the mouse reports them).</summary>
	public Vector2 ScreenOf(Vector3 boxLocal) => _cam.UnprojectPosition(Box.ToGlobal(boxLocal));

	/// <summary>The top of a piece's cube where it lies, on screen: where to click to pick it up by that cube.</summary>
	public Vector2 ScreenOfCube(PuzzleBox.Piece p, int cube)
	{
		var cells = PuzzleBox.Rotated(p.Shape, p.Rot);
		return ScreenOf(RestingTarget(p) + CellVec(cells[cube]) + Vector3.Up * PuzzleBox.Cell * 0.45f);
	}

	/// <summary>Where on screen to let go of the held piece so its grabbed cube lands on grid cell <paramref name="cell"/>.</summary>
	public Vector2 ScreenOfHeldCell(Vector2I cell)
	{
		Vector3 c = PuzzleBox.CellLocal(cell.X, cell.Y);
		return ScreenOf(new Vector3(c.X, HeldY, c.Z));
	}
}
