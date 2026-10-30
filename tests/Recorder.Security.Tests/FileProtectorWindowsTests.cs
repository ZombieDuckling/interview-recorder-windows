using System.Security.AccessControl;
using System.Security.Principal;
using Recorder.Core.Abstractions;

namespace Recorder.Security.Tests;

[Trait("Category", "Windows")]
public sealed class FileProtectorWindowsTests
{
    [Fact]
    public async Task EnsureSessionKey_CreatesHiddenKeyFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sessionDir = CreateSessionDirectory();
        var protector = new FileProtector();
        await protector.EnsureSessionKeyAsync(sessionDir, CancellationToken.None);

        var keyPath = CryptoConstants.SessionKeyPath(sessionDir);
        Assert.True(File.Exists(keyPath));
        Assert.Equal(FileAttributes.Hidden, File.GetAttributes(keyPath) & FileAttributes.Hidden);
    }

    [Fact]
    public void RestrictToCurrentUser_AllowsOnlyUserAndSystem()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sessionDir = CreateSessionDirectory();
        var protector = new FileProtector();
        protector.RestrictToCurrentUser(sessionDir);

        var security = new DirectoryInfo(sessionDir).GetAccessControl(AccessControlSections.Access);
        Assert.True(security.AreAccessRulesProtected);

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(r => r.AccessControlType == AccessControlType.Allow)
            .ToList();

        Assert.Equal(2, rules.Count);

        var currentUser = WindowsIdentity.GetCurrent().User!.Value;
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
        var ruleSids = rules.Select(r => r.IdentityReference.Value).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var expected = new[] { currentUser, systemSid }.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, ruleSids);
    }

    private static string CreateSessionDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "recorder-sec-win-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
