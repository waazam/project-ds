using Godot;

namespace ProjectDS.Player;

/// <summary>Which hand.</summary>
public enum HandSide { Left, Right }

/// <summary>How a hand is closed (2026-10-13j). The hand fits itself to the pose; what it holds is parented to its
/// <see cref="IFirstPersonHand.Socket"/>.</summary>
public enum HandPose
{
	/// <summary>Open, at rest.</summary>
	Relaxed,
	/// <summary>The left hand's fist round the lantern's wire bail (the wire along X through the socket).</summary>
	LanternBail,
	/// <summary>A fist round a handle standing up (+Y) through the socket: an axe, a hammer, a crowbar, a knife, the cap's neck.</summary>
	ToolGrip,
	/// <summary>A fist round a thin stick standing up through the socket: a road flare.</summary>
	FlareGrip,
	/// <summary>The camera's right end in the hand: fingers down its front, the thumb on its back, the first finger over the
	/// shutter button.</summary>
	CameraHold,
	/// <summary>A small thing upright between the fingers and the thumb: the lighter, the walkie.</summary>
	SmallGrip,
}

/// <summary>
/// A first-person hand (2026-10-13j): what the held things are built against, so the hand can be swapped without touching
/// them. The hand's own root (<see cref="Root"/>) is placed by whoever holds it; the thing held goes under
/// <see cref="Socket"/>, in "grip space": the origin in the middle of the closed fist, a handle running up +Y through it,
/// -Z away from the eye (forward), +X to the right.
/// <para>Another piece of work (the realistic, reusable FirstPersonHand with its grip poses) replaces
/// <see cref="LegacyHand"/> through <see cref="FirstPersonHands.Factory"/>; until then it's the Blender fist
/// (fp_arm_fist_R.glb, mirrored for the left hand).</para>
/// </summary>
public interface IFirstPersonHand
{
	HandSide Side { get; }
	HandPose Pose { get; set; }
	/// <summary>The hand, its forearm and its sleeve (place and turn this).</summary>
	Node3D Root { get; }
	/// <summary>Where the held thing goes (grip space, see above).</summary>
	Node3D Socket { get; }
}

public static class FirstPersonHands
{
	/// <summary>Makes a hand. Replace to plug in another hand; the default is <see cref="LegacyHand"/>.</summary>
	public static System.Func<HandSide, HandPose, IFirstPersonHand> Factory = (side, pose) => new LegacyHand(side, pose);

	public static IFirstPersonHand Create(HandSide side, HandPose pose) => Factory(side, pose);
}

/// <summary>
/// The hand as it was before the rebuild: the Blender fist round a handle standing up through its origin
/// (<see cref="HeldItem.BuildArm"/>, fp_arm_fist_R), mirrored for a left hand. It has the one pose (the fist); the
/// held things are placed so the fist reads right round each of them.
/// </summary>
public sealed class LegacyHand : IFirstPersonHand
{
	public HandSide Side { get; }
	public HandPose Pose { get; set; }
	public Node3D Root { get; }
	public Node3D Socket { get; }

	public LegacyHand(HandSide side, HandPose pose)
	{
		Side = side;
		Pose = pose;
		Root = new Node3D { Name = side == HandSide.Left ? "HandL" : "HandR" };
		var arm = HeldItem.BuildArm(Root, "fp_arm_fist_R", Vector3.Zero);
		if (arm != null && side == HandSide.Left) arm.Scale = new Vector3(-1f, 1f, 1f);   // (a mirror image: the left fist)
		// (the Blender fist's knuckles face 47° in from straight ahead: the socket's -Z with them)
		Socket = new Node3D { Name = "Socket", RotationDegrees = new Vector3(0f, side == HandSide.Left ? -47f : 47f, 0f) };
		Root.AddChild(Socket);
	}
}
