using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ProjectDS.Player;
using ProjectDS.UI;
using ProjectDS.World;

namespace ProjectDS.Systems;

/// <summary>
/// The scaffolding every story beat shares, in one place: finding the fader,
/// the atmosphere, the post-process screen, the cabin and the player (by group,
/// cached, with a one-time by-name fallback that registers the group), showing a
/// caption, reaching a checkpoint, and building trigger volumes in code.
///
/// Groups used: "screen_fader", "atmosphere", "post_screen", "cabin", "player".
/// GameFlow registers the first three from its exported paths at level start.
/// </summary>
public static class StoryBeat
{
	public const uint PlayerLayer = 1u << 1;   // physics layer 2 "Player"

	public static StoryManager Story => StoryManager.Instance;

	public static PlayerController Player(Node n) => n.GetTree().GetFirstNodeInGroup("player") as PlayerController;
	public static ScreenFader Fader(Node n) => Find<ScreenFader>(n, "screen_fader", "ScreenFader");
	public static ForestAtmosphere Atmosphere(Node n) => Find<ForestAtmosphere>(n, "atmosphere", "Atmosphere");
	public static Cabin Cabin(Node n) => n.GetTree().GetFirstNodeInGroup("cabin") as Cabin;

	/// <summary>The full-screen post-process material (vignette, shadow tint), or null.</summary>
	public static ShaderMaterial PostMaterial(Node n) => Find<ColorRect>(n, "post_screen", "Screen")?.Material as ShaderMaterial;

	private static T Find<T>(Node n, string group, string fallbackName) where T : Node
	{
		var tree = n.GetTree();
		if (tree.GetFirstNodeInGroup(group) is T found) return found;
		// Fallback for scenes that don't register the group (previews): look once, then register.
		if (tree.CurrentScene?.FindChild(fallbackName, true, false) is not T byName) return null;
		byName.AddToGroup(group);
		return byName;
	}

	/// <summary>A narration caption on the fader (no title). Completes immediately if there is no fader.
	/// Pass the cutscene's token so a cancelled sequence takes its caption down with it.</summary>
	public static Task Caption(Node owner, string text, float fadeIn, float hold, float fadeOut, CancellationToken ct = default)
		=> Fader(owner)?.ShowCaption("", text, fadeIn, hold, fadeOut, ct) ?? Task.CompletedTask;

	/// <summary>A second line under the caption band (a voice echoing the first), so two overlapping
	/// captions render as two lines instead of one overwriting the other.</summary>
	public static Task Echo(Node owner, string text, float fadeIn, float hold, float fadeOut, CancellationToken ct = default)
		=> Fader(owner)?.ShowEcho(text, fadeIn, hold, fadeOut, ct) ?? Task.CompletedTask;

	/// <summary>Reaches a checkpoint at the player's current position and view.</summary>
	public static void ReachCheckpoint(PlayerController p, Checkpoint cp)
		=> Story?.ReachCheckpoint(cp, p.GlobalPosition, p.CameraRig.Yaw);

	public static void SetMood(Node n, ForestAtmosphere.Mood mood, float seconds)
		=> Atmosphere(n)?.SetMood(mood, seconds);

	/// <summary>The lighting mood the saved story implies (applied instantly on Continue).</summary>
	public static ForestAtmosphere.Mood ExpectedMood(StoryManager s)
	{
		if (s == null) return ForestAtmosphere.Mood.Auto;
		if (s.Current >= Checkpoint.Act11GiantEncounter) return ForestAtmosphere.Mood.Dawn;
		if (s.HasFlag(StoryManager.Flag.Act6NightFell)) return ForestAtmosphere.Mood.Night;
		if (Act6Revealed(s)) return ForestAtmosphere.Mood.Menacing;
		if (s.HasFlag(StoryManager.Flag.DawnBroke)) return ForestAtmosphere.Mood.Dawn;
		return ForestAtmosphere.Mood.Auto;
	}

	/// <summary>Act 6's clearing turns once the bridge is crossed with the newel post taken.</summary>
	public static bool Act6Revealed(StoryManager s) => s is { NewelPostTaken: true } && s.Current >= Checkpoint.Act6BridgeCrossed;

	/// <summary>Whether the storm is raging in the saved story.</summary>
	public static bool StormShouldRage(StoryManager s)
		=> s != null && s.HasFlag(StoryManager.Flag.StormStarted) && !s.HasFlag(StoryManager.Flag.DawnBroke);

	/// <summary>Whether the cabin door has been broken open in the saved story.</summary>
	public static bool CabinDoorOpen(StoryManager s)
		=> s != null && (s.HasFlag(StoryManager.Flag.CabinDoorOpen) || s.Current >= Checkpoint.Act5CabinEntered);

	/// <summary>
	/// A player-only trigger volume built in code (layer none, mask Player). Parent it where the
	/// beat lives; <paramref name="onEnter"/> gets the player.
	/// </summary>
	public static Area3D MakeTrigger(Node3D parent, Shape3D shape, Vector3 localOffset, Action<PlayerController> onEnter, string name = "StoryTrigger")
	{
		var area = new Area3D { Name = name, CollisionLayer = 0, CollisionMask = PlayerLayer, Monitorable = false, Position = localOffset };
		area.AddChild(new CollisionShape3D { Shape = shape });
		parent.AddChild(area);
		if (onEnter != null) area.BodyEntered += b => { if (b is PlayerController p) onEnter(p); };
		return area;
	}

	/// <summary>The player, if it is currently inside <paramref name="area"/>.</summary>
	public static PlayerController PlayerInside(Area3D area)
	{
		if (area == null || !area.IsInsideTree() || !area.Monitoring) return null;
		foreach (var b in area.GetOverlappingBodies())
			if (b is PlayerController p) return p;
		return null;
	}

	/// <summary>Turns the player's view toward a point over time (a scripted look through PlayerInput,
	/// so it works with input disabled). Pausable.</summary>
	public static async Task PanTowards(Node owner, PlayerController player, Vector3 target, float seconds, CancellationToken ct)
	{
		double t = 0;
		while (t < seconds)
		{
			Vector3 to = target - player.GlobalPosition; to.Y = 0;
			if (to.LengthSquared() > 0.01f)
			{
				float want = Mathf.Atan2(-to.X, -to.Z);
				float diff = Mathf.AngleDifference(player.CameraRig.Yaw, want);
				player.PlayerInput.AddCutsceneLook(new Vector2(Mathf.Clamp(diff, -0.06f, 0.06f), 0));
			}
			await Cutscene.Frame(owner, ct);
			t += owner.GetProcessDeltaTime();
		}
	}

	/// <summary>Plays a one-shot 2D/3D sound under <paramref name="parent"/> on an explicit bus; frees itself.</summary>
	public static AudioStreamPlayer3D PlayAt(Node3D parent, string path, string bus, Vector3 localPos,
		float volumeDb = 0f, float unitSize = 4f, float maxDistance = 50f, float pitch = 1f)
	{
		if (!ResourceLoader.Exists(path)) return null;
		var voice = new AudioStreamPlayer3D
		{
			Stream = GD.Load<AudioStream>(path), Bus = bus, VolumeDb = volumeDb,
			UnitSize = unitSize, MaxDistance = maxDistance, PitchScale = pitch, Position = localPos,
		};
		parent.AddChild(voice);
		voice.Finished += voice.QueueFree;
		voice.Play();
		return voice;
	}
}
