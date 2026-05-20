namespace SemaRepair.Api.Dtos;

/// <summary>
/// Request body for POST /api/chat/stream.
/// Sent by the frontend on every message.
/// The backend is stateless — all context must be in each request.
/// </summary>
public sealed record ChatRequest(
    /// <summary>The mechanic's latest message.</summary>
    string Message,

    /// <summary>
    /// All previous turns in this conversation.
    /// The frontend accumulates these and sends them on every request.
    /// </summary>
    IReadOnlyList<ConversationTurn> History,

    /// <summary>
    /// The confirmed car — null until the mechanic confirms.
    /// Once set, repair searches are filtered to this engine code.
    /// </summary>
    ConfirmedCarDto? Car
);
