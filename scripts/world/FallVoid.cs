using System.Collections.Generic;
using Godot;

namespace ProjectDS.World;

/// <summary>
/// The end of Act 6's stair loop (2026-10-15, the owner: "make the fall from the loop staircase a temporary infinite fall:
/// when you hit the ground you fall into / through it ... you keep falling through lights flashing ... then after a few
/// seconds you black out"). Two pieces, both built in code and torn down after:
/// <list type="bullet">
/// <item><b>The sink</b> (<see cref="Sink"/>): where they come down, the forest floor gives way: a dark funnel opening under
/// them, its rings turning inward, the litter round its lip drawn toward the middle, black at the centre.</item>
/// <item><b>The void</b> (<see cref="Tunnel"/>): hung on the camera, a long tube of streaking light rushing at the eye,
/// turning slowly, the streaks pale blue-white with a little amber and the odd red, the rush quickening; an opaque black
/// sleeve outside it so nothing of the world shows; and the flashes (<see cref="Flash"/>, a screen wash under the game's
/// own post-process, so the tape look stays on top). With Reduce Flashing they're fewer and softer, never near white.</item>
/// </list>
/// </summary>
public static class FallVoid
{
	private static ShaderMaterial _streakMat, _sinkMat;
	private static StandardMaterial3D _sleeveMat;

	// The void's light: mostly black, the streaks sparse, dim and cold (deep blue-grey, a sickly green-white, now and then
	// the game's red), bending as the tube twists, each with a fainter ghost of itself trailing behind (double vision);
	// the tube's wall breathing in and out; `travel` is the distance fallen (driven from code: lulls and surges).
	private const string StreakShader = @"shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, fog_disabled, shadows_disabled;
uniform float travel = 0.0;
uniform float flash = 0.0;
uniform float glow = 0.5;
uniform float twist = 0.0;
uniform float breathe = 0.0;
uniform float hue = 0.0;
uniform float density = 0.74;
uniform float ghost = 0.35;
uniform float flames = 0.0;   // (2026-10-24, Act 24's end: the wendigo blown apart) 1 = the colours of fire, flickering and licking
float h(float n) { return fract(sin(n * 127.1) * 43758.5453); }
void vertex() {
	float along = VERTEX.y;
	float ang = atan(VERTEX.z, VERTEX.x) + twist * along * 0.02;
	float rad = length(VERTEX.xz) * (1.0 + breathe * (0.22 * sin(along * 0.11 + TIME * 1.3) + 0.12 * sin(along * 0.37 - TIME * 2.1)));
	VERTEX.x = cos(ang) * rad;
	VERTEX.z = sin(ang) * rad;
}
float streaks(vec2 uv, float t) {
	float cols = 56.0;
	float a = uv.x * cols;
	float c = floor(a);
	float r0 = h(c), r1 = h(c + 31.7), r2 = h(c + 77.3);
	float v = uv.y * (14.0 + 10.0 * r0) + t * (0.5 + 0.9 * r1) + r2 * 9.0;
	float s = fract(v);
	float len = 0.08 + 0.3 * r2;
	float streak = smoothstep(0.0, 0.04, s) * smoothstep(len, 0.0, s);
	float x = fract(a) - 0.5;
	float w = exp(-x * x / (0.003 + 0.01 * r0));
	float on = step(density, h(c + floor(v) * 3.1));
	return streak * w * on;
}
void fragment() {
	float s1 = streaks(UV, travel);
	float s2 = streaks(UV + vec2(0.004 + 0.01 * ghost, 0.0), travel - 0.35 - ghost) * ghost;   // its ghost (a smear, longer and stronger as it slows)
	float r1 = h(floor(UV.x * 56.0) + 31.7), r2 = h(floor(UV.x * 56.0) + 77.3);
	// (2026-10-15, the owner asked for orange streaks) ember orange, a deeper amber, now and then a hot
	// yellow-orange; the game's red still the rare accent
	vec3 cold = mix(vec3(1.0, 0.42, 0.06), vec3(0.85, 0.24, 0.03), step(0.6, r1));
	cold = mix(cold, vec3(1.0, 0.68, 0.22), step(0.88, r1));
	cold = mix(cold, vec3(0.75, 0.08, 0.06), step(0.93, r2));
	cold = mix(cold, cold * vec3(1.0, 0.7, 0.45), hue * 0.35);   // (the hue drift stays in the warm range)
	float d = length(VERTEX);
	float fade = smoothstep(0.6, 6.0, d) * exp(-d / 55.0);
	// fire: each streak deep red to orange, the rare one white-hot, flickering; and the tube's wall itself licked with
	// flame rolling past (a low, slow red-orange wash, brighter in tongues)
	vec3 fire = mix(vec3(0.85, 0.06, 0.01), vec3(1.0, 0.3, 0.02), smoothstep(0.15, 0.6, r1));
	fire = mix(fire, vec3(1.0, 0.58, 0.08), step(0.72, r1));
	fire = mix(fire, vec3(1.0, 0.86, 0.6), step(0.94, r2));
	float flick = 0.7 + 0.3 * sin(TIME * (14.0 + 9.0 * r2) + r1 * 40.0);
	vec3 col = mix(cold, fire * flick * 1.25, flames);
	float tongue = sin(UV.y * 34.0 - travel * 1.7 + sin(UV.x * 31.4 + TIME * 1.3) * 2.2) * 0.5 + 0.5;
	tongue *= sin(UV.x * 18.85 + UV.y * 9.0 - travel * 0.6 + TIME * 0.8) * 0.5 + 0.5;
	vec3 lick = mix(vec3(0.55, 0.03, 0.0), vec3(1.0, 0.36, 0.03), tongue) * pow(tongue, 2.0) * 0.3 * flames;
	ALBEDO = (col * (s1 + s2) + lick) * fade * glow * (1.0 + 2.0 * flash);
}";

