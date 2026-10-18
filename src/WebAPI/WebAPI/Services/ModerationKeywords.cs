namespace WebAPI.Services;

/// <summary>
/// Single source of truth for the keyword-based fee-bypass detection used by both
/// chat-message and rating-comment moderation queues.
/// </summary>
public static class ModerationKeywords
{
    public static readonly string[] FeeBypassKeywords =
    {
        "kontant", "cash", "whatsapp", "buite die app", "outside the app",
        "direk betaal", "eft direk", "skype", "bel my direk", "contant"
    };
}
