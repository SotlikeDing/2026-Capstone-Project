using WebApp.Models;

namespace WebApp.Services
{
    /// <summary>
    /// Carries the offer built in the AI Helper to the role's normal editor within the same Blazor circuit (scoped per user tab).
    /// The AI Helper puts a draft here and navigates to the editor with ?ai=1; the editor takes it once, pre-fills its form and
    /// shows that the values came from the AI. The user still reviews and saves with the editor's own button.
    /// </summary>
    public class AiDraftHandoff
    {
        private AiAppliedDraft? _pending;

        public void Put(AiAppliedDraft draft) => _pending = draft;

        /// <summary>Returns and clears the pending draft if it is for this role (and, when targetId is given, for this record).</summary>
        public AiAppliedDraft? Take(string roleCode, int? targetId = null)
        {
            var d = _pending;
            if (d == null || !string.Equals(d.RoleCode, roleCode, StringComparison.OrdinalIgnoreCase)) return null;
            if (targetId.HasValue && d.TargetId != targetId.Value) return null;
            _pending = null;
            return d;
        }
    }
}
