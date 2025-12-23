using Microsoft.Extensions.Logging;
using SayP.Application.Interfaces;
using SayP.Domain.Interfaces;

namespace SayP.Application.Services;

public class GeminiService : IGeminiService
{
    private readonly IAIProvider _aiProvider;
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(
        IAIProvider aiProvider,
        ILogger<GeminiService> logger)
    {
        _aiProvider = aiProvider;
        _logger = logger;
    }

    public async Task<string> ProcessAudioAsync(
        byte[] audioData, 
        string prompt, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing audio with Gemini. Size: {Size} bytes", audioData.Length);

            // Determine MIME type based on audio data
            // Most mobile recordings are either WAV or M4A
            var mimeType = DetermineMimeType(audioData);
            
            _logger.LogInformation("Detected MIME type: {MimeType}", mimeType);

            // Use Gemini's audio processing capability
            var result = await _aiProvider.ExtractCommandFromAudioAsync(
                audioData,
                mimeType,
                prompt,
                cancellationToken
            );

            if (!result.Success || string.IsNullOrWhiteSpace(result.CommandJson))
            {
                _logger.LogWarning("Gemini audio processing failed or returned empty result");
                return string.Empty;
            }

            // For transcription, the result might be in CommandJson or we need to parse it
            // If it's a simple transcription, Gemini returns the text directly
            var transcribedText = result.CommandJson;

            _logger.LogInformation("Audio transcribed successfully. Length: {Length} characters", 
                transcribedText.Length);

            return transcribedText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audio with Gemini");
            throw;
        }
    }

    private string DetermineMimeType(byte[] audioData)
    {
        // Check file signature (magic numbers)
        if (audioData.Length < 12)
            return "audio/wav"; // Default

        // WAV: RIFF....WAVE
        if (audioData[0] == 0x52 && audioData[1] == 0x49 && 
            audioData[2] == 0x46 && audioData[3] == 0x46)
        {
            return "audio/wav";
        }

        // M4A/MP4: ....ftyp
        if (audioData.Length >= 12 &&
            audioData[4] == 0x66 && audioData[5] == 0x74 &&
            audioData[6] == 0x79 && audioData[7] == 0x70)
        {
            return "audio/mp4";
        }

        // MP3: ID3 or 0xFF 0xFB
        if ((audioData[0] == 0x49 && audioData[1] == 0x44 && audioData[2] == 0x33) ||
            (audioData[0] == 0xFF && (audioData[1] & 0xE0) == 0xE0))
        {
            return "audio/mpeg";
        }

        // OGG: OggS
        if (audioData[0] == 0x4F && audioData[1] == 0x67 &&
            audioData[2] == 0x67 && audioData[3] == 0x53)
        {
            return "audio/ogg";
        }

        // Default to WAV
        return "audio/wav";
    }
}
