namespace SayP.Application.Interfaces;

/// <summary>
/// Gemini-specific service for audio processing
/// </summary>
public interface IGeminiService
{
    /// <summary>
    /// Process audio and return transcribed text
    /// </summary>
    Task<string> ProcessAudioAsync(byte[] audioData, string prompt, CancellationToken cancellationToken = default);
}