	// The ground giving way: litter spiralling into a black middle that widens as it opens (drawn over the floor's own
	// litter and decals so the funnel always reads).
	private const string SinkShader = @"shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, depth_test_disabled, fog_disabled, shadows_disabled;
uniform float open = 0.0;
uniform vec3 litter = vec3(0.3, 0.25, 0.17);
void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float r = length(p);
	float ang = atan(p.y, p.x);
	float sp = sin(ang * 3.0 + log(max(r, 0.02)) * 9.0 - TIME * (2.0 + 4.0 * open)) * 0.5 + 0.5;
	float sink = smoothstep(1.0, 0.0, r);
	float hole = smoothstep(0.8 * open + 0.05, 0.02, r);
	vec3 rim = litter * mix(0.2, 1.0, sp) * (1.0 - sink * 0.85);
	ALBEDO = mix(rim, vec3(0.0), hole);
	ALPHA = clamp((1.0 - smoothstep(0.65, 1.0, r)) * min(open * 2.0, 1.0), 0.0, 1.0);
}";

	// The pull, on the screen itself (under the post-process, so the tape look stays on top): the whole view swirled
	// and pinched toward the middle (the trees leaning in over them), the light draining, a slow roll, and an iris
	// closing to a point (the sky above shrinking as they go down).
	private const string WarpShader = @"shader_type canvas_item;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform float swirl = 0.0;
