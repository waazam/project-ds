using Godot;

namespace ProjectDS.Entities;

/// <summary>Small helpers for posing a skeleton's bones by hand (the remodelled creatures' overlays and limb solvers):
/// turning a bone about an axis in the skeleton's space, and aiming its length (+Y) along a direction.</summary>
public static class BonePose
{
	/// <summary>Turns <paramref name="bone"/> about an axis in the skeleton's space, carrying its children.</summary>
	public static void Turn(Skeleton3D skel, int bone, Vector3 axisSkel, float angle)
	{
		if (bone < 0 || Mathf.Abs(angle) < 1e-6f || axisSkel.LengthSquared() < 1e-8f || !axisSkel.IsFinite() || !float.IsFinite(angle)) return;
		Basis g = skel.GetBoneGlobalPose(bone).Basis.Orthonormalized();
		Vector3 local = (g.Inverse() * axisSkel).Normalized();
		if (!local.IsFinite()) return;
		skel.SetBonePoseRotation(bone, (skel.GetBonePoseRotation(bone) * new Quaternion(local, angle)).Normalized());
	}

	/// <summary>Turns <paramref name="bone"/> so a direction it carries goes from <paramref name="from"/> to <paramref name="to"/>.</summary>
	public static void AimFrom(Skeleton3D skel, int bone, Vector3 from, Vector3 to, float w = 1f)
	{
		Vector3 axis = from.Cross(to);
		float s = axis.Length();
		if (s < 1e-6f) return;
		Turn(skel, bone, axis / s, Mathf.Atan2(s, from.Dot(to)) * w);
	}

	/// <summary>Aims <paramref name="bone"/>'s length along <paramref name="dirSkel"/> (by <paramref name="w"/> of the way).</summary>
	public static void Aim(Skeleton3D skel, int bone, Vector3 dirSkel, float w = 1f)
	{
		if (bone < 0) return;
		AimFrom(skel, bone, skel.GetBoneGlobalPose(bone).Basis.Y.Normalized(), dirSkel.Normalized(), w);
	}
}
