using MobileApi.Models.RowlandWard;

namespace MobileApi.Services;

public interface IRowlandWardEvaluationService
{
    RowlandWardEvaluationSnapshot Evaluate(
        RowlandWardEvaluationRequest request,
        DateTime? evaluatedAtUtc = null
    );

    void AttachCurrentWorldRecord(RowlandWardEvaluationSnapshot evaluation);

    RowlandWardPublicEvaluation ToPublicEvaluation(RowlandWardEvaluationSnapshot evaluation);
}
