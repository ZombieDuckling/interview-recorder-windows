using System.Text;

namespace Recorder.Security;

internal static class CryptoConstants
{
    internal const int SessionKeyBytes = 32;
    internal const int FramePlaintextBytes = 64 * 1024;
    internal const int GcmTagBytes = 16;
    internal const int GcmNonceBytes = 12;
    internal const int FileSaltBytes = 16;
    internal const int NonceCounterOffset = 4;
    internal const int FrameIndexBytes = 8;

    internal static ReadOnlySpan<byte> EncryptedFileMagic => "IRG2"u8;

    /// <summary>DPAPI optional entropy (app-specific, not secret).</summary>
    internal static ReadOnlySpan<byte> DpapiEntropy => "InterviewRecorder.Windows.DPAPI.v1"u8;

    internal static ReadOnlySpan<byte> FileKeyInfoLabel => "InterviewRecorder-AES-GCM-file-v1"u8;

    internal static ReadOnlySpan<byte> HmacInfoLabel => "InterviewRecorder-HMAC-SHA256-v1"u8;

    internal const string KeysFolderName = ".keys";
    internal const string SessionKeyFileName = "session.key";

    internal static string SessionKeyPath(string sessionDirectory) =>
        Path.Combine(sessionDirectory, KeysFolderName, SessionKeyFileName);
}
