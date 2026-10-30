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

    [Fact]
    public async Task DpapiKey_RoundTripsAndSurvivesNewProtector()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sessionDir = CreateSessionDirectory();
        var protector = new FileProtector();
        await protector.EnsureSessionKeyAsync(sessionDir, CancellationToken.None);
        var path = Path.Combine(sessionDir, "p.bin");
        await using (var w = protector.OpenEncryptedWrite(sessionDir, path))
        {
            await w.WriteAsync(new byte[] { 1, 2, 3 });
        }

        // The on-disk key blob is DPAPI-wrapped, not the raw 32-byte key.
        var blob = await File.ReadAllBytesAsync(CryptoConstants.SessionKeyPath(sessionDir));
        Assert.True(blob.Length > CryptoConstants.SessionKeyBytes);

        var fresh = new FileProtector();
        await fresh.EnsureSessionKeyAsync(sessionDir, CancellationToken.None);
        await using var r = fresh.OpenDecryptedRead(sessionDir, path);
        var got = new byte[3];
        await r.ReadExactlyAsync(got);
        Assert.Equal(new byte[] { 1, 2, 3 }, got);
    }

    [Fact]
    public void RestrictToCurrentUser_NewFilesInheritOnlyUserAndSystem()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sessionDir = CreateSessionDirectory();
        new FileProtector().RestrictToCurrentUser(sessionDir);
        var file = Path.Combine(sessionDir, "later.txt");
        File.WriteAllText(file, "x");

        var rules = new FileInfo(file).GetAccessControl(AccessControlSections.Access)
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(r => r.AccessControlType == AccessControlType.Allow)
            .Select(r => r.IdentityReference.Value)
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            WindowsIdentity.GetCurrent().User!.Value,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value,
        }.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, rules);
    }

    private static string CreateSessionDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "recorder-sec-win-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
