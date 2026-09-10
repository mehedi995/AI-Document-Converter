using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AI.Document.Converter.Web.Tests.TestSupport;

// The parts of "being a browser" the tests actually need: carrying an
// anti-forgery token from a form to its POST, and computing an authenticator
// code.
//
// Both are real. The token is scraped from the rendered form rather than
// disabled, so every POST here exercises the same CSRF validation a real
// request does - a harness that turned anti-forgery off would quietly stop
// testing it.
public static partial class BrowserFlow
{
    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="([^"]+)""")]
    private static partial Regex TokenPattern();

    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = TokenPattern().Match(html);

        Assert.True(match.Success, $"No anti-forgery token found in the form at {path}.");

        return match.Groups[1].Value;
    }

    public static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string path, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await GetAntiforgeryTokenAsync(client, path);

        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    // RFC 6238, which is what Identity's authenticator provider validates
    // against: HMAC-SHA1 over the 30-second counter, six digits.
    //
    // Implemented here rather than mocked, so the two-factor login step is
    // exercised end to end. A faked token provider would prove the controller
    // calls something, not that a real code from a real app signs you in.
    public static string ComputeTotp(string base32Key, DateTimeOffset? at = null)
    {
        var key = DecodeBase32(base32Key);
        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;

        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0F;

        var binary =
            ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] DecodeBase32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        var cleaned = input.Replace(" ", string.Empty).Replace("-", string.Empty)
            .TrimEnd('=').ToUpperInvariant();

        var bits = 0;
        var value = 0;
        var output = new List<byte>();

        foreach (var character in cleaned)
        {
            var index = alphabet.IndexOf(character);
            Assert.True(index >= 0, $"'{character}' is not valid base32.");

            value = (value << 5) | index;
            bits += 5;

            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }

    public static string? LocationOf(HttpResponseMessage response) =>
        response.Headers.Location?.ToString();

    public static bool RedirectsToAccessDenied(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.Redirect
        && LocationOf(response)?.Contains("/account/denied", StringComparison.OrdinalIgnoreCase) == true;

    public static bool RedirectsToLogin(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.Redirect
        && LocationOf(response)?.Contains("/account/login", StringComparison.OrdinalIgnoreCase) == true;
}
