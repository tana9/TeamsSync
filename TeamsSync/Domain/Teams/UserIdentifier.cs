using System.Text;

namespace TeamsSync.Domain.Teams;

public static class UserIdentifier
{
    public static string NormalizeName(string value)
    {
        return string.Concat(
            value.Normalize(NormalizationForm.FormKC).Where(character => !char.IsWhiteSpace(character)));
    }

    public static bool NameEquals(string left, string right)
    {
        return string.Equals(NormalizeName(left), NormalizeName(right), StringComparison.OrdinalIgnoreCase);
    }
}