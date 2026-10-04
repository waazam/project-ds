using Godot;

namespace ProjectDS.World;

/// <summary>
/// The winter light coming down through the clerestory (2026-10-04): a pale shaft through each of the south side's paired
/// lancets, slanting down across the nave, the dust turning in it (<see cref="WindowShafts"/>). Cold and dim: a haze of
/// light in the dark of the nave, never a glare.
/// </summary>
public partial class Church
{
	private ShaderMaterial _shaftMat;

	private void BuildShafts()
	{
		_shaftMat = WindowShafts.Material(new Color(0.74f, 0.8f, 0.92f), 0.035f);
		var dir = new Vector3(-0.55f, -0.8f, 0.12f);
		float x = NaveHalf - 0.6f;
		for (int b = 0; b < NaveBays; b++)
		{
			float c = (b + 0.5f) * BayLen;
			foreach (float dz in new[] { -1.1f, 1.1f })
				WindowShafts.Add(this, _shaftMat, new Vector3(x, 20.3f, c + dz), Vector3.Back, Vector3.Up, 1.2f, 3.6f, dir, 24f, b % 2 == 0 && dz < 0 ? 26 : 0);
		}
	}
}
