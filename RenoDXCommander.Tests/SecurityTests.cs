using System.Net.Http;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Tests for the archive path guard and the GitHub token handler.
/// Pure logic only — no files are created and no network is used.
/// </summary>
public class SecurityTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rhi-security-tests", "game");

    // ═══════════════════════════════════════════════════════════════════════════════
    // FileHelper.GetSafeChildPath — archive entry names must stay inside the target folder
    // ═══════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("reshade-shaders\\Shaders\\a.fx")]
    [InlineData("reshade-shaders/Shaders/a.fx")]
    [InlineData("a\\..\\b.fx")]
    [InlineData("dxgi.dll")]
    public void GetSafeChildPath_InsideRoot_ReturnsFullPath(string relative)
    {
        var result = FileHelper.GetSafeChildPath(Root, relative);

        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(Path.Combine(Root, relative)), result);
        Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("..\\evil.dll")]
    [InlineData("../evil.dll")]
    [InlineData("a\\..\\..\\evil.dll")]
    [InlineData("..\\game2\\evil.dll")] // sibling folder that shares the root's name as a prefix
    [InlineData("C:\\Windows\\System32\\evil.dll")]
    [InlineData("\\Windows\\evil.dll")]
    [InlineData("C:evil.dll")]
    [InlineData("\\\\server\\share\\evil.dll")]
    [InlineData("")]
    public void GetSafeChildPath_EscapingOrRooted_ReturnsNull(string relative)
    {
        Assert.Null(FileHelper.GetSafeChildPath(Root, relative));
    }

    [Fact]
    public void GetSafeChildPath_RootWithTrailingSeparator_Works()
    {
        var root = Root + Path.DirectorySeparatorChar;

        Assert.NotNull(FileHelper.GetSafeChildPath(root, "a.fx"));
        Assert.Null(FileHelper.GetSafeChildPath(root, "..\\a.fx"));
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // GitHubAuthHandler — the token goes to api.github.com only
    // ═══════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("https://api.github.com/repos/a/b/releases", true)]
    [InlineData("https://API.GITHUB.COM/user", true)]
    [InlineData("http://api.github.com/user", false)]
    [InlineData("https://github.com/a/b/releases/download/v1/x.zip", false)]
    [InlineData("https://raw.githubusercontent.com/a/b/main/x", false)]
    [InlineData("https://api.github.com.evil.example/user", false)]
    [InlineData("https://evil.example/api.github.com", false)]
    public void IsGitHubApi_MatchesOnlyHttpsApiHost(string url, bool expected)
    {
        Assert.Equal(expected, GitHubAuthHandler.IsGitHubApi(new Uri(url)));
    }

    [Fact]
    public void IsGitHubApi_RelativeOrNull_IsFalse()
    {
        Assert.False(GitHubAuthHandler.IsGitHubApi(null));
        Assert.False(GitHubAuthHandler.IsGitHubApi(new Uri("repos/a/b", UriKind.Relative)));
    }

    [Fact]
    public async Task GitHubAuthHandler_AddsTokenForGitHubApiOnly()
    {
        var inner = new RecordingHandler();
        using var invoker = new HttpMessageInvoker(new GitHubAuthHandler(inner));
        var previous = GitHubAuthHandler.Token;
        GitHubAuthHandler.Token = "test-token";
        try
        {
            using var api = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            using var other = new HttpRequestMessage(HttpMethod.Get, "https://example.com/mod.zip");
            using var own = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            own.Headers.TryAddWithoutValidation("Authorization", "Bearer other-token");

            (await invoker.SendAsync(api, CancellationToken.None)).Dispose();
            (await invoker.SendAsync(other, CancellationToken.None)).Dispose();
            (await invoker.SendAsync(own, CancellationToken.None)).Dispose();

            Assert.Equal("Bearer test-token", api.Headers.GetValues("Authorization").Single());
            Assert.False(other.Headers.Contains("Authorization"));
            Assert.Equal("Bearer other-token", own.Headers.GetValues("Authorization").Single());
        }
        finally
        {
            GitHubAuthHandler.Token = previous;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
