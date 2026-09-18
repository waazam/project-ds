using System.Text;

namespace ProjectDS.Tools.AudioGen;

public static class Wav
{
	/// <summary>Writes 16-bit PCM mono. Samples are clamped to [-1,1] and rounded (no dither: nothing here is near the LSB).</summary>
	public static void Write(string path, double[] x, int sr)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		using var bw = new BinaryWriter(File.Create(path));
		int data = x.Length * 2;
		bw.Write(Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + data); bw.Write(Encoding.ASCII.GetBytes("WAVE"));
		bw.Write(Encoding.ASCII.GetBytes("fmt ")); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
		bw.Write(sr); bw.Write(sr * 2); bw.Write((short)2); bw.Write((short)16);
		bw.Write(Encoding.ASCII.GetBytes("data")); bw.Write(data);
		foreach (var v in x) bw.Write((short)Math.Round(Math.Clamp(v, -1.0, 1.0) * 32767.0));
	}

	public record Info(int SampleRate, int Channels, int Bits, double[] Samples);

	/// <summary>Minimal PCM16 reader (walks chunks; enough to verify our own output).</summary>
	public static Info Read(string path)
	{
		using var br = new BinaryReader(File.OpenRead(path));
		if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "RIFF") throw new InvalidDataException("not RIFF");
		br.ReadInt32();
		if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "WAVE") throw new InvalidDataException("not WAVE");
		int sr = 0, ch = 0, bits = 0;
		while (br.BaseStream.Position < br.BaseStream.Length)
		{
			string id = Encoding.ASCII.GetString(br.ReadBytes(4));
			int len = br.ReadInt32();
			if (id == "fmt ")
			{
				short fmt = br.ReadInt16(); ch = br.ReadInt16(); sr = br.ReadInt32(); br.ReadInt32(); br.ReadInt16(); bits = br.ReadInt16();
				if (fmt != 1) throw new InvalidDataException("not PCM");
				br.ReadBytes(len - 16);
			}
			else if (id == "data")
			{
				int n = len / 2;
				var s = new double[n];
				for (int i = 0; i < n; i++) s[i] = br.ReadInt16() / 32767.0;
				return new Info(sr, ch, bits, s);
			}
			else br.ReadBytes(len + (len & 1));
		}
		throw new InvalidDataException("no data chunk");
	}
}
