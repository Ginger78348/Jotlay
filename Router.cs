using System.Text.RegularExpressions;

namespace Jotlay;

/// <summary>
/// Turns raw typed text into a (bucket, body) pair.
///
/// Any leading "word:" becomes the bucket, and everything after it is the body:
///     work:  email the vendor about the invoice  ->  bucket "work"
///     idea:  a landing page that writes itself     ->  bucket "idea"
///     todo:  renew the domain before Friday        ->  bucket "todo"
///     call:  dentist, reschedule                    ->  bucket "call"
///     read:  that article on rust ownership         ->  bucket "read"
///
/// No prefix at all lands in "inbox". You never have to pre-declare buckets —
/// a new one springs into existence the first time you use it.
/// </summary>
public static class Router
{
    // A leading token of letters/digits/underscore/hyphen (max 32), then a colon.
    private static readonly Regex PrefixRx =
        new(@"^\s*([A-Za-z0-9_\-]{1,32})\s*:\s*(.*)$", RegexOptions.Singleline | RegexOptions.Compiled);

    public static (string bucket, string body) Parse(string raw)
    {
        raw = raw.Trim();
        var m = PrefixRx.Match(raw);
        if (m.Success)
        {
            string bucket = m.Groups[1].Value.Trim().ToLowerInvariant();
            string body   = m.Groups[2].Value.Trim();
            if (body.Length == 0)
                return ("inbox", raw); // "word:" with nothing after — keep it whole in inbox
            return (bucket, body);
        }
        return ("inbox", raw);
    }
}
