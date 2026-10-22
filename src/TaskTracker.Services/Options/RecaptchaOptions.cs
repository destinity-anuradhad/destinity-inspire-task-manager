namespace TaskTracker.Services.Options;

public class RecaptchaOptions
{
    public bool   Enabled    { get; set; }
    public string SiteKey    { get; set; } = "";
    public string SecretKey  { get; set; } = "";
    public string VerifyUrl  { get; set; } = "https://www.google.com/recaptcha/api/siteverify";
}