uniform float pinch = 0.0;
uniform float dark = 0.0;
uniform float iris = 2.0;
uniform float flash = 0.0;
void fragment() {
	vec2 aspect = vec2(SCREEN_PIXEL_SIZE.y / SCREEN_PIXEL_SIZE.x, 1.0);
	vec2 c = (SCREEN_UV - 0.5) * aspect;
	float r = length(c);
	float ang = atan(c.y, c.x) + swirl * pow(max(0.0, 1.0 - r), 2.0) + swirl * 0.15 * sin(TIME * 0.7 + r * 6.0);
	float rr = r * (1.0 - pinch * 0.6 * (1.0 - smoothstep(0.0, 0.9, r)));
	vec2 uv = vec2(cos(ang), sin(ang)) * rr / aspect + 0.5;
	vec3 col = texture(screen_tex, uv).rgb;
	col *= 1.0 - dark;
	col = mix(col, vec3(dot(col, vec3(0.33))), dark * 0.6);
	float mask = smoothstep(iris, iris - 0.06, r);
	col = col * mask + vec3(0.55, 0.62, 0.7) * flash * 0.4;
	COLOR = vec4(col, 1.0);
}";

	private static ShaderMaterial StreakMat => _streakMat ??= new ShaderMaterial { Shader = new Shader { Code = StreakShader } };
	private static ShaderMaterial SinkMat => _sinkMat ??= new ShaderMaterial { Shader = new Shader { Code = SinkShader } };
	private static StandardMaterial3D SleeveMat => _sleeveMat ??= new StandardMaterial3D
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Colors.Black, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		DisableFog = true, DisableReceiveShadows = true,
	};

	/// <summary>Draws all three materials once, tiny, in front of the camera (two frames), so the fall never stalls on a
	/// first-time shader compile. Call a second or two before they're needed.</summary>
	public static Node3D Prewarm(Camera3D cam)
	{
		var n = new Node3D { Name = "FallVoidWarm" };
		cam.AddChild(n);
		n.Position = new Vector3(0, 0, -0.6f);
		n.Scale = Vector3.One * 0.001f;
		n.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 8, Rings = 1 }, MaterialOverride = StreakMat });
		n.AddChild(new MeshInstance3D { Mesh = new QuadMesh(), MaterialOverride = SinkMat });
		n.AddChild(new MeshInstance3D { Mesh = new BoxMesh(), MaterialOverride = SleeveMat });
		// the screen warp too: a 2 px rect for the warm-up frames
		var layer = new CanvasLayer { Layer = 8 };
		n.AddChild(layer);
		layer.AddChild(new ColorRect { Material = WarpMat, Size = new Vector2(2, 2), MouseFilter = Control.MouseFilterEnum.Ignore });
		return n;
	}

	/// <summary>The funnel opening in the forest floor at <paramref name="at"/> (ground height), <paramref name="radius"/> m.</summary>
	public static MeshInstance3D Sink(Node parent, Vector3 at, float radius)
	{
		var m = new MeshInstance3D
		{
			Name = "FallSink",
			Mesh = new PlaneMesh { Size = new Vector2(radius * 2f, radius * 2f) },
			MaterialOverride = (ShaderMaterial)SinkMat.Duplicate(),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			TopLevel = true,
		};
		parent.AddChild(m);
		m.GlobalPosition = at + Vector3.Up * 0.04f;
		return m;
	}

	public sealed class TunnelRig
	{
		public Node3D Root;
		public ShaderMaterial Streaks;
		public void Set(float travel, float flash, float glow, float twist, float breathe, float hue, float density = 0.74f, float ghost = 0.35f)
		{
			Streaks.SetShaderParameter("density", density);
			Streaks.SetShaderParameter("ghost", ghost);
			Streaks.SetShaderParameter("travel", travel);
			Streaks.SetShaderParameter("flash", flash);
			Streaks.SetShaderParameter("glow", glow);
			Streaks.SetShaderParameter("twist", twist);
			Streaks.SetShaderParameter("breathe", breathe);
			Streaks.SetShaderParameter("hue", hue);
		}
	}

	/// <summary>The tube of light hung on the camera, looking down its length, with the black sleeve round it.</summary>
	public static TunnelRig Tunnel(Camera3D cam)
	{
		var root = new Node3D { Name = "FallVoid" };
		cam.AddChild(root);
		var mat = (ShaderMaterial)StreakMat.Duplicate();
		// along the camera's view (-Z): the cylinder's axis is Y, so tipped 90 degrees, reaching from just behind the eye
		var tube = new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = 3.2f, BottomRadius = 3.2f, Height = 240f, RadialSegments = 48, Rings = 24, CapTop = false, CapBottom = false },
			MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		var sleeve = new MeshInstance3D
		{
			Mesh = new CylinderMesh { TopRadius = 5f, BottomRadius = 5f, Height = 260f, RadialSegments = 24, Rings = 1 },
			MaterialOverride = SleeveMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		foreach (var m in new[] { tube, sleeve })
		{
			root.AddChild(m);
			m.RotationDegrees = new Vector3(90f, 0f, 0f);
			m.Position = new Vector3(0, 0, -((CylinderMesh)m.Mesh).Height * 0.5f + 4f);
		}
		return new TunnelRig { Root = root, Streaks = mat };
	}

	private static ShaderMaterial _warpMat;
	private static ShaderMaterial WarpMat => _warpMat ??= new ShaderMaterial { Shader = new Shader { Code = WarpShader } };

	/// <summary>The screen warp for the pull into the ground (see <see cref="WarpShader"/>): a full-screen rect on a layer
	/// under the post-process; its material's uniforms are driven by the caller.</summary>
	public static ShaderMaterial Warp(Node parent)
	{
		var layer = new CanvasLayer { Name = "FallWarp", Layer = 8 };
		parent.AddChild(layer);
		var mat = (ShaderMaterial)WarpMat.Duplicate();
		var rect = new ColorRect { Material = mat, MouseFilter = Control.MouseFilterEnum.Ignore };
		rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(rect);
		return mat;
	}

	private static StandardMaterial3D _eyeMat;

	/// <summary>A pair of eyes in the dark (the thing from the woods, glimpsed as they fall past it): two small red points
	/// a hand apart, unshaded, no fog. Moved by the caller.</summary>
	public static Node3D Eyes(Node parent)
	{
		_eyeMat ??= new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.14f, 0.08f), DisableFog = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};
		var n = new Node3D { Name = "VoidEyes" };
		parent.AddChild(n);
		foreach (float x in new[] { -0.07f, 0.07f })
			n.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.025f, Height = 0.05f, RadialSegments = 8, Rings = 4 }, MaterialOverride = _eyeMat, Position = new Vector3(x, 0, 0) });
		return n;
	}

	/// <summary>
	/// (2026-10-24, the owner: Act 24's end, "have the wendigo explode and use the transition from ... falling back down to
	/// earth with that transition space spiral effect ... the colors of flames") Blown into the void: the same tube and
	/// screen swirl as Act 6's fall, in the colours of fire, rushing at full force and slowing, then a fade to black (held:
	/// the caller moves on). The player is taken far below the world for it (the next level loads over them).
	/// </summary>
	public static async System.Threading.Tasks.Task FlameFall(Node owner, Player.PlayerController player, float seconds, System.Threading.CancellationToken ct)
	{
		var rig = player?.CameraRig;
		var cam = rig?.Camera;
		if (cam == null) return;
		bool reduce = Systems.GameSettings.Instance?.ReduceFlashing ?? false;
		bool motion = Systems.GameSettings.Instance?.HeadMotion ?? true;
		cam.CullMask &= ~Player.HeldItem.HeldLayer;
		foreach (var n in owner.GetTree().Root.FindChildren("*", nameof(CanvasLayer), true, false))
			if (n is UI.Compass or UI.EquippedItemHud or UI.InteractPrompt or UI.StaminaBar or UI.FlamethrowerGauge) ((CanvasLayer)n).Visible = false;
		// (the flamethrower's wand is its own, not on the held layer: put away with it)
		if (Player.Flamethrower.Instance is { } ft && GodotObject.IsInstanceValid(ft)) ft.ProcessMode = Node.ProcessModeEnum.Disabled;
		if (cam.GetNodeOrNull<Node3D>("FlamethrowerWand") is { } wand) wand.Visible = false;
		player.SetPhysicsProcess(false);
		player.Velocity = Vector3.Zero;
		float down = -1500f - player.GlobalPosition.Y;
		player.GlobalPosition += Vector3.Up * down;
		rig.ShiftBy(Vector3.Up * down);
		var tunnel = Tunnel(cam);
		tunnel.Streaks.SetShaderParameter("flames", 1f);
		var root = Systems.Cutscene.SceneRoot(owner);
		var wash = Flash(root);
		wash.Color = new Color(1f, 0.42f, 0.1f, 0f);
		var warp = Warp(root);
		var sounds = new List<AudioStreamPlayer>();
		AudioStreamPlayer Play(string path, float db, float pitch, bool loop)
		{
			if (!ResourceLoader.Exists(path)) return null;
			var st = GD.Load<AudioStream>(path);
			if (loop && st is AudioStreamWav w) { w = (AudioStreamWav)w.Duplicate(); w.LoopMode = AudioStreamWav.LoopModeEnum.Forward; w.LoopEnd = Mathf.RoundToInt(w.GetLength() * w.MixRate); st = w; }
			var a = new AudioStreamPlayer { Stream = st, VolumeDb = db, PitchScale = pitch, Bus = "Events" };
			root.AddChild(a);
			a.Play();
			sounds.Add(a);
			return a;
		}
		var rng = new RandomNumberGenerator();
		var rush = Play($"res://assets/audio/sfx/fall_void_{rng.RandiRange(1, 3):00}.wav", 0f, 1.1f, false);
		var roar = Play("res://assets/audio/ambient/fire_crackle_loop.wav", 2f, 0.7f, true);
		var drone = Play("res://assets/audio/ambient/pressure_loop.wav", -30f, 0.6f, true);
		var fader = Systems.StoryBeat.Fader(owner);
		double burst = 2.2, slowing = 2.0, fadeAt = seconds - 2.4, t = 0, nextFlash = 0.0;
		float travel = 0f, spin = 0f, flash = 0f, speed = 38f;
		bool fading = false;
		while (t < seconds)
		{
			float dt = (float)owner.GetProcessDeltaTime();
			float calm = Mathf.SmoothStep(0f, 1f, (float)((t - burst) / slowing));
			speed = Mathf.Lerp(speed, Mathf.Lerp(38f, 4f, calm), 1f - Mathf.Exp(-dt * 1.6f));
			travel += speed * dt;
			if (t >= nextFlash && t < fadeAt)
			{
				flash = (reduce ? 0.3f : rng.RandfRange(0.7f, 1f)) * Mathf.Lerp(1f, 0.4f, calm);
				nextFlash = t + (reduce ? rng.RandfRange(0.9f, 1.5f) : Mathf.Lerp(rng.RandfRange(0.1f, 0.3f), rng.RandfRange(1.2f, 2.2f), calm));
			}
			flash = Mathf.MoveToward(flash, 0f, dt * 4f);
			wash.Color = wash.Color with { A = flash * (reduce ? 0.07f : Mathf.Lerp(0.42f, 0.16f, calm)) };
			float twist = Mathf.Lerp(3.8f, 2.2f, calm) * Mathf.Sin((float)t * Mathf.Lerp(1f, 0.4f, calm));
			spin += dt * Mathf.Lerp(2.0f, 0.6f, calm);
			tunnel.Set(travel, flash * (reduce ? 0.3f : 1f), Mathf.Lerp(1.45f, 0.85f, calm), twist, Mathf.Lerp(0.3f, 0.6f, calm), 0.5f, Mathf.Lerp(0.45f, 0.7f, calm), Mathf.Lerp(0.25f, 0.6f, calm));
			tunnel.Root.Rotation = new Vector3(0f, 0f, spin);
			tunnel.Root.Position = motion ? new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f), 0f) * (0.008f + 0.0012f * speed) : Vector3.Zero;
			warp.SetShaderParameter("swirl", (motion ? 1f : 0.4f) * Mathf.Lerp(2.8f * Mathf.Sin((float)t * 2.2f), 0.8f * Mathf.Sin((float)t * 0.45f), calm));
			warp.SetShaderParameter("pinch", Mathf.Lerp(0.55f, 0.3f, calm));
			warp.SetShaderParameter("dark", Mathf.Lerp(0f, 0.12f, calm));
			warp.SetShaderParameter("flash", flash * (reduce ? 0.3f : 0.6f));
			rig.FovSwim = motion ? Mathf.Lerp(42f, 24f, calm) + 0.4f * speed : 26f;
			if (GodotObject.IsInstanceValid(rush)) rush.VolumeDb = Mathf.Lerp(0f, -12f, calm);
			if (GodotObject.IsInstanceValid(roar)) roar.VolumeDb = Mathf.Lerp(2f, -8f, calm);
			if (GodotObject.IsInstanceValid(drone)) drone.VolumeDb = Mathf.Lerp(-30f, -8f, calm);
			if (!fading && t >= fadeAt)
			{
				fading = true;
				if (fader != null) _ = Systems.Cutscene.Run(owner, c => fader.Fade(1f, 2.2f, c));
			}
			if (fading) foreach (var a in sounds) if (GodotObject.IsInstanceValid(a)) a.VolumeDb -= dt * 14f;
			await Systems.Cutscene.Frame(owner, ct);
			t += dt;
		}
		FlameFalls++;
		GD.Print($"[story] blown into the void: {seconds:0.0} s of the flame spiral");
	}

	/// <summary>For tests: how many flame falls have run.</summary>
	public static int FlameFalls { get; private set; }

	/// <summary>The flashes' screen wash: on a layer under the post-process (10), so the tape look stays on top.</summary>
	public static ColorRect Flash(Node parent)
	{
		var layer = new CanvasLayer { Name = "FallFlash", Layer = 9 };
		parent.AddChild(layer);
		var rect = new ColorRect { Color = new Color(0.82f, 0.88f, 1f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
		rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(rect);
		return rect;
	}
}
