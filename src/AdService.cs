using System;
using System.DirectoryServices;
using System.DirectoryServices.AccountManagement;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace BorgionPasswordChange
{
    /// <summary>Wachtwoordbeleid zoals het domein dat afdwingt (alleen voor uitleg in de app; de DC is leidend).</summary>
    public sealed class PasswordPolicy
    {
        public int MinLength { get; set; } = 8;
        public bool Complexity { get; set; } = true;
        public int History { get; set; }
        public double MinAgeDays { get; set; }
        public bool FromDomain { get; set; }
    }

    public sealed class UserInfo
    {
        public string Domain { get; set; }
        public string SamAccountName { get; set; }
        public string DisplayName { get; set; }
        public DateTime? PasswordExpires { get; set; }
        public bool PasswordNeverExpires { get; set; }
        public PasswordPolicy Policy { get; set; } = new PasswordPolicy();
    }

    /// <summary>Fout met een gebruikersvriendelijke (Nederlandse) melding.</summary>
    public sealed class AdException : Exception
    {
        public AdException(string message, Exception inner = null) : base(message, inner) { }
    }

    public static class AdService
    {
        /// <summary>True als de ingelogde gebruiker een lokaal account is (geen domeinaccount).</summary>
        public static bool IsLocalAccount()
        {
            return string.Equals(Environment.UserDomainName, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        }

        public static UserInfo LoadCurrentUser(int fallbackMinLength)
        {
            var info = new UserInfo
            {
                Domain = Environment.UserDomainName,
                SamAccountName = Environment.UserName,
                DisplayName = Environment.UserName,
            };
            info.Policy.MinLength = fallbackMinLength;

            try
            {
                using (var ctx = new PrincipalContext(ContextType.Domain))
                using (var up = FindCurrent(ctx))
                {
                    info.SamAccountName = up.SamAccountName ?? info.SamAccountName;
                    info.DisplayName = string.IsNullOrWhiteSpace(up.DisplayName) ? info.SamAccountName : up.DisplayName;
                    info.PasswordNeverExpires = up.PasswordNeverExpires;

                    var de = (DirectoryEntry)up.GetUnderlyingObject();
                    ReadExpiry(de, info);
                    ReadDomainPolicy(info.Policy);
                    ReadFineGrainedPolicy(de, info.Policy);
                }
            }
            catch (Exception ex) when (!(ex is AdException))
            {
                throw Translate(ex);
            }
            return info;
        }

        public static void ChangePassword(string currentPassword, string newPassword)
        {
            try
            {
                using (var ctx = new PrincipalContext(ContextType.Domain))
                using (var up = FindCurrent(ctx))
                {
                    up.ChangePassword(currentPassword, newPassword);
                }
            }
            catch (Exception ex) when (!(ex is AdException))
            {
                throw Translate(ex);
            }
        }

        static UserPrincipal FindCurrent(PrincipalContext ctx)
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value;
            var up = sid == null ? null : UserPrincipal.FindByIdentity(ctx, IdentityType.Sid, sid);
            if (up == null)
                throw new AdException("Je account is niet gevonden in Active Directory.");
            return up;
        }

        static void ReadExpiry(DirectoryEntry de, UserInfo info)
        {
            try
            {
                de.RefreshCache(new[] { "msDS-UserPasswordExpiryTimeComputed" });
                var raw = LargeInt(de.Properties["msDS-UserPasswordExpiryTimeComputed"].Value);
                if (raw <= 0 || raw == long.MaxValue)
                    info.PasswordNeverExpires = true;
                else
                    info.PasswordExpires = DateTime.FromFileTime(raw);
            }
            catch { /* niet kritiek */ }
        }

        static void ReadDomainPolicy(PasswordPolicy p)
        {
            try
            {
                string dnc;
                using (var rootDse = new DirectoryEntry("LDAP://RootDSE"))
                    dnc = rootDse.Properties["defaultNamingContext"].Value as string;
                if (string.IsNullOrEmpty(dnc)) return;

                using (var domain = new DirectoryEntry("LDAP://" + dnc))
                {
                    if (domain.Properties["minPwdLength"].Value is int min) p.MinLength = min;
                    if (domain.Properties["pwdProperties"].Value is int props) p.Complexity = (props & 1) != 0;
                    if (domain.Properties["pwdHistoryLength"].Value is int hist) p.History = hist;
                    var minAge = LargeInt(domain.Properties["minPwdAge"].Value);
                    p.MinAgeDays = Math.Abs(minAge) / (double)TimeSpan.TicksPerDay;
                    p.FromDomain = true;
                }
            }
            catch { /* standaardwaarden houden */ }
        }

        static void ReadFineGrainedPolicy(DirectoryEntry user, PasswordPolicy p)
        {
            // Fine-Grained Password Policy (PSO): gewone gebruikers mogen dit vaak niet lezen; dan negeren.
            try
            {
                user.RefreshCache(new[] { "msDS-ResultantPSO" });
                var psoDn = user.Properties["msDS-ResultantPSO"].Value as string;
                if (string.IsNullOrEmpty(psoDn)) return;
                using (var pso = new DirectoryEntry("LDAP://" + psoDn))
                {
                    if (pso.Properties["msDS-MinimumPasswordLength"].Value is int min) p.MinLength = min;
                    if (pso.Properties["msDS-PasswordComplexityEnabled"].Value is bool cx) p.Complexity = cx;
                    if (pso.Properties["msDS-PasswordHistoryLength"].Value is int hist) p.History = hist;
                    var minAge = LargeInt(pso.Properties["msDS-MinimumPasswordAge"].Value);
                    p.MinAgeDays = Math.Abs(minAge) / (double)TimeSpan.TicksPerDay;
                }
            }
            catch { }
        }

        static long LargeInt(object value)
        {
            if (value == null) return 0;
            if (value is long l) return l;
            var t = value.GetType();
            var hi = (int)t.InvokeMember("HighPart", BindingFlags.GetProperty, null, value, null);
            var lo = (int)t.InvokeMember("LowPart", BindingFlags.GetProperty, null, value, null);
            return ((long)hi << 32) | (uint)lo;
        }

        // ------------------------------------------------------------------ foutvertaling

        const string WrongPassword = "Je huidige wachtwoord is onjuist. Controleer ook of Caps Lock aan staat.";
        const string PolicyFailed =
            "Het nieuwe wachtwoord voldoet niet aan het wachtwoordbeleid van het domein.\n" +
            "Controleer de lengte en complexiteit en gebruik geen wachtwoord dat je eerder hebt gebruikt. " +
            "Let op: je wachtwoord kan vaak maar één keer per dag worden gewijzigd.";

        static AdException Translate(Exception ex)
        {
            if (ex is PrincipalServerDownException)
                return new AdException("Er is geen verbinding met het domein. Controleer je netwerk- of VPN-verbinding en probeer het opnieuw.", ex);

            foreach (var code in ErrorCodes(ex))
            {
                switch (code)
                {
                    case 0x80070056: // ERROR_INVALID_PASSWORD
                    case 0x8007052B: // ERROR_WRONG_PASSWORD
                    case 0x8007052E: // ERROR_LOGON_FAILURE
                        return new AdException(WrongPassword, ex);
                    case 0x800708C5: // NERR_PasswordTooShort
                    case 0x8007052D: // ERROR_PASSWORD_RESTRICTION
                    case 0x8007202F: // LDAP constraint violation
                        return new AdException(PolicyFailed, ex);
                    case 0x80070775: // ERROR_ACCOUNT_LOCKED_OUT
                        return new AdException("Je account is geblokkeerd na te veel mislukte pogingen. Neem contact op met de servicedesk.", ex);
                    case 0x80070005: // E_ACCESSDENIED
                        return new AdException("Je mag je wachtwoord op dit moment niet wijzigen. Mogelijk is de minimale wachtwoordleeftijd nog niet verstreken, of is wijzigen voor jouw account uitgeschakeld.", ex);
                    case 0x8007054B: // ERROR_NO_SUCH_DOMAIN
                    case 0x8007203A: // server not operational
                    case 0x80070035: // network path not found
                        return new AdException("Er is geen verbinding met het domein. Controleer je netwerk- of VPN-verbinding en probeer het opnieuw.", ex);
                }
            }

            if (ex is PasswordException)
                return new AdException(PolicyFailed, ex);

            return new AdException("Het wijzigen is niet gelukt: " + ex.Message, ex);
        }

        static System.Collections.Generic.IEnumerable<uint> ErrorCodes(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is COMException com) yield return unchecked((uint)com.ErrorCode);
                yield return unchecked((uint)e.HResult);
                var m = Regex.Match(e.Message ?? "", @"0x([0-9A-Fa-f]{8})");
                if (m.Success) yield return Convert.ToUInt32(m.Groups[1].Value, 16);
            }
        }
    }
}
