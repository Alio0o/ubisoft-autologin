using UbisoftAutoLogin.Core;

namespace UbisoftAutoLogin.Tests;

public class ClassifierTests
{
    // OCR output captured (and sanitized) from the real Ubisoft Connect 174.2 prompt.
    private static readonly string[] RealPrompt =
    {
        "UBISOFT CONNECT", "Re-enter your password", "Please re-enter your Ubisoft account", "password to play this game.",
        "E-mail:", "someone@example.com", "Password:", "Password", "Recover password", "Lo", "x", "in"
    };

    [Fact]
    public void RealPrompt_IsSafeToFill()
    {
        var c = PromptClassifier.Classify(RealPrompt);
        Assert.Equal(PromptKind.PasswordLogin, c.Kind);
        Assert.True(c.SafeToAutoFill);
    }

    [Fact]
    public void TypedPassword_IsNotEmpty_SoNotFilled()
    {
        var lines = RealPrompt.Where(l => l != "Password").Append("••••").ToArray();
        var c = PromptClassifier.Classify(lines);
        Assert.Equal(PromptKind.PasswordLogin, c.Kind);
        Assert.False(c.PasswordFieldEmpty);
        Assert.False(c.SafeToAutoFill);
    }

    [Fact]
    public void MissingEmail_IsNotFilled()
    {
        var c = PromptClassifier.Classify(RealPrompt.Where(l => !l.Contains('@')).ToArray());
        Assert.False(c.SafeToAutoFill);
    }

    [Theory]
    [InlineData("Enter the verification code we sent you", PromptKind.TwoFactor)]
    [InlineData("Please complete the CAPTCHA", PromptKind.Captcha)]
    [InlineData("Reset your password", PromptKind.AccountRecovery)]
    [InlineData("Unable to connect to Ubisoft servers", PromptKind.NetworkError)]
    public void SecurityAndErrorScreens_AreNeverFilled(string line, PromptKind expected)
    {
        var c = PromptClassifier.Classify(RealPrompt.Append(line).ToArray());
        Assert.Equal(expected, c.Kind);
        Assert.False(c.SafeToAutoFill);
    }

    [Fact]
    public void UnrelatedWindow_IsUnknown()
    {
        var c = PromptClassifier.Classify(new[] { "Settings", "Password:", "Password", "Save" });
        Assert.Equal(PromptKind.Unknown, c.Kind);
    }
}

public class SanitizeTests
{
    [Fact]
    public void Emails_And_Tokens_AreRedacted()
    {
        var s = Redact.Sanitize("user a.b+c@mail.example.org token abcdefghijklmnopqrstuvwxyz0123456789ABCD", 400);
        Assert.DoesNotContain("@", s);
        Assert.Contains("<email>", s);
        Assert.Contains("<long-string>", s);
    }
}

public class CredentialStoreTests
{
    [Fact]
    public void SaveReadDelete_RoundTrip()
    {
        var store = new CredentialStore("UbisoftAutoLogin:UnitTest:" + Guid.NewGuid());
        try
        {
            store.Save("tester@example.com", "p@ss wörd!".AsSpan());
            Assert.True(store.Exists());
            Assert.Equal("tester@example.com", store.ReadUserName());
            var pwd = store.ReadPassword();
            Assert.Equal("p@ss wörd!", new string(pwd!));
            Array.Clear(pwd!);
        }
        finally { store.Delete(); }
        Assert.False(store.Exists());
    }
}

public class VerifierTests
{
    [Fact]
    public void OwnProcess_IsNotTrusted()
    {
        var id = LauncherVerifier.Identify((uint)Environment.ProcessId);
        Assert.False(id.IsTrusted);
        Assert.False(PromptDetector.IsVerifiedPrompt(IntPtr.Zero, out _));
    }
}
