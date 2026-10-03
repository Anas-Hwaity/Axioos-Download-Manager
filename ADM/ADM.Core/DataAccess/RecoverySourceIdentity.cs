using System;

namespace ADM.Core.DataAccess
{
    public enum RecoverySourceIdentityDecision
    {
        Equivalent,
        Changed,
        InsufficientEvidence
    }

    public sealed class RecoverySourceIdentity
    {
        public string StrongETag { get; set; }
        public string ContentHash { get; set; }
        public string StrongLastModified { get; set; }
        public long? ExpectedLength { get; set; }
        public string SampledByteHash { get; set; }
    }

    public static class RecoverySourceIdentityEvaluator
    {
        public static RecoverySourceIdentityDecision Evaluate(RecoverySourceIdentity durable, RecoverySourceIdentity candidate)
        {
            if (durable == null || candidate == null)
            {
                return RecoverySourceIdentityDecision.InsufficientEvidence;
            }

            if (IsStrongETag(durable.StrongETag))
            {
                if (!IsStrongETag(candidate.StrongETag))
                {
                    return RecoverySourceIdentityDecision.InsufficientEvidence;
                }
                return string.Equals(durable.StrongETag, candidate.StrongETag, StringComparison.Ordinal)
                    ? RecoverySourceIdentityDecision.Equivalent
                    : RecoverySourceIdentityDecision.Changed;
            }

            if (!string.IsNullOrWhiteSpace(durable.ContentHash))
            {
                if (string.IsNullOrWhiteSpace(candidate.ContentHash))
                {
                    return RecoverySourceIdentityDecision.InsufficientEvidence;
                }
                return string.Equals(durable.ContentHash, candidate.ContentHash, StringComparison.OrdinalIgnoreCase)
                    ? RecoverySourceIdentityDecision.Equivalent
                    : RecoverySourceIdentityDecision.Changed;
            }

            if (!string.IsNullOrWhiteSpace(durable.StrongLastModified))
            {
                if (string.IsNullOrWhiteSpace(candidate.StrongLastModified) || !LengthsCompatible(durable, candidate))
                {
                    return RecoverySourceIdentityDecision.Changed;
                }
                return string.Equals(durable.StrongLastModified, candidate.StrongLastModified, StringComparison.Ordinal)
                    ? RecoverySourceIdentityDecision.Equivalent
                    : RecoverySourceIdentityDecision.Changed;
            }

            if (!string.IsNullOrWhiteSpace(durable.SampledByteHash))
            {
                if (string.IsNullOrWhiteSpace(candidate.SampledByteHash) || !LengthsCompatible(durable, candidate))
                {
                    return RecoverySourceIdentityDecision.Changed;
                }
                return string.Equals(durable.SampledByteHash, candidate.SampledByteHash, StringComparison.OrdinalIgnoreCase)
                    ? RecoverySourceIdentityDecision.Equivalent
                    : RecoverySourceIdentityDecision.Changed;
            }

            return RecoverySourceIdentityDecision.InsufficientEvidence;
        }

        public static bool IsStrongETag(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && !value.TrimStart().StartsWith("W/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LengthsCompatible(RecoverySourceIdentity durable, RecoverySourceIdentity candidate)
        {
            return durable.ExpectedLength.HasValue && candidate.ExpectedLength.HasValue && durable.ExpectedLength.Value == candidate.ExpectedLength.Value;
        }
    }
}
