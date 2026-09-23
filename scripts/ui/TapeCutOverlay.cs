using System;
using System.Collections.Generic;
using Godot;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.UI;

/// <summary>
/// The duct-tape cutting minigame (Act 13): the view cuts to a close, fixed framing of the tape
/// (a door frame's slit, a box's edge...), the player holds left click and drags in the cut's
/// direction, and a knife-slice sound tracks their progress. Several cuts can run in sequence
/// (the cigar box's three sides), each with its own camera framing, direction and tape visual —
/// this class only owns the modal input, the camera swap and the drag-progress bookkeeping; the
/// caller (<see cref="CutSpec"/>) supplies everything about what's actually being cut and reacts
/// to progress/completion itself (shrinking its own tape mesh, playing its own effects).
///
/// Modelled on CodeLockOverlay's modal pattern (PlayerInput.BeginModal, raw _Input reading), but
/// a real close-up 3D view via a second Camera3D instead of a 2D panel — a puzzle the player's
/// full attention is on deserves to fill the screen, not sit in a corner.
/// </summary>
public partial class TapeCutOverlay : CanvasLayer
{
	public static TapeCutOverlay Instance { get; private set; }

	/// <summary>One cut: the close-up framing to show while it's active, the screen-space direction
	/// to drag (down = (0,1)), how many pixels of drag completes it, a progress callback (0..1, for
	/// the caller's own tape visual/knife animation), and a callback once this cut is done.</summary>
	public sealed class CutSpec
	{
		public Transform3D CameraView;
		public Vector2 Direction = Vector2.Down;
		public float PixelsNeeded = 260f;
		public Action<float> OnProgress;
		public Action OnCut;
	}

	public bool IsOpen { get; private set; }
	/// <summary>For tests: index of the cut currently active, or -1 if closed.</summary>
	public int ActiveIndex { get; private set; } = -1;
	/// <summary>For tests: 0..1 progress on the active cut.</summary>
	public float Progress { get; private set; }

	private List<CutSpec> _cuts;
	private int _index;
	private bool _dragging;
	private PlayerController _player;
	private Camera3D _cam;
	private Action _onAllCut;
	private Control _root;
	private Label _hint;

	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public override void _Ready()
	{
		Layer = 19;
		_root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_root);
		var wash = new ColorRect { Color = new Color(0, 0, 0, 0.1f), MouseFilter = Control.MouseFilterEnum.Ignore };
		wash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(wash);
		_hint = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		_hint.AddThemeFontOverride("font", UiKit.Mono);
		_hint.AddThemeFontSizeOverride("font_size", 12);
		_hint.AddThemeColorOverride("font_color", new Color(0.85f, 0.82f, 0.74f));
		_hint.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
		_hint.AddThemeConstantOverride("shadow_offset_x", 1);
		_hint.AddThemeConstantOverride("shadow_offset_y", 1);
		_hint.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		_hint.OffsetTop = -60; _hint.OffsetBottom = -24;
		_root.AddChild(_hint);
	}

	public void Open(PlayerController player, List<CutSpec> cuts, Action onAllCut)
	{
		if (IsOpen || player == null || cuts == null || cuts.Count == 0) return;
		_player = player;
		_cuts = cuts;
		_onAllCut = onAllCut;
		_index = 0;
		Progress = 0f;
		_dragging = false;
		IsOpen = true;
		ActiveIndex = 0;
		player.PlayerInput.BeginModal();
		var playerCam = player.CameraRig?.Camera;
		if (playerCam != null) playerCam.Current = false;
		_cam = new Camera3D();
		Cutscene.SceneRoot(this).AddChild(_cam);
		_cam.GlobalTransform = cuts[0].CameraView;
		_cam.Current = true;
		_root.Visible = true;
		_hint.Text = HintFor(cuts[0].Direction);
	}

	/// <summary>Ends the minigame. <paramref name="completed"/> false means the player backed out
	/// (Esc) — nothing already cut un-cuts, but the overall onAllCut callback never fires.</summary>
	public void Close(bool completed)
	{
		if (!IsOpen) return;
		IsOpen = false;
		ActiveIndex = -1;
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
		var cb = _onAllCut;
		_onAllCut = null;
		if (completed) cb?.Invoke();
	}

	public override void _Input(InputEvent e)
	{
		if (!IsOpen) return;
		if (e.IsActionPressed("pause") || e.IsActionPressed("ui_cancel")) { Close(false); GetViewport().SetInputAsHandled(); return; }
		if (e is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			_dragging = mb.Pressed;
			GetViewport().SetInputAsHandled();
			return;
		}
		if (e is InputEventMouseMotion mm && _dragging)
		{
			var cut = _cuts[_index];
			float d = mm.Relative.Dot(cut.Direction.Normalized());
			if (d > 0f)
			{
				Progress = Mathf.Min(1f, Progress + d / Mathf.Max(cut.PixelsNeeded, 1f));
				cut.OnProgress?.Invoke(Progress);
				if (Progress >= 1f) AdvanceCut();
			}
			GetViewport().SetInputAsHandled();
		}
	}

	private void AdvanceCut()
	{
		var cut = _cuts[_index];
		PlaySlice();
		cut.OnCut?.Invoke();
		_index++;
		Progress = 0f;
		_dragging = false;
		if (_index >= _cuts.Count) { Close(true); return; }
		ActiveIndex = _index;
		var next = _cuts[_index];
		_hint.Text = HintFor(next.Direction);
		Transform3D from = _cam.GlobalTransform;
		var tween = CreateTween();
		tween.TweenMethod(Callable.From<float>(t => { if (_cam != null && GodotObject.IsInstanceValid(_cam)) _cam.GlobalTransform = from.InterpolateWith(next.CameraView, t); }), 0f, 1f, 0.5f)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
	}

	private static string HintFor(Vector2 dir) => Mathf.Abs(dir.Y) >= Mathf.Abs(dir.X)
		? "Hold [Left Click] and drag down to cut"
		: "Hold [Left Click] and drag across to cut";

	private void PlaySlice()
	{
		string path = $"res://assets/audio/sfx/knife_slice_{GD.RandRange(1, 3):00}.wav";
		if (!ResourceLoader.Exists(path) || _player == null) return;
		var s = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(path), Bus = "Player", VolumeDb = -6f, PitchScale = (float)GD.RandRange(0.92, 1.08) };
		Cutscene.SceneRoot(this).AddChild(s);
		s.Finished += s.QueueFree;
		s.Play();
	}

	/// <summary>For tests: completes the active cut instantly, as a full drag would.</summary>
	public void TestCompleteCut()
	{
		if (!IsOpen) return;
		Progress = 1f;
		_cuts[_index].OnProgress?.Invoke(1f);
		AdvanceCut();
	}
}
