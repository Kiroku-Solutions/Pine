using System.Text;

namespace Pine.Core.Security;

/// <summary>
/// Computes the AEAD associated data that binds a <see cref="VaultKeyWrap"/> to the header it
/// belongs to.
/// </summary>
/// <remarks>
/// <para>
/// THE RULE, AND IT IS DELIBERATELY A RULE RATHER THAN A JUDGEMENT CALL: <b>every field of the
/// header except the wrap itself is bound.</b> The alternative - binding only the fields somebody
/// argued were "security relevant" - invites exactly the argument that gets it wrong once. A reader
/// can check this rule against <see cref="VaultHeader"/>'s property list in one glance; they cannot
/// check a taste-based subset at all.
/// </para>
/// <para>
/// WHAT THE BINDING BUYS. Associated data is authenticated but not encrypted: if any bound byte
/// differs at unwrap time, the GCM tag check fails and the data key is never produced. Concretely
/// it stops two attacks that the KDF alone does not.
/// </para>
/// <list type="number">
///   <item><description><b>Splicing.</b> Copying vault A's wrap into vault B's header is refused,
///   because <see cref="VaultHeader.VaultId"/> differs and the vault id is bound. Without this, a
///   header field the KDF never reads is a field an attacker may edit freely.</description></item>
///   <item><description><b>Downgrade.</b> Rewriting <see cref="VaultHeader.HeaderVersion"/> to
///   claim an older, weaker schema is refused for the same reason.</description></item>
/// </list>
/// <para>
/// The cost parameters and the salt are bound too, even though tampering with those already fails
/// on its own (they feed the KDF, so changing one derives a different auth key and the tag check
/// fails anyway). Binding them is redundant, and the redundancy is kept on purpose: the rule above
/// stays true without a footnote, and a future KDF that ignores a parameter cannot silently turn
/// that parameter into an unauthenticated one.
/// </para>
/// <para>
/// STABILITY IS PART OF THE CONTRACT. This encoding is frozen for header version
/// <see cref="VaultHeader.CurrentVersion"/>. Changing so much as a field order or a length prefix
/// changes every associated-data value, and every existing vault would stop opening. A new binding
/// belongs to a new header version, computed under a new branch here.
/// </para>
/// </remarks>
public static class VaultHeaderBinding
{
    /// <summary>
    /// Builds the associated data for a header's constituent values.
    /// </summary>
    /// <remarks>
    /// Takes the values rather than a <see cref="VaultHeader"/> because creating a vault needs the
    /// associated data BEFORE a header can exist: the header requires the wrap, and the wrap
    /// requires this. Unwrapping calls the overload that takes the assembled header, so both sides
    /// provably use one encoding.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="kdfParameters"/> or <paramref name="salt"/> is null.</exception>
    public static byte[] Compute(
        int headerVersion,
        Guid vaultId,
        DateTimeOffset createdAtUtc,
        KdfParameters kdfParameters,
        byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(kdfParameters);
        ArgumentNullException.ThrowIfNull(salt);

        using var buffer = new MemoryStream();

        // BinaryWriter, not string concatenation or JSON. Every value below is written with an
        // unambiguous, self-delimiting encoding - fixed-width integers, length-prefixed strings and
        // byte arrays - so no two DIFFERENT headers can ever produce the SAME associated data.
        // A delimiter-joined string could: a vault named with the delimiter would collide.
        // BinaryWriter's integer encoding is little-endian and its string encoding is a 7-bit
        // length prefix plus UTF-8 on every platform, so this is portable and version-stable.
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(VaultHeader.Magic);
            writer.Write(headerVersion);
            writer.Write(vaultId.ToByteArray());

            // UtcTicks rather than the formatted timestamp: the header file stores an ISO-8601
            // string WITH an offset, and two spellings of the same instant ("...Z" and "+00:00")
            // must not produce two different bindings. The instant is the fact; its spelling is not.
            writer.Write(createdAtUtc.UtcTicks);

            writer.Write(kdfParameters.KdfId);
            writer.Write(kdfParameters.MemoryKib);
            writer.Write(kdfParameters.Iterations);
            writer.Write(kdfParameters.Parallelism);
            writer.Write(kdfParameters.KeyLengthBytes);
            writer.Write(kdfParameters.SaltLengthBytes);

            writer.Write(salt.Length);
            writer.Write(salt);
        }

        return buffer.ToArray();
    }

    /// <summary>Builds the associated data for an assembled header.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="header"/> is null.</exception>
    public static byte[] Compute(VaultHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        return Compute(
            header.HeaderVersion,
            header.VaultId,
            header.CreatedAtUtc,
            header.KdfParameters,
            header.Salt);
    }
}
