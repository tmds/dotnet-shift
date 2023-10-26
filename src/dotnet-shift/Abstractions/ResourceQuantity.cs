// Represents a valid Quantity (https://kubernetes.io/docs/reference/kubernetes-api/common-definitions/quantity/#Quantity)
public partial class ResourceQuantity
{
    private string _value;
    private ResourceQuantity(string value) => _value = value;
    public override string ToString() => _value;

    private static readonly string[] Suffixes = new[] { "Ki", "Mi", "Gi", "Ti", "Pi", "Ei", "n", "u", "m", "k", "M", "G", "T", "P", "E" };

    private static readonly char[] SuffixStartChars = new[] { 'e', 'K', 'M', 'G', 'T', 'P', 'E', 'n', 'u', 'm', 'k' };

    public static bool TryParse(string? s, [NotNullWhen(true)]out ResourceQuantity? result)
    {
        result = null;
        if (s is null)
        {
            result = null;
            return false;
        }

        var si = s.IndexOfAny(SuffixStartChars);
        ReadOnlySpan<char> number = s.AsSpan();
        if (si != -1)
        {
            number = number.Slice(0, si);

            if (!Suffixes.Any(suffix => suffix.AsSpan().SequenceEqual(s.AsSpan(si))))
            {
                if (char.ToLower(s[si]) == 'e')
                {
                    if (!int.TryParse(s.AsSpan(si + 1), out _))
                    {
                        return false;
                    }
                }
            }
        }
        if (decimal.TryParse(number, out _))
        {
            result = new ResourceQuantity(s);
            return true;
        }
        return false;
    }
}
