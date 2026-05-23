using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SemaRepair.Api.Dtos;
using SemaRepair.Api.Services;

namespace SemaRepair.Api.Controllers;

/// <summary>
/// Single endpoint for the chat interface.
/// Zero business logic — delegates entirely to RepairOrchestrator.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class ChatController : ControllerBase
{
    private readonly RepairOrchestrator _orchestrator;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        RepairOrchestrator orchestrator,
        ILogger<ChatController> logger)
    {
        _orchestrator = orchestrator;
        _logger       = logger;
    }

    [HttpPost("stream")]
    public async Task StreamAsync(
        [FromBody] ChatRequest request,
        CancellationToken ct)
    {
        Response.Headers["Content-Type"]      = "text/event-stream";
        Response.Headers["Cache-Control"]     = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            var confirmedEngineCode = request.Car?.CodiceMotore;

            await foreach (var chunk in _orchestrator.StreamAsync(
                request.History,
                request.Message,
                confirmedEngineCode,
                ct))
            {
                await Response.WriteAsync(
                    $"data: {{\"text\":{JsonSerializer.Serialize(chunk)}}}\n\n",
                    ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Client disconnected.");
        }
        catch (Exception ex)
        {
            _logger.LogError("Stream error: {Error}", ex.Message);
            await Response.WriteAsync(
                $"data: {{\"error\":{JsonSerializer.Serialize(ex.Message)}}}\n\n",
                ct);
        }
        finally
        {
            await Response.WriteAsync("data: [DONE]\n\n", ct);
        }
    }
}
