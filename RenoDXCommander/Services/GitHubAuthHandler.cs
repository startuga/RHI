// GitHubAuthHandler.cs — Adds the GitHub token to GitHub API requests only.

using System.Net.Http;

namespace RenoDXCommander.Services;

/// <summary>
/// Sits in front of the shared HttpClient's handler and adds <c>Authorization: Bearer</c>
/// to requests for https://api.github.com. The shared client also downloads from mod hosts,
/// wikis and CDNs, so a default Authorization header would hand the user's token to every
/// one of them. Requests that already carry an Authorization header are left alone.
/// </summary>
public sealed class GitHubAuthHandler : DelegatingHandler
{
    private static volatile string? _token;

    /// <summary>The token to send to api.github.com, or null for anonymous requests.</summary>
    public static string? Token
    {
        get => _token;
        set => _token = string.IsNullOrEmpty(value) ? null : value;
    }

    public GitHubAuthHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _token;
        if (token != null && IsGitHubApi(request.RequestUri) && !request.Headers.Contains("Authorization"))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>True only for https URLs on api.github.com.</summary>
    public static bool IsGitHubApi(Uri? uri) =>
        uri is { IsAbsoluteUri: true }
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase);
}
