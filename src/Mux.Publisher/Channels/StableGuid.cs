namespace Mux.Publisher.Channels
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>
    /// Produces a deterministic GUID from a string so installer identities (Inno AppId, WiX UpgradeCode)
    /// are stable across releases without being committed by hand. The same input always yields the same
    /// GUID, which is what lets an installer recognize and upgrade a prior install.
    /// </summary>
    public static class StableGuid
    {
        /// <summary>
        /// Derives an uppercase GUID string (no braces) from an arbitrary seed.
        /// </summary>
        /// <param name="seed">The seed string.</param>
        /// <returns>A deterministic GUID string like <c>2F1C...-....</c>.</returns>
        public static string FromString(string seed)
        {
            if (seed == null) throw new ArgumentNullException(nameof(seed));

            byte[] hash;
            using (MD5 md5 = MD5.Create())
            {
                hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
            }

            // 16 bytes → GUID. Deterministic; not a v4 random GUID by design.
            Guid guid = new Guid(hash);
            return guid.ToString("D").ToUpperInvariant();
        }
    }
}
