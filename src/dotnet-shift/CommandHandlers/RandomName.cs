namespace CommandHandlers;

static class RandomName
{
    private static ReadOnlySpan<char> NameChars => "abcdefghijklmnopqrstuvwxyz012345";

    public static string Generate(int length = 8)
    {
        Span<byte> bytes = stackalloc byte[length];
        Span<char> chars = stackalloc char[length];
        Random.Shared.NextBytes(bytes);
        for (int i = 0; i < length; i++)
        {
            chars[i] = NameChars[bytes[i] % NameChars.Length];
        }
        return new string(chars);
    }
}