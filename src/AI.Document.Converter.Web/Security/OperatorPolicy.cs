namespace AI.Document.Converter.Web.Security;

// Who may reach the operator console.
//
// SR-SEC-7 requires MFA for production administrators, so the policy demands
// TWO independent things:
//
//   1. The Operator role - granted out of band, never through the web UI. A
//      page that grants administrative access is a privilege-escalation
//      surface, and there is no reason to expose one for something done a
//      handful of times in a system's life.
//
//   2. Evidence that THIS SESSION was established with a second factor. Role
//      alone would mean a stolen password reaches cross-tenant data, which is
//      the exact outcome the MFA requirement exists to prevent. Identity stamps
//      "amr=mfa" on the principal when a session is completed through the
//      two-factor step, so the claim proves how the session was created rather
//      than merely what the account is capable of.
//
// The second point is why enabling two-factor does not immediately unlock the
// console: the existing cookie was minted without it. Signing out and back in
// is the correct behaviour, not a defect.
public static class OperatorPolicy
{
    public const string Name = "OperatorConsole";

    public const string RoleName = "Operator";

    // The claim Identity adds on a two-factor sign-in. Named here rather than
    // written as a literal at the policy, because "amr"/"mfa" is meaningless
    // out of context and easy to mistype into something that silently never
    // matches - a policy that always fails closed is safe, but a typo that
    // makes it always fail is a bug nobody notices until an operator is locked
    // out at 3am.
    public const string AuthenticationMethodClaim = "amr";

    public const string MultiFactorValue = "mfa";
}
