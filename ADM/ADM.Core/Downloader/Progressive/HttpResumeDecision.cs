using System.Net;

namespace ADM.Core.Downloader.Progressive
{
    public enum HttpResumeDecision
    {
        AppendAllowed,
        OffsetMismatch,
        RepresentationChanged,
        RangeIgnored,
        CandidateFinalVerification,
        ReconcileLength,
        ValidatorRejected,
        RetryOrNeedsAttention
    }

    public static class HttpResumeDecisionEvaluator
    {
        public static HttpResumeDecision Evaluate(
            HttpStatusCode statusCode,
            long requestedStart,
            long contentRangeStart,
            long responseCompleteLength,
            long? expectedCompleteLength,
            long localVerifiedCoverage = -1)
        {
            if (statusCode == HttpStatusCode.PartialContent)
            {
                if (contentRangeStart != requestedStart)
                {
                    return HttpResumeDecision.OffsetMismatch;
                }
                if (expectedCompleteLength.HasValue && responseCompleteLength > 0
                    && expectedCompleteLength.Value != responseCompleteLength)
                {
                    return HttpResumeDecision.RepresentationChanged;
                }
                return HttpResumeDecision.AppendAllowed;
            }

            if (statusCode == HttpStatusCode.OK)
            {
                return HttpResumeDecision.RangeIgnored;
            }

            if (statusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                if (responseCompleteLength > 0 && localVerifiedCoverage == responseCompleteLength)
                {
                    return HttpResumeDecision.CandidateFinalVerification;
                }
                return HttpResumeDecision.ReconcileLength;
            }

            if (statusCode == HttpStatusCode.PreconditionFailed)
            {
                return HttpResumeDecision.ValidatorRejected;
            }

            return HttpResumeDecision.RetryOrNeedsAttention;
        }
    }
}
