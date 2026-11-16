using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using PrintMerkez.Core;
namespace PrintMerkez.Web.Services;

public sealed record DeliveryResult(NotificationState State,string? Detail=null);
public interface IUsageMailSender { Task<DeliveryResult> SendAsync(UsageNotification notification,CancellationToken cancellationToken); }
public sealed class UsageMailSender(IConfiguration config) : IUsageMailSender
{
    public async Task<DeliveryResult> SendAsync(UsageNotification n,CancellationToken cancellationToken)
    {
        var mode=config["Mail:Mode"]??"Preview";
        // Synthetic traffic is never transmitted, even when SMTP is enabled.
        if(n.Source==EventSource.Simulation || mode.Equals("Preview",StringComparison.OrdinalIgnoreCase))
            return new(NotificationState.Previewed,"Önizleme oluşturuldu; dışarı mail gönderilmedi.");
        if(!mode.Equals("Smtp",StringComparison.OrdinalIgnoreCase)) return new(NotificationState.Failed,"Mail:Mode Preview veya Smtp olmalı.");
        var host=config["Mail:Host"]; var from=config["Mail:From"]; var port=config.GetValue("Mail:Port",587);
        var security=config["Mail:Security"]??"StartTls";
        if(string.IsNullOrWhiteSpace(host)||!NotificationLabels.IsEmail(from)||!NotificationLabels.IsEmail(n.RecipientEmail)||port<1||port>65535)
            return new(NotificationState.Failed,"SMTP sunucusu, portu, gönderen veya alıcı yapılandırması eksik/geçersiz.");
        if(from!.EndsWith(".invalid",StringComparison.OrdinalIgnoreCase)||n.RecipientEmail!.EndsWith(".invalid",StringComparison.OrdinalIgnoreCase))
            return new(NotificationState.Failed,"Örnek .invalid adreslerine gerçek gönderim yapılmaz.");
        if(security is not "StartTls" and not "SslOnConnect") return new(NotificationState.Failed,"SMTP güvenliği StartTls veya SslOnConnect olmalı.");
        using var client=new SmtpClient { Timeout=15000, SslProtocols=System.Security.Authentication.SslProtocols.Tls12|System.Security.Authentication.SslProtocols.Tls13 };
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var token=timeout.Token;
        try
        {
            await client.ConnectAsync(host,port,security=="StartTls"?SecureSocketOptions.StartTls:SecureSocketOptions.SslOnConnect,token);
            var username=config["Mail:Username"];
            if(!string.IsNullOrWhiteSpace(username))
            {
                var password=config["Mail:Password"];
                if(string.IsNullOrEmpty(password)) return new(NotificationState.Failed,"SMTP hesabının parolası yapılandırılmamış.");
                await client.AuthenticateAsync(username,password,token);
            }
        }
        catch(AuthenticationException) { return new(NotificationState.Failed,"SMTP kimlik doğrulaması başarısız."); }
        catch(SslHandshakeException) { return new(NotificationState.Failed,"SMTP TLS/sertifika doğrulaması başarısız."); }
        catch(Exception) { return new(NotificationState.Retry,"SMTP bağlantısı kurulamadı; henüz mesaj gönderilmedi."); }
        var message=new MimeMessage { MessageId=n.MessageId,Subject=n.Subject,Date=DateTimeOffset.UtcNow };
        message.From.Add(MailboxAddress.Parse(from)); message.To.Add(MailboxAddress.Parse(n.RecipientEmail));
        message.Body=new TextPart("plain") { Text=n.Body };
        try { await client.SendAsync(message,token); }
        catch(SmtpCommandException ex) { return new((int)ex.StatusCode<500?NotificationState.Retry:NotificationState.Failed,"SMTP sunucusu mesajı reddetti."); }
        catch(Exception) { return new(NotificationState.Uncertain,"Gönderim sırasında bağlantı kesildi. Sunucunun kabulü belirsiz; otomatik tekrar gönderilmeyecek."); }
        try { await client.DisconnectAsync(true,token); } catch(Exception) { /* Send already received the SMTP acceptance response. */ }
        return new(NotificationState.Sent);
    }
}
