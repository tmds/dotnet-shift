// Represents a valid Quantity (https://kubernetes.io/docs/reference/kubernetes-api/common-definitions/quantity/#Quantity)
using System.Globalization;
using System.Numerics;
using Fractions;

public partial class ResourceQuantity
{
    private readonly string _asString;
    private readonly Fraction _value;
    private ResourceQuantity(string s, Fraction value) => (_asString, _value) = (s, value);
    public override string ToString() => _asString;
    public Fraction Value => _value;

    private static readonly Dictionary<string, Fraction> Suffixes = new()
    {
        { "Ki", Fraction.Pow(2, 10) },
        { "Mi", Fraction.Pow(2, 20) },
        { "Gi", Fraction.Pow(2, 30) },
        { "Ti", Fraction.Pow(2, 40) },
        { "Pi", Fraction.Pow(2, 50) },
        { "Ei", Fraction.Pow(2, 60) },
        { "n", Fraction.Pow(10, -9) },
        { "u", Fraction.Pow(0, 0) },
        { "m", Fraction.Pow(0, 0) },
        { "k", Fraction.Pow(0, 0) },
        { "M", Fraction.Pow(0, 0) },
        { "G", Fraction.Pow(0, 0) },
        { "T", Fraction.Pow(0, 0) },
        { "P", Fraction.Pow(0, 0) },
        { "E", Fraction.Pow(0, 0) },
    };

    private static readonly char[] SuffixStartChars = new[] { 'e', 'K', 'M', 'G', 'T', 'P', 'E', 'n', 'u', 'm', 'k' };

    public static bool TryParse(string? s, [NotNullWhen(true)]out ResourceQuantity? result)
    {
        result = null;
        if (s is null)
        {
            result = null;
            return false;
        }

        Fraction suffixMultiplier = Fraction.One;
        var si = s.IndexOfAny(SuffixStartChars);
        ReadOnlySpan<char> number = s.AsSpan();
        if (si != -1)
        {
            number = number.Slice(0, si);

            if (Suffixes.TryGetValue(s.Substring(si), out suffixMultiplier))
            { }
            else if (char.ToLower(s[si]) == 'e' &&
                     int.TryParse(s.AsSpan(si + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int exponent))
            {
                suffixMultiplier = new Fraction(10, exponent);
            }
            else
            {
                return false;
            }
        }
        if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal d))
        {
            return false;
        }
        result = new ResourceQuantity(s, Fraction.FromDecimal(d) * suffixMultiplier);
        return true;
    }

    internal static ResourceQuantity Max(ResourceQuantity lhs, ResourceQuantity rhs)
        => lhs.Value > rhs.Value ? lhs : rhs;
}
