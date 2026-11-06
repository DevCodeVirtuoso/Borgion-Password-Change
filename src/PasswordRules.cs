using System;
using System.Linq;

namespace BorgionPasswordChange
{
    /// <summary>
    /// Controleert het nieuwe wachtwoord in de app, zodat de gebruiker direct ziet wat er mist.
    /// Volgt de regels van Windows "Wachtwoord moet voldoen aan complexiteitsvereisten".
    /// De domeincontroller blijft leidend (bijv. voor wachtwoordgeschiedenis).
    /// </summary>
    public static class PasswordRules
    {
        static readonly char[] NameDelimiters = { ',', '.', '-', '_', '#', ' ', '\t' };

        public static bool LengthOk(string pw, PasswordPolicy p) => (pw ?? "").Length >= Math.Max(1, p.MinLength);

        public static int Categories(string pw)
        {
            if (string.IsNullOrEmpty(pw)) return 0;
            int n = 0;
            if (pw.Any(char.IsUpper)) n++;
            if (pw.Any(char.IsLower)) n++;
            if (pw.Any(char.IsDigit)) n++;
            if (pw.Any(c => !char.IsLetterOrDigit(c))) n++;
            return n;
        }

        public static bool ComplexityOk(string pw, PasswordPolicy p) => !p.Complexity || Categories(pw) >= 3;

        /// <summary>Wachtwoord bevat niet de accountnaam of delen (3+ tekens) van de weergavenaam.</summary>
        public static bool NameOk(string pw, UserInfo u)
        {
            if (string.IsNullOrEmpty(pw) || u == null) return true;
            if (!string.IsNullOrEmpty(u.SamAccountName) && u.SamAccountName.Length >= 3 &&
                pw.IndexOf(u.SamAccountName, StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            var tokens = (u.DisplayName ?? "").Split(NameDelimiters, StringSplitOptions.RemoveEmptyEntries);
            return !tokens.Any(t => t.Length >= 3 && pw.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Indicatie van de sterkte, 0 t/m 4.</summary>
        public static int Strength(string pw)
        {
            if (string.IsNullOrEmpty(pw)) return 0;
            int score = 1;
            if (pw.Length >= 8 && Categories(pw) >= 3) score++;
            if (pw.Length >= 12) score++;
            if (pw.Length >= 16 && Categories(pw) >= 3) score++;
            if (pw.Distinct().Count() <= 3) score = Math.Min(score, 1);
            return Math.Min(score, 4);
        }
    }
}
