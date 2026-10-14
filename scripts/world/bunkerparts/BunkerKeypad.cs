using System;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World.BunkerParts;

/// <summary>
/// (2026-10-13, the owner: "make the bunker keypad an actual keypad that cant be clicked with the mouse") The bunker
/// door's code lock: a weathered 3x4 stainless keypad in a cast box bolted to the concrete right of the door, under a
/// rain hood, with a red seven-segment readout, three status lamps (power, locked, open) and a conduit down into a
/// junction box. Modelled on the 1990s outdoor door-entry pads (a stainless faceplate on a die-cast back box, engraved
/// square keys, * and # for clear and enter).
///
/// It's used where it hangs, never through a screen overlay, and the mouse cursor never comes up. E on it (A on a
/// pad) leans the view in close (a camera of its own eases from the eye to the pad and back). There the view's centre
/// sits on the highlighted key: the arrows, WASD, the left stick or the D-pad move it, and E/Space/A presses it. The
/// number keys (and the numpad) press their key straight away. Backspace (X on a pad) takes the last digit back, * clears,
/// Enter or # tries the code, and Esc or B steps back. The mouse does nothing while it's up. Each press sinks the key
/// and chirps (eight takes, never the last three again). A wrong code buzzes, flashes the red lamp and clears; the right
/// one beeps, lights the green lamp and calls <see cref="Unlocked"/> (the bunker's own unlock, unchanged).
///
/// Dead (dark, "no power") until the cabin has been seen burning (Act 7); the code is never filled in: the readout
/// starts empty every time, and the four numbers are the player's to remember.
/// </summary>
public partial class BunkerKeypad : Interactable
{
	/// <summary>The keys, row by row as they stand on the pad.</summary>
	public static readonly string[] Keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "*", "0", "#" };
	public const int Cols = 3, Rows = 4;
	public const int Clear = 9, Enter = 11;

	public enum Lamp { Off, Locked, Denied, Open }

	/// <summary>Raised once, when the right code is tried.</summary>
	public event Action Unlocked;
	/// <summary>The code it opens to (the survey lot's four numbers).</summary>
	public Func<string> CodeSource;

	public bool Engaged { get; private set; }
	/// <summary>The digits on the readout right now (for tests; empty when the pad comes up).</summary>
	public string Entered { get; private set; } = "";
	/// <summary>The highlighted key (an index into <see cref="Keys"/>).</summary>
	public int Selected { get; private set; } = 4;
	public Lamp LampState { get; private set; } = Lamp.Off;
	public bool Powered { get; private set; }
	/// <summary>True while the close view is easing in or out, or is up.</summary>
	public bool ViewUp => _cam != null;

	/// <summary>Where the faceplate's centre is (world): aim here to use it.</summary>
	public Vector3 FaceWorld => ToGlobal(new Vector3(0, -0.01f, Front + 0.01f));
	/// <summary>A key's top (world).</summary>
	public Vector3 KeyWorld(int i) => ToGlobal(KeyCentre(i) + Vector3.Back * 0.008f);

	// ---- the unit's dimensions (local: +Z out of the wall, origin on the concrete at the box's centre)
	private const float BoxW = 0.15f, BoxH = 0.25f, BoxD = 0.045f;
	private const float PlateW = 0.124f, PlateH = 0.214f, PlateT = 0.004f;
	private const float Front = BoxD + PlateT;     // the faceplate's front surface
	private const float KeySize = 0.027f, KeyPitch = 0.034f, KeyTravel = 0.0025f, KeyH = 0.007f;
	private static readonly Vector3 DisplayCentre = new(0, 0.074f, Front);
	private static Vector3 KeyCentre(int i) => new((i % Cols - 1) * KeyPitch, 0.017f - (i / Cols) * KeyPitch, Front + KeyH * 0.5f);

	private const float ViewDistance = 0.5f, ViewFov = 46f, EaseIn = 0.55f, EaseOut = 0.4f;

	private readonly Node3D[] _keys = new Node3D[Keys.Length];
	private readonly MeshInstance3D[] _keyMesh = new MeshInstance3D[Keys.Length];
	private readonly MeshInstance3D[] _digitLit = new MeshInstance3D[4];
	private Node3D _display;
	private MeshInstance3D _ledPower, _ledRed, _ledGreen;
	private OmniLight3D _glow;
	private AudioStreamPlayer3D _audio;
	private RecentPicker _pressPick;
	private SamplePicker _denyPick;
	private readonly RandomNumberGenerator _rng = new();

	private PlayerController _player;
	private Camera3D _cam, _eye;
	private float _blend;          // 0 = at the eye, 1 = close on the pad
	private Tween _blendTween;
	private Transform3D _closeXf;
	private bool _busy, _doorOpen, _padLast;
	private string _override;      // the readout showing something other than the digits ("OPEn", a blink)
	private float _cursorBlink, _powerPoll;
	private static bool _hintShown;

	public override void _Ready()
	{
		Prompt = "Use the keypad";
		MaxDistance = 2.6f;
		PickRadius = 0.15f;
		PickOffset = new Vector3(0, -0.01f, Front);
		HighlightRoot = ".";
		base._Ready();
		if (Engine.IsEditorHint()) return;
		_audio = new AudioStreamPlayer3D { Name = "Audio", Bus = "Events", UnitSize = 2f, MaxDistance = 30f, Position = new Vector3(0, 0, Front) };
		AddChild(_audio);
		_rng.Randomize();
		RefreshPower();
		Paint();
	}

	public override bool CanInteract(PlayerController player) => base.CanInteract(player) && Powered && !_doorOpen && !Engaged && _cam == null;

	public override string GetPrompt(PlayerController player) => Powered ? "Use the keypad" : "A keypad. Dead.";

	public override void Interact(PlayerController player)
	{
		base.Interact(player);
		Engage(player);
	}

	/// <summary>The door stands open (or shut again in a preview): the pad goes green and stops taking codes.</summary>
	public void SetDoorOpen(bool open)
	{
		_doorOpen = open;
		Enabled = !open;
		RefreshPower();
		Paint();
	}

	private void RefreshPower()
	{
		bool was = Powered;
		Powered = _doorOpen || StoryManager.Instance is { Current: >= Checkpoint.Act7CabinBurning };
		LampState = !Powered ? Lamp.Off : _doorOpen ? Lamp.Open : LampState == Lamp.Denied ? Lamp.Denied : Lamp.Locked;
		if (Powered && !was && IsInsideTree()) GD.Print("[story] the bunker's keypad wakes");
	}

	// ------------------------------------------------------------------ engaging

	private void Engage(PlayerController player)
	{
		if (Engaged || _cam != null || player?.CameraRig?.Camera is not { } eye) return;
		_player = player;
		_eye = eye;
		Engaged = true;
		_busy = false;
		_override = null;
		// (2026-10-13, the owner: "the player should have to remember themselves") empty every time, never filled in
		Entered = "";
		Selected = 4;
		player.PlayerInput.BeginModal();
		// the number keys and the D-pad belong to the pad now, not to the hands (1-9 switch the item, D-pad down the blacklight)
		PlayerHands.Hold(this);
		_cam = new Camera3D
		{
			Name = "CloseView", TopLevel = true, Fov = eye.Fov, Near = 0.02f, Far = eye.Far,
			Environment = eye.Environment, Attributes = eye.Attributes, CullMask = eye.CullMask, KeepAspect = eye.KeepAspect,
		};
		AddChild(_cam);
		_cam.GlobalTransform = eye.GlobalTransform;
		_closeXf = CloseTransform(Selected);
		_cam.MakeCurrent();
		_blend = 0f;
		_blendTween?.Kill();
		_blendTween = CreateTween();
		_blendTween.TweenMethod(Callable.From<float>(v => _blend = v), 0f, 1f, EaseIn).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		Paint();
		GD.Print("[story] the keypad, close up");
		if (!_hintShown && UI.Subtitle.Instance is { } sub)
		{
			_hintShown = true;
			_ = sub.Show(_padLast
				? "D-pad: choose a key.   A: press it.   X: back a digit.   B: step back."
				: "0-9, or the arrows and E: press.   Backspace: undo.   Enter: try.   Esc: step back.", 0.4f, 4.5f, 1f);
		}
	}

	/// <summary>Steps back from the pad: the view eases back to the eye, then the player has control again.</summary>
	public void Leave()
	{
		if (!Engaged) return;
		Engaged = false;
		_busy = false;
		_override = null;
		Paint();
		if (_cam == null) { Release(); return; }
		_blendTween?.Kill();
		_blendTween = CreateTween();
		_blendTween.TweenMethod(Callable.From<float>(v => _blend = v), _blend, 0f, EaseOut).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		_blendTween.TweenCallback(Callable.From(Release));
	}

	private void Release()
	{
		if (_eye != null && IsInstanceValid(_eye)) _eye.MakeCurrent();
		_cam?.QueueFree();
		_cam = null;
		_eye = null;
		if (_player != null && IsInstanceValid(_player)) _player.PlayerInput.EndModal();
		PlayerHands.Release(this);
		_player = null;
		SetHighlightKey(-1);
	}

	/// <summary>The close view on key <paramref name="i"/>: the view's centre (the crosshair) sits on it, the pad square on.</summary>
	private Transform3D CloseTransform(int i)
	{
		Vector3 key = KeyCentre(i);
		var local = new Transform3D(Basis.Identity, new Vector3(key.X, key.Y, Front + ViewDistance));
		return GlobalTransform * local;
	}

	public override void _ExitTree()
	{
		base._ExitTree();
		if (!Engine.IsEditorHint() && _cam != null) { Engaged = false; Release(); }
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint()) return;
		float dt = (float)delta;
		if (!Powered)
		{
			_powerPoll -= dt;
			if (_powerPoll <= 0f) { _powerPoll = 0.5f; RefreshPower(); if (Powered) Paint(); }
		}
		if (_cam == null) return;
		// a cutscene took the player (or they're gone): let go at once
		if (_player == null || !IsInstanceValid(_player) || !_player.PlayerInput.Enabled || _eye == null || !IsInstanceValid(_eye))
		{
			Engaged = false;
			_blendTween?.Kill();
			Release();
			Paint();
			return;
		}
		// the view slides from key to key (gently: the owner gets headaches from strong camera motion)
		var goal = CloseTransform(Selected);
		bool still = GameSettings.Instance?.AutoTest ?? false;
		float k = still ? 1f : 1f - Mathf.Exp(-9f * dt);
		_closeXf = new Transform3D(GlobalBasis, _closeXf.Origin.Lerp(goal.Origin, k));
		float b = Mathf.Clamp(_blend, 0f, 1f);
		_cam.GlobalTransform = _eye.GlobalTransform.InterpolateWith(_closeXf, b);
		_cam.Fov = Mathf.Lerp(_eye.Fov, ViewFov, b);
		if (Engaged && !_busy)
		{
			_cursorBlink += dt;
			if (_cursorBlink > 1f) _cursorBlink -= 1f;
			PaintDisplay();
		}
	}

	// ------------------------------------------------------------------ input (keyboard keys, pad buttons; never the mouse)

	public override void _Input(InputEvent e)
	{
		if (e is InputEventJoypadButton { Pressed: true } || e is InputEventJoypadMotion jm && Mathf.Abs(jm.AxisValue) > 0.5f) _padLast = true;
		else if (e is InputEventKey { Pressed: true }) _padLast = false;
		if (!Engaged) return;
		// While it's up nothing reaches the game: the mouse neither looks nor clicks, keys don't fall through to the pause menu.
		// (The sticks are left out too: an axis repeats its "press" for as long as it's held; the D-pad steps.)
		if (e is InputEventMouse or InputEventJoypadMotion) { GetViewport().SetInputAsHandled(); return; }
		if (!e.IsPressed() || e.IsEcho()) { if (e is InputEventKey or InputEventJoypadButton or InputEventAction) GetViewport().SetInputAsHandled(); return; }

		if (e.IsActionPressed("pause") || e.IsActionPressed("ui_cancel") || e is InputEventJoypadButton { ButtonIndex: JoyButton.B }) Leave();
		else if (_busy) { }
		else if (e is InputEventKey key && DigitOf(key) is int d) Press(Array.IndexOf(Keys, d.ToString()));
		else if (e is InputEventKey { Keycode: Key.Backspace or Key.Delete } || e is InputEventJoypadButton { ButtonIndex: JoyButton.X }) Backspace();
		else if (e is InputEventKey { Keycode: Key.Enter or Key.KpEnter }) Press(Enter);
		else if (e is InputEventKey { Keycode: Key.KpMultiply or Key.Asterisk }) Press(Clear);
		else if (e.IsActionPressed("interact") || e.IsActionPressed("ui_accept")) Press(Selected);
		else if (e is InputEventJoypadButton { ButtonIndex: JoyButton.DpadLeft }) Move(-1, 0);
		else if (e is InputEventJoypadButton { ButtonIndex: JoyButton.DpadRight }) Move(1, 0);
		else if (e is InputEventJoypadButton { ButtonIndex: JoyButton.DpadUp }) Move(0, -1);
		else if (e is InputEventJoypadButton { ButtonIndex: JoyButton.DpadDown }) Move(0, 1);
		else if (e.IsActionPressed("move_left") || e.IsActionPressed("ui_left")) Move(-1, 0);
		else if (e.IsActionPressed("move_right") || e.IsActionPressed("ui_right")) Move(1, 0);
		else if (e.IsActionPressed("move_forward") || e.IsActionPressed("ui_up")) Move(0, -1);
		else if (e.IsActionPressed("move_back") || e.IsActionPressed("ui_down")) Move(0, 1);
		GetViewport().SetInputAsHandled();
	}

	private static int? DigitOf(InputEventKey k)
	{
		Key c = k.Keycode != Key.None ? k.Keycode : k.PhysicalKeycode;
		if (c >= Key.Key0 && c <= Key.Key9) return (int)(c - Key.Key0);
		if (c >= Key.Kp0 && c <= Key.Kp9) return (int)(c - Key.Kp0);
		return null;
	}

	private void Move(int dx, int dy)
	{
		int col = Mathf.Clamp(Selected % Cols + dx, 0, Cols - 1), row = Mathf.Clamp(Selected / Cols + dy, 0, Rows - 1);
		Selected = row * Cols + col;
		SetHighlightKey(Selected);
	}

	// ------------------------------------------------------------------ the keys

	/// <summary>Presses key <paramref name="i"/> as the player would (tests may call it directly while engaged).</summary>
	public void Press(int i)
	{
		if (!Engaged || _busy || i < 0 || i >= Keys.Length) return;
		Selected = i;
		SetHighlightKey(i);
		Sink(i);
		PlaySfx($"keypad_press_{_pressPick.Next(_rng, 8) + 1:00}", -10f, 0.03f);
		_cursorBlink = 0f;
		string k = Keys[i];
		if (i == Clear) Entered = "";
		else if (i == Enter) { Submit(); return; }
		else if (Entered.Length < 4) Entered += k;
		if (LampState == Lamp.Denied) LampState = Lamp.Locked;
		Paint();
	}

	private void Backspace()
	{
		if (Entered.Length == 0) { Sink(Clear); PlaySfx($"keypad_press_{_pressPick.Next(_rng, 8) + 1:00}", -12f, 0.03f); return; }
		Sink(Clear);
		PlaySfx($"keypad_press_{_pressPick.Next(_rng, 8) + 1:00}", -10f, 0.03f);
		Entered = Entered[..^1];
		Paint();
	}

	private void Sink(int i)
	{
		var key = _keys[i];
		if (key == null) return;
		Vector3 rest = KeyCentre(i);
		key.Position = rest;
		var tw = key.CreateTween();
		tw.TweenProperty(key, "position", rest + Vector3.Forward * KeyTravel, 0.045f).SetEase(Tween.EaseType.Out);
		tw.TweenProperty(key, "position", rest, 0.09f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
	}

	private async void Submit()
	{
		string code = CodeSource?.Invoke() ?? "0000";
		bool right = Entered == code;
		GD.Print($"[story] the keypad tried {(Entered.Length > 0 ? Entered : "(nothing)")} against {code}: {(right ? "open" : "no")}");
		_busy = true;
		if (right)
		{
			LampState = Lamp.Open;
			Paint();
			PlaySfx("keypad_accept_01", -6f, 0f);
			UI.SoundCaption.Heard(this, "keypad_accept", GlobalPosition);
			var cb = Unlocked;
			Unlocked = null;
			cb?.Invoke();
			await Wait(0.35f);
			_override = "OPEn";
			Paint();
			await Wait(0.75f);
			_busy = false;
			Leave();
			return;
		}
		PlaySfx($"keypad_deny_{_denyPick.Next(_rng, 3) + 1:00}", -8f, 0.01f);
		UI.SoundCaption.Heard(this, "keypad_deny", GlobalPosition);
		string shown = Entered.Length > 0 ? Entered : "----";
		Denying = true;
		for (int blink = 0; blink < 3 && Engaged; blink++)
		{
			LampState = Lamp.Denied; _override = shown; Paint();
			await Wait(0.16f);
			LampState = Lamp.Off; _override = "    "; Paint();
			await Wait(0.12f);
		}
		_override = null;
		Entered = "";
		LampState = Powered ? Lamp.Locked : Lamp.Off;
		Denying = false;
		_busy = false;
		Paint();
	}

	/// <summary>True for the second or so a wrong code buzzes and the red lamp flashes (then it clears).</summary>
	public bool Denying { get; private set; }

	private async System.Threading.Tasks.Task Wait(float seconds)
	{
		if (!IsInsideTree()) return;
		await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
	}

	private void PlaySfx(string name, float db, float jitter)
	{
		string path = $"res://assets/audio/sfx/{name}.wav";
		if (_audio == null || !ResourceLoader.Exists(path)) return;
		// a player of its own per sound, so a quick run of presses overlaps instead of cutting itself off
		var p = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), Bus = "Events", VolumeDb = db, UnitSize = 2f, MaxDistance = 30f,
			PitchScale = 1f + (float)_rng.RandfRange(-jitter, jitter), Position = _audio.Position };
		AddChild(p);
		p.Finished += p.QueueFree;
		p.Play();
	}

	// ------------------------------------------------------------------ the readout and the lamps

	private void Paint()
	{
		if (_display == null) return;
		PaintDisplay();
		SetLamp(_ledPower, Powered, new Color(1f, 0.62f, 0.12f), 1f);
		SetLamp(_ledRed, LampState is Lamp.Locked or Lamp.Denied, new Color(1f, 0.12f, 0.06f), LampState == Lamp.Denied ? 1.6f : 0.7f);
		SetLamp(_ledGreen, LampState == Lamp.Open, new Color(0.2f, 1f, 0.3f), 1.2f);
		if (_glow != null)
		{
			_glow.Visible = Powered;
			_glow.LightColor = LampState switch { Lamp.Open => new Color(0.3f, 1f, 0.4f), Lamp.Denied => new Color(1f, 0.15f, 0.08f), _ => new Color(1f, 0.25f, 0.1f) };
			_glow.LightEnergy = LampState switch { Lamp.Denied => 0.5f, Lamp.Open => 0.35f, _ => Engaged ? 0.18f : 0.08f };
		}
		if (!Engaged) SetHighlightKey(-1);
		else SetHighlightKey(Selected);
	}

	/// <summary>What the four digits show: the override, else the digits typed and (while engaged) a blinking dash where the next goes.</summary>
	public string DisplayText
	{
		get
		{
			if (!Powered) return "    ";
			if (_override != null) return _override.PadRight(4)[..4];
			if (_doorOpen) return "    ";
			string s = Entered;
			if (Engaged && s.Length < 4 && _cursorBlink < 0.55f) s += "_";
			return s.PadRight(4);
		}
	}

	private string _painted;
	private void PaintDisplay()
	{
		string text = DisplayText;
		if (text == _painted) return;
		_painted = text;
		for (int i = 0; i < 4; i++)
		{
			_digitLit[i]?.QueueFree();
			_digitLit[i] = null;
			int mask = Segments(text[i]);
			if (mask == 0) continue;
			var k = new MeshKit();
			k.Mat(LitSegment);
			AddSegments(k, DigitOrigin(i) + Vector3.Back * 0.0004f, mask);
			_digitLit[i] = k.CommitTo(_display, $"Digit{i}", false);
		}
	}

	private static void SetLamp(MeshInstance3D led, bool on, Color c, float energy)
	{
		if (led == null) return;
		led.MaterialOverride = on ? LampOn(c, energy) : null;
	}

	// seven segments: a top, b top right, c bottom right, d bottom, e bottom left, f top left, g middle
	private static int Segments(char ch) => ch switch
	{
		'0' or 'O' => 0b0111111, '1' => 0b0000110, '2' => 0b1011011, '3' => 0b1001111, '4' => 0b1100110,
		'5' => 0b1101101, '6' => 0b1111101, '7' => 0b0000111, '8' => 0b1111111, '9' => 0b1101111,
		'-' => 0b1000000, '_' => 0b0001000, 'P' => 0b1110011, 'E' => 0b1111001, 'n' => 0b1010100,
		_ => 0,
	};

	private const float SegW = 0.0105f, SegH = 0.0185f, SegT = 0.0022f;
	/// <summary>A digit's centre: the ghost segments sit just in front of the dark glass, the lit ones just in front of those.</summary>
	private static Vector3 DigitOrigin(int i) => DisplayCentre + new Vector3((i - 1.5f) * 0.0175f, 0, 0.0036f);

	private static void AddSegments(MeshKit k, Vector3 o, int mask)
	{
		// a slight forward slant, as LED readouts had
		Vector3 Sl(float x, float y) => o + new Vector3(x + y * 0.12f, y, 0);
		float hw = SegW * 0.5f, hh = SegH * 0.5f, t = SegT;
		void H(Vector3 c) => k.Box(c, new Vector3(SegW - t * 1.2f, t, 0.0006f));
		void V(Vector3 c) => k.Box(c, new Vector3(t, hh - t * 0.9f, 0.0006f));
		if ((mask & 1) != 0) H(Sl(0, hh));
		if ((mask & 2) != 0) V(Sl(hw, hh * 0.5f));
		if ((mask & 4) != 0) V(Sl(hw, -hh * 0.5f));
		if ((mask & 8) != 0) H(Sl(0, -hh));
		if ((mask & 16) != 0) V(Sl(-hw, -hh * 0.5f));
		if ((mask & 32) != 0) V(Sl(-hw, hh * 0.5f));
		if ((mask & 64) != 0) H(Sl(0, 0));
	}

	// ------------------------------------------------------------------ highlight on the chosen key

	private int _highlighted = -1;
	private static ShaderMaterial _keyGlow;
	private void SetHighlightKey(int i)
	{
		if (i == _highlighted) return;
		if (_highlighted >= 0 && _keyMesh[_highlighted] is { } old && IsInstanceValid(old)) old.MaterialOverlay = null;
		_highlighted = i;
		if (i < 0 || _keyMesh[i] is not { } m) return;
		_keyGlow ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/interact_highlight.gdshader") };
		_keyGlow.SetShaderParameter("fill", 0.14f);
		_keyGlow.SetShaderParameter("rim", 0.6f);
		m.MaterialOverlay = _keyGlow;
	}

	// ------------------------------------------------------------------ materials

	private static StandardMaterial3D _lit, _ghost, _glass, _steel, _keyMat, _rubber;
	private static readonly System.Collections.Generic.Dictionary<Color, StandardMaterial3D> _lamps = new();

	private static StandardMaterial3D LitSegment => _lit ??= new StandardMaterial3D
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.16f, 0.07f),
	};
	private static StandardMaterial3D GhostSegment => _ghost ??= new StandardMaterial3D
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.07f, 0.014f, 0.012f),
	};
	private static StandardMaterial3D Glass => _glass ??= new StandardMaterial3D
	{
		AlbedoColor = new Color(0.03f, 0.008f, 0.008f), Roughness = 0.12f, MetallicSpecular = 0.8f,
	};
	/// <summary>Brushed stainless gone dull: the metal texture under vertex colour, a little shine left.</summary>
	private static Material Steel => ProcTextures.MetalMat;
	private static StandardMaterial3D KeyMat => _keyMat ??= new StandardMaterial3D
	{
		VertexColorUseAsAlbedo = true, AlbedoTexture = ProcTextures.MetalMat.AlbedoTexture, Roughness = 0.62f, Metallic = 0.12f, MetallicSpecular = 0.5f,
		Uv1Scale = new Vector3(0.35f, 0.35f, 0.35f),
	};
	private static StandardMaterial3D Rubber => _rubber ??= new StandardMaterial3D
	{
		VertexColorUseAsAlbedo = true, Roughness = 0.95f,
	};
	private static StandardMaterial3D LampOn(Color c, float energy)
	{
		var key = new Color(c.R, c.G, c.B, energy);
		if (_lamps.TryGetValue(key, out var m)) return m;
		m = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = c * Mathf.Min(energy, 1f) + new Color(0.15f, 0.15f, 0.15f) * Mathf.Max(0f, energy - 1f) };
		m.AlbedoColor = m.AlbedoColor with { A = 1f };
		_lamps[key] = m;
		return m;
	}

	private static Font _keyFont;
	private static Font KeyFont => _keyFont ??= new SystemFont
	{
		FontNames = new[] { "Arial", "Helvetica", "Liberation Sans", "DejaVu Sans" }, FontWeight = 700,
		Antialiasing = TextServer.FontAntialiasing.Gray, GenerateMipmaps = true, AllowSystemFallback = true,
	};

	// ------------------------------------------------------------------ the model

	/// <summary>Builds the unit (also in the editor): the back box, hood, faceplate, readout, lamps, keys, screws and conduit.</summary>
	public void Build()
	{
		foreach (var c in GetChildren()) if (c.Name.ToString().StartsWith("KP_")) { RemoveChild(c); c.QueueFree(); }
		var rng = new RandomNumberGenerator { Seed = 4729_1998 };
		Color Grime(Color c, float amount) => c.Lerp(new Color(0.16f, 0.14f, 0.11f), amount * rng.RandfRange(0.6f, 1.1f));

		// the cast back box, its rain hood and the four masonry bolts through its flanges
		var k = new MeshKit();
		k.Mat(Steel);
		Color cast = new(0.34f, 0.36f, 0.33f);
		k.Color = cast;
		k.Box(new Vector3(0, 0, BoxD * 0.5f), new Vector3(BoxW, BoxH, BoxD));
		k.Color = Grime(cast, 0.35f);
		foreach (float sy in new[] { -1f, 1f })   // flanges top and bottom
			k.Box(new Vector3(0, sy * (BoxH * 0.5f + 0.012f), 0.004f), new Vector3(BoxW * 0.7f, 0.026f, 0.008f));
		k.Color = Grime(cast, 0.2f);
		// the hood: a sloped lid out over the face and two cheeks
		// (no shadows from it: the door lamp sits low to the right, and a cheek's shadow blacked out the whole right column of keys)
		var hood = new MeshKit();
		hood.Mat(Steel);
		hood.Color = k.Color;
		var hoodTilt = Basis.FromEuler(new Vector3(Mathf.DegToRad(14f), 0, 0));
		hood.Box(new Vector3(0, BoxH * 0.5f + 0.008f, BoxD + 0.012f), new Vector3(BoxW + 0.012f, 0.004f, 0.075f), 1f, hoodTilt);
		foreach (float sx in new[] { -1f, 1f })
			hood.Box(new Vector3(sx * (BoxW * 0.5f + 0.004f), BoxH * 0.5f - 0.02f, BoxD + 0.012f), new Vector3(0.004f, 0.05f, 0.06f));
		hood.CommitTo(this, "KP_Hood", false);
		k.Color = new Color(0.28f, 0.26f, 0.23f);
		foreach (float sy in new[] { -1f, 1f })
			foreach (float sx in new[] { -0.035f, 0.035f })
			{
				Vector3 p = new(sx, sy * (BoxH * 0.5f + 0.012f), 0.008f);
				k.Cylinder(p, p + Vector3.Back * 0.005f, 0.0065f, 0.006f, 6);
			}
		// the conduit out of the bottom, down to a junction box on the wall, two straps holding it
		Color pipe = new(0.4f, 0.4f, 0.37f);
		k.Color = Grime(pipe, 0.3f);
		Vector3 c0 = new(0.03f, -BoxH * 0.5f, 0.024f), c1 = new(0.03f, -0.42f, 0.024f);
		k.Cylinder(c0 + Vector3.Up * 0.004f, c1, 0.011f, 0.011f, 10);
		k.Color = Grime(pipe, 0.5f);
		k.Cylinder(c0 + Vector3.Down * 0.002f, c0 + Vector3.Down * 0.022f, 0.0145f, 0.0145f, 8);   // the gland nut
		foreach (float y in new[] { -0.2f, -0.33f })
		{
			k.Box(new Vector3(0.03f, y, 0.024f), new Vector3(0.026f, 0.012f, 0.026f));
			k.Box(new Vector3(0.03f, y, 0.003f), new Vector3(0.05f, 0.012f, 0.006f));
		}
		k.Color = Grime(cast, 0.5f);
		k.Box(new Vector3(0.03f, -0.46f, 0.025f), new Vector3(0.08f, 0.08f, 0.05f));
		k.Color = Grime(cast, 0.2f);
		k.Box(new Vector3(0.03f, -0.46f, 0.051f), new Vector3(0.072f, 0.072f, 0.004f));
		k.Color = new Color(0.3f, 0.28f, 0.25f);
		foreach (float sx in new[] { -0.03f, 0.03f })
			foreach (float sy in new[] { -0.03f, 0.03f })
				k.Cylinder(new Vector3(0.03f + sx, -0.46f + sy, 0.053f), new Vector3(0.03f + sx, -0.46f + sy, 0.056f), 0.004f, 0.004f, 6);

		// the stainless faceplate, dulled and grimy toward the bottom, its four tamper screws
		Color steel = new(0.62f, 0.62f, 0.6f);
		k.Color = Grime(steel, 0.25f);
		k.Box(new Vector3(0, 0.035f, BoxD + PlateT * 0.5f), new Vector3(PlateW, PlateH * 0.5f, PlateT));
		k.Color = Grime(steel, 0.45f);
		k.Box(new Vector3(0, -0.0535f, BoxD + PlateT * 0.5f), new Vector3(PlateW, PlateH * 0.5f, PlateT));
		foreach (float sx in new[] { -1f, 1f })
			foreach (float sy in new[] { -1f, 1f })
			{
				Vector3 p = new(sx * (PlateW * 0.5f - 0.009f), -0.0095f + sy * (PlateH * 0.5f - 0.009f), Front);
				k.Color = new Color(0.5f, 0.49f, 0.46f);
				k.Cylinder(p, p + Vector3.Back * 0.0022f, 0.0048f, 0.0042f, 10);
				k.Color = new Color(0.12f, 0.11f, 0.1f);
				float a = rng.RandfRange(0f, Mathf.Pi);
				k.Box(p + Vector3.Back * 0.0023f, new Vector3(0.007f, 0.0011f, 0.0006f), 1f, Basis.FromEuler(new Vector3(0, 0, a)));
			}
		// the readout's bezel and window, the lamps' bezels
		k.Color = new Color(0.1f, 0.1f, 0.1f);
		k.Box(DisplayCentre + Vector3.Back * 0.0015f, new Vector3(0.086f, 0.032f, 0.003f));
		k.Color = new Color(0.45f, 0.45f, 0.43f);
		for (int i = 0; i < 3; i++)
		{
			Vector3 p = LampPos(i);
			k.Cylinder(p, p + Vector3.Back * 0.0018f, 0.0045f, 0.0045f, 10);
		}
		k.CommitTo(this, "KP_Body");

		// the readout's dark red glass, and every segment's ghost behind it
		_display = new Node3D { Name = "KP_Display" };
		AddChild(_display);
		var g = new MeshKit();
		g.Mat(Glass);
		g.Box(DisplayCentre + Vector3.Back * 0.0032f, new Vector3(0.078f, 0.026f, 0.0004f));
		g.CommitTo(_display, "Glass", false);
		var gh = new MeshKit();
		gh.Mat(GhostSegment);
		for (int i = 0; i < 4; i++) AddSegments(gh, DigitOrigin(i), 0b1111111);
		gh.CommitTo(_display, "Ghost", false);

		// the three lamps: power (amber), locked (red), open (green), dull lenses when off
		MeshInstance3D Lamp(int i, Color off)
		{
			var lk = new MeshKit();
			lk.Mat(Rubber);
			lk.Color = off;
			Vector3 p = LampPos(i);
			lk.Cylinder(p + Vector3.Back * 0.0018f, p + Vector3.Back * 0.0042f, 0.0031f, 0.0024f, 10);
			return lk.CommitTo(this, $"KP_Lamp{i}", false);
		}
		_ledPower = Lamp(0, new Color(0.12f, 0.08f, 0.03f));
		_ledRed = Lamp(1, new Color(0.3f, 0.06f, 0.05f));
		_ledGreen = Lamp(2, new Color(0.02f, 0.06f, 0.025f));
		// three tiny legends under the lamps, stamped into the plate
		string[] legends = { "PWR", "LOCK", "OPEN" };
		for (int i = 0; i < 3; i++)
			AddChild(Engrave($"KP_Legend{i}", legends[i], LampPos(i) + new Vector3(0, -0.0078f, 0.0004f), 22, 0.00016f, 0.75f));

		// the keys: square stainless caps on a rubber seat, engraved and paint-filled, each worn its own way
		for (int i = 0; i < Keys.Length; i++)
		{
			var key = new Node3D { Name = $"KP_Key{i}", Position = KeyCentre(i) };
			AddChild(key);
			_keys[i] = key;
			var seat = new MeshKit();
			seat.Mat(Rubber);
			seat.Color = new Color(0.07f, 0.07f, 0.07f);
			seat.Box(new Vector3(0, 0, -KeyH * 0.5f + 0.0008f), new Vector3(KeySize + 0.004f, KeySize + 0.004f, 0.0016f));
			seat.CommitTo(key, "Seat", false);
			var cap = new MeshKit();
			cap.Mat(KeyMat);
			float wear = rng.RandfRange(0.15f, 0.55f) + (i / Cols) * 0.06f;   // the lower rows dirtier, nearer the splash
			cap.Color = Grime(new Color(0.7f, 0.7f, 0.68f), wear);
			cap.Box(new Vector3(0, 0, -0.0004f), new Vector3(KeySize, KeySize, KeyH - 0.0008f));
			cap.Color = Grime(new Color(0.78f, 0.78f, 0.75f), wear * 0.6f);   // the face, rubbed a little brighter by thumbs
			cap.Box(new Vector3(0, 0, KeyH * 0.5f - 0.0004f), new Vector3(KeySize - 0.0024f, KeySize - 0.0024f, 0.0008f));
			_keyMesh[i] = cap.CommitTo(key, "Cap", true);
			string text = Keys[i];
			float ink = rng.RandfRange(0.72f, 0.95f);
			key.AddChild(Engrave("Numeral", text, new Vector3(0, i >= Clear && i != 10 ? 0.0025f : 0f, KeyH * 0.5f + 0.0003f), text == "*" ? 110 : 64, 0.00023f, ink));
			if (i == Clear) key.AddChild(Engrave("Sub", "CLEAR", new Vector3(0, -0.0085f, KeyH * 0.5f + 0.0003f), 20, 0.00019f, ink));
			if (i == Enter) key.AddChild(Engrave("Sub", "ENTER", new Vector3(0, -0.0085f, KeyH * 0.5f + 0.0003f), 20, 0.00019f, ink));
		}

		// the status lamps' faint spill on the plate and keys
		_glow = new OmniLight3D { Name = "KP_Glow", Position = LampPos(1) + Vector3.Back * 0.03f, OmniRange = 0.22f, LightEnergy = 0.08f, LightColor = new Color(1f, 0.25f, 0.1f), Visible = false, ShadowEnabled = false };
		AddChild(_glow);

		// rust weeping from the bottom flange's bolts, and the hood's drip line down the face
		if (!Engine.IsEditorHint())
		{
			BunkerKit.AddDecal(this, BunkerTextures.RustRun(), new Vector3(-0.02f, -0.26f, 0f), Vector3.Back, Vector3.Down, new Vector2(0.06f, 0.2f), 0.05f, new Color(0.7f, 0.6f, 0.55f, 0.55f)).Name = "KP_Rust";
			BunkerKit.AddDecal(this, BunkerTextures.Streak(), new Vector3(0, 0.02f, Front), Vector3.Back, Vector3.Down, new Vector2(0.14f, 0.26f), 0.03f, new Color(1, 1, 1, 0.28f)).Name = "KP_Grime";
		}
		_painted = null;
		Paint();
	}

	private static Vector3 LampPos(int i) => new((i - 1) * 0.026f, 0.041f, Front);

	/// <summary>Engraved, paint-filled lettering: shaded, so it is dark in the dark and reads under a light.</summary>
	private static Label3D Engrave(string name, string text, Vector3 at, int size, float pixel, float ink) => new()
	{
		Name = name, Text = text, Position = at, Font = KeyFont, FontSize = size, PixelSize = pixel, OutlineSize = 0,
		Modulate = new Color(0.05f, 0.045f, 0.04f, ink), Shaded = true, DoubleSided = false, AlphaCut = Label3D.AlphaCutMode.Discard,
		TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
	};
}
