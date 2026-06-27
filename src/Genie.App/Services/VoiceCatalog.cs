using System;
using System.Collections.Generic;
using System.Linq;

namespace Genie.App.Services;

/// <summary>One downloadable sherpa-onnx Piper voice.</summary>
/// <param name="Id">Folder name (matches the tarball's internal folder, so the
/// extracted voice lands at <c>&lt;TtsVoiceDir&gt;/{Id}/</c>).</param>
/// <param name="Alias">Short name accepted by <c>#tts install</c>.</param>
/// <param name="DisplayName">Human-facing label.</param>
/// <param name="Url">.tar.bz2 download (sherpa-onnx GitHub release).</param>
/// <param name="ApproxMb">Rough download size for the confirmation message.</param>
public sealed record VoiceInfo(string Id, string Alias, string DisplayName, string Url, int ApproxMb);

/// <summary>
/// The small curated set of voices <c>#tts install</c> can fetch. URLs are
/// sherpa-onnx's pre-converted Piper models (model + tokens.txt + espeak-ng-data
/// in one archive). Kept intentionally short; grows as we vet more voices.
/// </summary>
public static class VoiceCatalog
{
    private const string Base = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/";

    public static readonly VoiceInfo Default =
        new("vits-piper-en_US-amy-low", "amy", "Amy (US, low)", Base + "vits-piper-en_US-amy-low.tar.bz2", 67);

    public static readonly IReadOnlyList<VoiceInfo> All = new[]
    {
        Default,
        new VoiceInfo("vits-piper-en_US-lessac-medium", "lessac", "Lessac (US, medium)",
            Base + "vits-piper-en_US-lessac-medium.tar.bz2", 63),
        new VoiceInfo("vits-piper-en_US-glados", "glados", "GLaDOS (US)",
            Base + "vits-piper-en_US-glados.tar.bz2", 63),
    };

    /// <summary>Resolve a voice by id or short alias (case-insensitive).</summary>
    public static VoiceInfo? Find(string idOrAlias) =>
        All.FirstOrDefault(v =>
            v.Id.Equals(idOrAlias, StringComparison.OrdinalIgnoreCase) ||
            v.Alias.Equals(idOrAlias, StringComparison.OrdinalIgnoreCase));
}
