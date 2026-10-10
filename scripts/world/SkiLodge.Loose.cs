using System.Collections.Generic;
using Godot;
using ProjectDS.Audio;
using ProjectDS.Entities;
using ProjectDS.Player;
using ProjectDS.Systems;

namespace ProjectDS.World;

/// <summary>
/// (2026-10-10) Loose things in the lodge (see <see cref="Knockable"/>): bottles and a tin along the upstairs corridor's
/// walls, a chair pushed out from the wall; in the crawlspace, a couple of old tins on the boards before the arms.
/// Knock something over and it's heard: somewhere in the walls near it, something knocks back. In the crawlspace, a tin
/// thrown on past the arm draws it after the noise, clawing the wrong way, while they get under it.
/// </summary>
public partial class SkiLodge
{
	public readonly List<Knockable> Loose = new();
	public readonly List<Knockable> CrawlTins = new();
	public int KnockReplies { get; private set; }
	private double _knockReplyAt = -100;

	private Dictionary<string, Material> LooseRoles => new(Woodwork) { ["metal"] = LodgeParts.LodgeTextures.IronMat };

	private void BuildLoose()
	{
		Lures.Heard += OnLooseNoise;
		var r = LooseRoles;
		// the upstairs corridor, against its walls
		foreach (var (x, z, model, kind, yaw) in new[]
		{
			(-15.4f, 0.8f, "bottle_wine", Knockable.Sound.Glass, 0.3f), (-18.9f, -0.82f, "tin", Knockable.Sound.Tin, 1.2f),
			(-23.6f, 0.78f, "bottle_whiskey", Knockable.Sound.Glass, 2.0f), (-26.4f, -0.55f, "dining_chair", Knockable.Sound.Chair, 0.5f),
		})
			Loose.Add(Knockable.Put(this, model, r, kind, new Vector3(x, UpperY + 0.03f, z), yaw));
		// the crawlspace: a tin three cells short of the first arm, and of the third
		foreach (int a in new[] { 0, 2 })
		{
			int idx = CrawlPath.IndexOf(ArmSpots[a].cell);
			if (idx < 3) continue;
			var cell = CrawlPath[idx - 3];
			var c = CellCentre(cell) + CrawlDown;
			float y = FloorAt(_maze[cell], 0.2f, 0.2f) + CrawlDown.Y + 0.03f;
			var tin = Knockable.Put(this, "tin", r, Knockable.Sound.Tin, new Vector3(c.X + 0.22f, y, c.Z + 0.2f), 0.7f, takeable: true);
			tin.TakePrompt = "Take the tin";
			CrawlTins.Add(tin);
		}
	}

	private void OnLooseNoise(Vector3 world, float reach, string what)
	{
		if (!IsInsideTree()) return;
		var player = StoryBeat.Player(this);
		if (player == null || player.GlobalPosition.DistanceTo(world) > 40f) return;
		// in the walls: the arm goes after it
		bool any = false;
		foreach (var arm in Arms)
		{
			if (arm.Phase is not (WendigoArm.State.Reach or WendigoArm.State.Burst)) continue;
			if (arm.GlobalPosition.DistanceTo(world) > 8f) continue;
			arm.Distract(world, 6f);
			any = true;
			GD.Print($"[story] Act 23: {what} - the arm claws after the noise");
		}
		if (any || InCrawlspace) return;
		// the lodge: something answers it, from inside the walls nearby (not too often)
		double now = Time.GetTicksMsec() / 1000.0;
		if (now - _knockReplyAt < 25.0 || StoryManager.Instance is not { Current: >= Checkpoint.Act22Finished and < Checkpoint.Act23Finished }) return;
		_knockReplyAt = now;
		KnockReplies++;
		GD.Print($"[story] Act 23: {what} - something in the walls heard it");
		var at = world + new Vector3(GD.Randf() < 0.5f ? -1.4f : 1.4f, 1.2f, GD.Randf() < 0.5f ? -1.2f : 1.2f);
		var tw = CreateTween();
		tw.TweenInterval(1.4f);
		for (int i = 0; i < 3; i++)
		{
			tw.TweenCallback(Callable.From(() => AudioDirector.OneShot(this, "wall_knock", 3, at, -2f, "Events", 4f, 0.06f)));
			tw.TweenInterval(0.55f);
		}
		tw.TweenCallback(Callable.From(() => PlayerBreathing.Startle(0.6f)));
	}
}
