using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace CodexQuotaPet
{
    public sealed class ResetCredit
    {
        public DateTime? ExpiresAtUtc;
        public bool NeverExpires;
    }

    public sealed class ResetCreditsSnapshot
    {
        public long? AvailableCount;
        public List<ResetCredit> Credits = new List<ResetCredit>();
        public bool DetailsAvailable;
        public DateTime? LastSuccessUtc;
    }

    // Keep only the fields needed by the tooltip. Credit identifiers and account
    // metadata must not travel into UI snapshots or diagnostic serialization.
    internal static class ResetCredits
    {
        internal static ResetCreditsSnapshot Parse(object value, DateTime updatedAtUtc)
        {
            var result = new ResetCreditsSnapshot();
            var summary = value as Dictionary<string, object>;
            object count;
            long parsedCount;
            if (summary == null || !summary.TryGetValue("availableCount", out count) ||
                !TryNonnegativeInteger(count, out parsedCount)) return result;

            result.AvailableCount = parsedCount;
            result.LastSuccessUtc = updatedAtUtc;
            object details;
            IList credits = summary.TryGetValue("credits", out details) ? details as IList : null;
            result.DetailsAvailable = credits != null;
            if (credits == null) return result;
            foreach (object item in credits)
            {
                var credit = item as Dictionary<string, object>;
                object status;
                if (credit == null || !credit.TryGetValue("status", out status) ||
                    !String.Equals(status as string, "available", StringComparison.Ordinal)) continue;
                var parsed = new ResetCredit();
                object expiresAt;
                if (credit.TryGetValue("expiresAt", out expiresAt))
                {
                    if (expiresAt == null) parsed.NeverExpires = true;
                    else
                    {
                        long seconds;
                        if (TryNonnegativeInteger(expiresAt, out seconds))
                        {
                            try { parsed.ExpiresAtUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds); }
                            catch (ArgumentOutOfRangeException) { }
                        }
                    }
                }
                result.Credits.Add(parsed);
            }
            result.Credits.Sort(CompareCredits);
            return result;
        }

        private static int CompareCredits(ResetCredit first, ResetCredit second)
        {
            if (first.NeverExpires != second.NeverExpires) return first.NeverExpires ? 1 : -1;
            if (first.ExpiresAtUtc.HasValue != second.ExpiresAtUtc.HasValue) return first.ExpiresAtUtc.HasValue ? -1 : 1;
            return Nullable.Compare(first.ExpiresAtUtc, second.ExpiresAtUtc);
        }

        internal static ResetCreditsSnapshot Copy(ResetCreditsSnapshot source)
        {
            if (source == null) return null;
            var result = new ResetCreditsSnapshot
            {
                AvailableCount = source.AvailableCount,
                DetailsAvailable = source.DetailsAvailable,
                LastSuccessUtc = source.LastSuccessUtc
            };
            if (source.Credits != null)
                foreach (ResetCredit credit in source.Credits)
                    if (credit != null) result.Credits.Add(new ResetCredit { ExpiresAtUtc = credit.ExpiresAtUtc, NeverExpires = credit.NeverExpires });
            return result;
        }

        private static bool TryNonnegativeInteger(object value, out long result)
        {
            result = 0;
            if (!(value is byte || value is sbyte || value is short || value is ushort ||
                  value is int || value is uint || value is long || value is ulong ||
                  value is decimal || value is double || value is float)) return false;
            try
            {
                if (value is double || value is float)
                {
                    double floating = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    if (Double.IsNaN(floating) || Double.IsInfinity(floating) || floating != Math.Truncate(floating)) return false;
                }
                decimal number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                if (number < 0 || number > Int64.MaxValue || number != Decimal.Truncate(number)) return false;
                result = (long)number;
                return true;
            }
            catch (OverflowException) { return false; }
        }
    }
}
