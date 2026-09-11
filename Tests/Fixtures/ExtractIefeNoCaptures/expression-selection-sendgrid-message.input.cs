using System;

class EmailAddress
{
    public EmailAddress(string? email, string? name = null) { }
}

class SendGridMessage
{
    public EmailAddress? From { get; set; }
    public string? Subject { get; set; }
    public string? TemplateId { get; set; }
    public EmailAddress? ReplyTo { get; set; }
}

class EmailDefaults
{
    public string FromEmail = "";
    public string FromName = ""; 
    public string Subject = "";
    public string ReplyToEmail = "";
}

class EmailSettings
{
    public EmailDefaults Defaults = new EmailDefaults();
}

class EmailSender
{
    private readonly EmailSettings _emailSettings = new EmailSettings();

    void Send(string? fromEmail, string? fromName, string? subjectOverride, string emailTemplateId, string? replyToEmail)
    {
        var message = new SendGridMessage
        {
            From = /*[*/new EmailAddress(
                fromEmail ?? _emailSettings.Defaults.FromEmail,
                fromName ?? _emailSettings.Defaults.FromName
            )/*]*/,
            Subject = subjectOverride ?? _emailSettings.Defaults.Subject,
            TemplateId = emailTemplateId,
            ReplyTo = new EmailAddress(replyToEmail ?? _emailSettings.Defaults.ReplyToEmail)
        };
        Console.WriteLine(message);
    }
}
