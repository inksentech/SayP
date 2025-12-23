using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayP.Application.Interfaces;

namespace SayP.Api.Controllers;

/// <summary>
/// Speech-to-Text using Gemini API
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SpeechController : ControllerBase
{
    private readonly IGeminiService _geminiService;
    private readonly ILogger<SpeechController> _logger;

    public SpeechController(
        IGeminiService geminiService,
        ILogger<SpeechController> logger)
    {
        _geminiService = geminiService;
        _logger = logger;
    }

    /// <summary>
    /// Transcribe audio to text using Gemini
    /// </summary>
    [HttpPost("transcribe")]
    [ProducesResponseType(typeof(TranscribeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TranscribeAudio([FromBody] TranscribeRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.AudioBase64))
            {
                return BadRequest(new { message = "Audio data is required" });
            }

            _logger.LogInformation("Transcribing audio using Gemini (Language: {Language})", request.Language);

            // Convert base64 to byte array
            byte[] audioBytes;
            try
            {
                audioBytes = Convert.FromBase64String(request.AudioBase64);
            }
            catch (FormatException)
            {
                return BadRequest(new { message = "Invalid base64 audio data" });
            }

            // Prepare prompt for Gemini
            var prompt = request.Language switch
            {
                "tr" => "Bu ses kaydını metne dönüştür. Sadece konuşulan metni döndür, başka bir şey ekleme.",
                "en" => "Transcribe this audio to text. Return only the spoken text, nothing else.",
                _ => "Transcribe this audio to text. Return only the spoken text, nothing else."
            };

            // Call Gemini API with audio
            var transcribedText = await _geminiService.ProcessAudioAsync(audioBytes, prompt);

            if (string.IsNullOrWhiteSpace(transcribedText))
            {
                _logger.LogWarning("Gemini returned empty transcription");
                return Ok(new TranscribeResponse 
                { 
                    Text = "",
                    Confidence = 0,
                    Language = request.Language
                });
            }

            _logger.LogInformation("Audio transcribed successfully. Length: {Length} characters", transcribedText.Length);

            return Ok(new TranscribeResponse
            {
                Text = transcribedText.Trim(),
                Confidence = 0.95, // Gemini doesn't provide confidence, use default
                Language = request.Language
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transcribing audio");
            return StatusCode(500, new { message = "An error occurred while transcribing audio" });
        }
    }

    /// <summary>
    /// Get supported languages for transcription
    /// </summary>
    [HttpGet("languages")]
    [ProducesResponseType(typeof(IEnumerable<LanguageInfo>), StatusCodes.Status200OK)]
    public IActionResult GetSupportedLanguages()
    {
        var languages = new[]
        {
            new LanguageInfo { Code = "tr", Name = "Turkish", NativeName = "Türkçe" },
            new LanguageInfo { Code = "en", Name = "English", NativeName = "English" },
            new LanguageInfo { Code = "de", Name = "German", NativeName = "Deutsch" },
            new LanguageInfo { Code = "fr", Name = "French", NativeName = "Français" },
            new LanguageInfo { Code = "es", Name = "Spanish", NativeName = "Español" },
        };

        return Ok(languages);
    }
}

public class TranscribeRequest
{
    public string AudioBase64 { get; set; } = string.Empty;
    public string Language { get; set; } = "tr";
}

public class TranscribeResponse
{
    public string Text { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Language { get; set; } = string.Empty;
}

public class LanguageInfo
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
}
