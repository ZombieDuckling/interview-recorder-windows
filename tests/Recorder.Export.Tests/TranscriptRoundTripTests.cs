using System.Text.Json;
using Recorder.Core.Formats;

namespace Recorder.Export.Tests;

public sealed class TranscriptRoundTripTests
{
    private static readonly JsonSerializerOptions CanonicalCompareOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
    };

    [Fact]
    public void TranscriptExample_round_trip_matches_shared_fixture()
    {
        var original = File.ReadAllText(FixturePath("transcript.example.json"));
        var transcript = FormatJson.DeserializeTranscript(original);
        var roundTripped = FormatJson.SerializeTranscript(transcript);
        AssertJsonEquivalent(original, roundTripped);
    }

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static void AssertJsonEquivalent(string expectedJson, string actualJson)
    {
        var expected = NormalizeJson(expectedJson);
        var actual = NormalizeJson(actualJson);
        Assert.Equal(expected, actual);
    }

    private static string NormalizeJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc, CanonicalCompareOptions);
    }
}
