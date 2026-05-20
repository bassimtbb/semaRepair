using SemaRepair.Api.Dtos;

namespace SemaRepair.Api.Services.Interfaces;

/// <summary>
/// Streams a response from the LLM given a system prompt and conversation.
/// Using an interface allows the LLM provider to be swapped without
/// changing the controller or prompt logic.
/// </summary>
public interface IChatService
{
    /// <summary>
    /// Streams text chunks from the LLM.
    /// Each yielded string is a partial JSON text chunk to append to the response.
    /// The complete accumulated text forms a valid JSON object.
    /// </summary>
    /// <param name="systemPrompt">
    /// Instructions for the LLM — what to do, what format to respond in.
    /// </param>
    /// <param name="history">
    /// Previous turns in the conversation for multi-turn context.
    /// Each turn has a Role ("user" or "assistant") and Content (the text).
    /// </param>
    /// <param name="userMessage">The mechanic's latest message.</param>
    /// <param name="ct">Cancellation token — stops streaming if client disconnects.</param>
    IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        IReadOnlyList<ConversationTurn> history,
        string userMessage,
        CancellationToken ct = default);
}
