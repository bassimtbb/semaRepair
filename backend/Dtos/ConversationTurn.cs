namespace SemaRepair.Api.Dtos;

/// <summary>One turn in the conversation history.</summary>
public sealed record ConversationTurn(
    /// <summary>"user" or "assistant"</summary>
    string Role,
    /// <summary>The text content of this turn.</summary>
    string Content
);
