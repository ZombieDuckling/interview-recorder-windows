using System.Text;
using System.Text.Json;
using Recorder.Core.Formats;

namespace Recorder.Core.Tests;

public sealed class FormatJsonRoundTripTests
{
    private static readonly JsonSerializerOptions CanonicalCompareOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
    };

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void SessionExample_RoundTrip_MatchesFixtureBytes()
    {
        var original = File.ReadAllText(FixturePath("session.example.json"));
        var manifest = FormatJson.DeserializeSession(original);
        var roundTripped = FormatJson.SerializeSession(manifest);
        AssertJsonEquivalent(original, roundTripped);
    }

    [Fact]
    public void TranscriptExample_RoundTrip_MatchesFixtureBytes()
    {
        var original = File.ReadAllText(FixturePath("transcript.example.json"));
        var transcript = FormatJson.DeserializeTranscript(original);
        var roundTripped = FormatJson.SerializeTranscript(transcript);
        AssertJsonEquivalent(original, roundTripped);
    }

    [Fact]
    public void Session_UnknownTopLevelProperty_IsPreservedOnRoundTrip()
    {
        var original = File.ReadAllText(FixturePath("session.example.json"));
        using var doc = JsonDocument.Parse(original);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, IndentSize = 2 }))
        {
            writer.WriteStartObject();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WriteString("futureField", "keep-me");
            writer.WriteEndObject();
        }

        var withExtra = Encoding.UTF8.GetString(stream.ToArray());
        var manifest = FormatJson.DeserializeSession(withExtra);
        var roundTripped = FormatJson.SerializeSession(manifest);
        using var roundDoc = JsonDocument.Parse(roundTripped);
        Assert.True(roundDoc.RootElement.TryGetProperty("futureField", out var future));
        Assert.Equal("keep-me", future.GetString());
    }

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
