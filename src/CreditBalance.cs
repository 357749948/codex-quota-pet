using System;
using System.Collections.Generic;
using System.Globalization;

namespace CodexQuotaPet
{
    public sealed class CreditBalanceSnapshot
    {
        public decimal? Balance;
        public bool Unlimited;
        public DateTime? LastSuccessUtc;
    }

    internal static class CreditBalances
    {
        internal static CreditBalanceSnapshot Parse(object value, DateTime updatedAtUtc)
        {
            var result = new CreditBalanceSnapshot();
            var credits = value as Dictionary<string, object>;
            if (credits == null) return result;
            object unlimited;
            if (credits.TryGetValue("unlimited", out unlimited) && unlimited is bool && (bool)unlimited)
            {
                result.Unlimited = true;
                result.LastSuccessUtc = updatedAtUtc;
                return result;
            }
            object raw;
            decimal balance;
            // The protocol sends a decimal string. Do not pass it through
            // double or use machine-specific decimal/thousands separators.
            if (!credits.TryGetValue("balance", out raw) || !(raw is string) ||
                !Decimal.TryParse((string)raw, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out balance) || balance < 0) return result;
            result.Balance = balance;
            result.LastSuccessUtc = updatedAtUtc;
            return result;
        }

        internal static CreditBalanceSnapshot Copy(CreditBalanceSnapshot source)
        {
            return source == null ? null : new CreditBalanceSnapshot {
                Balance = source.Balance, Unlimited = source.Unlimited, LastSuccessUtc = source.LastSuccessUtc
            };
        }
    }
}
