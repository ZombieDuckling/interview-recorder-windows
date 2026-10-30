using System.Runtime.CompilerServices;

namespace Recorder.Security.Tests;

/// <summary>Off Windows there is no DPAPI; tests install a plaintext wrapper that lives only in this test assembly.</summary>
internal sealed class PlaintextTestKeyWrapper : ISessionKeyWrapper
{
    public byte[] Wrap(byte[] key) => [.. key];

    public byte[] Unwrap(byte[] blob) => [.. blob];
}

internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Init()
    {
        if (!OperatingSystem.IsWindows())
        {
            SessionKeyStore.Wrapper = new PlaintextTestKeyWrapper();
        }
    }
}
