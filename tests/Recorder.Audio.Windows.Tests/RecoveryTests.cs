using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Recorder.Core.Formats;

namespace Recorder.Audio.Windows.Tests;

public sealed class RecoveryTests
{
    [Fact]
    public async Task RepairsOnlyUnfinishedTailAndRecordsHash()
    {
        using var folder = new TestFolder();
        using (var writer = new PcmChunkWriter(folder.Path, StreamRole.Meeting, _ => { }))
            writer.Write(new short[9600]);
        var path = System.IO.Path.Combine(folder.Path, "capture/meeting/000001.wav");
        using (var file = new FileStream(path, FileMode.Append)) file.WriteByte(99);
        var result = await new CaptureRecovery().RecoverAsync(folder.Path, CancellationToken.None);
        Assert.NotNull(result);
        Assert.False(result.Complete);
        var stream = Assert.Single(result.Streams);
        Assert.Equal(TimeSpan.FromSeconds(0.2), stream.Duration);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(19244, bytes.Length);
        Assert.Equal(19200, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40)));
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        Assert.Equal(hash, folder.Manifest["hashes"]!["capture/meeting/000001.wav"]!.GetValue<string>());
        Assert.True(folder.Manifest["streams"]![0]!["chunks"]![0]!["recovered"]!.GetValue<bool>());
        // Recovery is idempotent; already closed originals remain byte-for-byte intact.
        await new CaptureRecovery().RecoverAsync(folder.Path, CancellationToken.None);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task DropsUnfinishedTailShorterThanPointOneSecond()
    {
        using var folder = new TestFolder();
        using (var writer = new PcmChunkWriter(folder.Path, StreamRole.Microphone, _ => { }))
            writer.Write(new short[4799]);
        Assert.Null(await new CaptureRecovery().RecoverAsync(folder.Path, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(System.IO.Path.Combine(folder.Path, "capture/mic"), "*.wav"));
    }

    [Fact]
    public async Task RefusesToRepairTamperedClosedChunk()
    {
        using var folder = new TestFolder();
        var journal = new CaptureManifest(folder.Path);
        journal.RegisterStream(StreamRole.Microphone, 960, false);
        using (var writer = new PcmChunkWriter(folder.Path, StreamRole.Microphone, c => journal.CloseChunk(StreamRole.Microphone, c)))
        {
            writer.Write(new short[4800]);
            writer.Complete();
        }
        var good = await new CaptureRecovery().RecoverAsync(folder.Path, CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(0.02), Assert.Single(good!.Streams).StartOffset);
        Assert.Equal(TimeSpan.FromSeconds(0.12), good.Duration);
        var path = System.IO.Path.Combine(folder.Path, "capture/mic/000001.wav");
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Write)) { file.Position = 100; file.WriteByte(99); }
        var corrupted = File.ReadAllBytes(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => new CaptureRecovery().RecoverAsync(folder.Path, CancellationToken.None));
        Assert.Equal(corrupted, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task RefusesUnknownSchemaWithoutChangingManifest()
    {
        using var folder = new TestFolder();
        var manifest = folder.Manifest;
        manifest["schemaVersion"] = "2.0.0";
        var path = System.IO.Path.Combine(folder.Path, "session.json");
        File.WriteAllText(path, manifest.ToJsonString());
        var before = File.ReadAllBytes(path);
        await Assert.ThrowsAsync<NotSupportedException>(() => new CaptureRecovery().RecoverAsync(folder.Path, CancellationToken.None));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void JournalPreservesUnrelatedFieldsAndRefusesMissingConsent()
    {
        using var folder = new TestFolder();
        var manifest = folder.Manifest;
        manifest["participantId"] = "P-TEST";
        File.WriteAllText(System.IO.Path.Combine(folder.Path, "session.json"), manifest.ToJsonString());
        var journal = new CaptureManifest(folder.Path);
        journal.Begin(DateTimeOffset.UtcNow);
        journal.RegisterStream(StreamRole.Meeting, 0, true);
        Assert.Equal("P-TEST", folder.Manifest["participantId"]!.GetValue<string>());
        Assert.Equal("wasapi-process-loopback", folder.Manifest["streams"]![0]!["source"]!.GetValue<string>());
        manifest = folder.Manifest;
        manifest["consent"]!["confirmed"] = false;
        File.WriteAllText(System.IO.Path.Combine(folder.Path, "session.json"), manifest.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => journal.Begin(DateTimeOffset.UtcNow));
    }
}
