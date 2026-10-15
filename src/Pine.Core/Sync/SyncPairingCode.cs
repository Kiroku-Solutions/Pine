using System;
using System.Security.Cryptography;
using System.Text;

namespace Pine.Core.Sync;

/// <summary>
/// The one-time code a user carries by hand from the vault that has the sync key to the vault that has not
/// (post-audit plan item 3.1e; TD-154).
/// </summary>
/// <remarks>
/// <para>
/// 125 BITS, AND THAT NUMBER IS THE WHOLE PROTOCOL. There is no PAKE here: the offer is a sealed file, so anyone
/// holding a copy of it can try codes against it offline, as fast as they can run HKDF and AES-GCM. That is
/// acceptable only because guessing is hopeless — <see cref="Characters"/> is 25 characters drawn uniformly from a
/// 32-letter alphabet, which is 32^25 = 2^125 codes. A six-digit code "protected" by a hand-rolled protocol would
/// need a PAKE to mean anything, and this item does not invent one. If the code ever shortens, this file is where
/// the argument has to be made again.
/// </para>
/// <para>
/// DRAWN PER CHARACTER, NOT BY ENCODING RANDOM BYTES. 25 characters is 125 bits, which is not a whole number of
/// bytes; encoding 16 bytes into 26 characters leaves padding bits whose value is not free, and a decoder that
/// ignores them accepts codes that differ but mean the same thing. One uniform draw per character
/// (<see cref="RandomNumberGenerator.GetInt32(int)"/>, which rejects the biased tail) has neither problem, and the
/// code is exactly what is typed.
/// </para>
/// <para>
/// CROCKFORD'S ALPHABET, BECAUSE A PERSON READS THIS ALOUD. No <c>I</c>, <c>L</c>, <c>O</c> or <c>U</c>: the first
/// three are misread as <c>1</c>, <c>1</c> and <c>0</c>, and the fourth keeps the generator from spelling things
/// nobody wants read out. <see cref="Normalise"/> accepts them anyway and folds them onto the digit they look like,
/// along with any case, spacing or hyphens — so a mistyped separator is not a refusal, and a genuinely wrong
/// character is.
/// </para>
/// <para>
/// GROUPED FOR THE EYE, NOT FOR THE KDF. <see cref="Format"/> inserts hyphens every five characters; the key
/// derivation always takes the 25 normalised characters, so how it was typed never changes what it opens.
/// </para>
/// </remarks>
public static class SyncPairingCode
{
    /// <summary>The alphabet: Crockford's base32, in order. Its length is 32, and the entropy claim depends on that.</summary>
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>How many characters a code has. 25 × log2(32) = 125 bits.</summary>
    public const int Characters = 25;

    /// <summary>How many characters go between hyphens when it is shown.</summary>
    public const int GroupSize = 5;

    /// <summary>The entropy of a code, in bits. Stated so a test can hold the protocol to it.</summary>
    public const int EntropyBits = Characters * 5;

    /// <summary>A new code from the CSPRNG, grouped for reading aloud.</summary>
    public static string Generate()
    {
        var characters = new char[Characters];
        for (var i = 0; i < characters.Length; i++)
        {
            characters[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return Format(new string(characters));
    }

    /// <summary>Groups a code for the screen: <c>XXXXX-XXXXX-XXXXX-XXXXX-XXXXX</c>.</summary>
    /// <param name="code">A code, however it is spaced.</param>
    /// <returns>The grouped form, or the input unchanged when it is not a code.</returns>
    public static string Format(string code)
    {
        if (Normalise(code) is not { } normalised)
        {
            return code;
        }

        var builder = new StringBuilder(Characters + ((Characters / GroupSize) - 1));
        for (var i = 0; i < normalised.Length; i++)
        {
            if (i > 0 && i % GroupSize == 0)
            {
                builder.Append('-');
            }

            builder.Append(normalised[i]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The 25 characters a code really is, or null when what was typed is not one.
    /// </summary>
    /// <param name="code">As the user typed it.</param>
    /// <returns>The canonical 25 characters, upper case and unseparated; null when it is not a code.</returns>
    public static string? Normalise(string? code)
    {
        if (code is null)
        {
            return null;
        }

        var characters = new char[Characters];
        var count = 0;
        foreach (var raw in code)
        {
            if (raw is '-' or ' ' or '\t' or '\r' or '\n' or '_' or '.')
            {
                continue;
            }

            var folded = char.ToUpperInvariant(raw) switch
            {
                'O' => '0',
                'I' or 'L' => '1',
                var other => other,
            };

            if (!Alphabet.Contains(folded, StringComparison.Ordinal))
            {
                return null;
            }

            if (count == Characters)
            {
                // Too long. Counted rather than measured first, so a code with separators is judged by its
                // characters and not by its length on screen.
                return null;
            }

            characters[count++] = folded;
        }

        return count == Characters ? new string(characters) : null;
    }

    /// <summary>Whether what was typed is a pairing code at all.</summary>
    public static bool IsWellFormed(string? code) => Normalise(code) is not null;

    /// <summary>The bytes a code contributes to the key derivation: its 25 canonical characters, ASCII.</summary>
    /// <exception cref="ArgumentException">Not a code.</exception>
    public static byte[] ToKeyMaterial(string code) =>
        Normalise(code) is { } normalised
            ? Encoding.ASCII.GetBytes(normalised)
            : throw new ArgumentException("Not a pairing code.", nameof(code));
}
