using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CutTheSave.Core;

public sealed class ScoreHashService
{
    public string Compute(int totalScore)
    {
        var input = totalScore.ToString(CultureInfo.InvariantCulture) + "\0";
        var bytes = Encoding.BigEndianUnicode.GetBytes(input);
        var digest = MD5.HashData(bytes);
        return Convert.ToHexString(digest).ToLowerInvariant() + "\0";
    }

    public int ComputeTotalScore(CutTheRopeSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        return save.IntEntries
            .Where(entry => entry.Key.StartsWith("SCORE_", StringComparison.Ordinal))
            .Sum(entry => entry.Value);
    }

    public void Recalculate(CutTheRopeSave save)
    {
        ArgumentNullException.ThrowIfNull(save);
        save.SetString("PREFS_SCORE_HASH", Compute(ComputeTotalScore(save)));
    }
}
