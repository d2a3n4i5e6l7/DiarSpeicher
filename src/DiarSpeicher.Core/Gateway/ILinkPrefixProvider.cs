namespace DiarSpeicher.Core.Gateway;

/// <summary>
/// Prefix that every absolute link handed to a client has to carry.
/// <para>
/// The gateway mounts this backend under a public prefix and strips it before proxying, so
/// incoming routing works without the services knowing about it. Generated links are the other
/// half of the problem: served under a prefix, a link built from the root points outside the
/// mount. The prefix is a per-request value, and this is the seam that carries it into the
/// services without giving the infrastructure layer a dependency on ASP.NET.
/// </para>
/// </summary>
public interface ILinkPrefixProvider
{
    /// <summary>
    /// The prefix, with a leading slash and no trailing one —"/diarspeicher"— or empty when
    /// served from the root. Concatenating it before a root-relative path always yields a
    /// valid path.
    /// </summary>
    string Prefix { get; }
}
