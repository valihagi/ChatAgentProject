namespace ChatAgent.Api.Agent;

/// <summary>GS1 mod-10 check digit used by GTIN-8/12/13/14 and SSCC.</summary>
public static class Gs1
{
    public static bool IsDigits(string s) => s.Length > 0 && s.All(char.IsAsciiDigit);

    public static char CheckDigit(string body)
    {
        var sum = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var digit = body[body.Length - 1 - i] - '0';
            sum += digit * (i % 2 == 0 ? 3 : 1);
        }
        return (char)('0' + (10 - sum % 10) % 10);
    }

    /// <summary>True if the last digit is the correct check digit for the preceding ones.</summary>
    public static bool HasValidCheckDigit(string digits) =>
        digits.Length > 1 && CheckDigit(digits[..^1]) == digits[^1];
}
