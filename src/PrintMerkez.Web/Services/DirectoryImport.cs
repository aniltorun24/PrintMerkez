using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;

namespace PrintMerkez.Web.Services;

public sealed class DirectoryExport
{
    public string DomainDns { get; set; } = "";
    public string DomainNetBios { get; set; } = "";
    public string BaseDn { get; set; } = "";
    public DateTime ExportedUtc { get; set; }
    public string ExportedBy { get; set; } = "";
    public List<DirectoryExportUser> Users { get; set; } = [];
}
public sealed class DirectoryExportUser
{
    public string ObjectGuid { get; set; } = "";
    public string SamAccountName { get; set; } = "";
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Department { get; set; }
    public string? Office { get; set; }
    public string? Email { get; set; }
    public string? Manager { get; set; }
    public string DistinguishedName { get; set; } = "";
    public string Sid { get; set; } = "";
    public bool Enabled { get; set; }
    public bool CriticalSystem { get; set; }
}
public sealed record DirectoryImportReport(int Read, int Added, int Updated, int SystemSkipped, int Conflicts,
    int MissingDepartment, int MissingLocation, int LocationConflicts, int MissingEmail, int Disabled,
    int DepartmentsAdded, bool Preview, int ArchivedSkipped = 0);

public static class DirectoryOrganization
{
    public static string Key(string text)
    {
        var decomposed = text.Replace('ı','i').Replace('İ','I').Normalize(NormalizationForm.FormD);
        return new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }
    private static readonly Dictionary<string,string> LocationNames = new()
    {
        ["MOI"]="Mall of İstanbul",["MALLOFISTANBUL"]="Mall of İstanbul",
        ["MOA"]="Mall of Antalya",["MALLOFANTALYA"]="Mall of Antalya",["ANTALYA"]="Mall of Antalya",
        ["KORUPARK"]="Korupark AVM",["KORUPARKAVM"]="Korupark AVM",
        ["TORIUM"]="Torium AVM",["TORIUMAVM"]="Torium AVM",
        ["ZAFERPLAZA"]="Zafer Plaza",["SERAKUTAHYA"]="Sera Kütahya AVM",["SERAKUTAHYAAVM"]="Sera Kütahya AVM",
        ["SAMSUNBULVAR"]="Bulvar Samsun AVM",["BULVARSAMSUNAVM"]="Bulvar Samsun AVM",
        ["FORUMMERSIN"]="Forum Mersin",["FORUMKAYSERI"]="Forum Kayseri",["5LEVENT"]="5. Levent",
        ["TORUNCENTER"]="Torun Center",["TORUNCENTERSANTIYE"]="Torun Center",
        ["TORUNCENTERSATISOFISI"]="Torun Center",["TORUNCENTERSATISOFIS"]="Torun Center"
    };
    private static readonly Dictionary<string,string> DepartmentNames = new()
    {
        ["BILGIISLEM"]="Bilgi İşlem",["IK"]="İK",["INSANKAYNAKLARI"]="İK",["ISLETME"]="İşletme",
        ["YONETIM"]="Yönetim",["MUHASEBE"]="Muhasebe",["PAZARLAMA"]="Pazarlama",["TEKNIK"]="Teknik",
        ["KIRALAMA"]="Kiralama",["ISG"]="İSG",["DANISMA"]="Danışma",["GUVENLIK"]="Güvenlik",["SINEMA"]="Sinema"
    };
    public static string? CanonicalLocation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // Parenthetical tenant/provider labels stay within the explicitly named AVM; Hilton is a separate unknown location.
        var baseName=value.Split('(')[0].Trim();
        return LocationNames.GetValueOrDefault(Key(baseName));
    }
    public static string CanonicalDepartment(string value) => DepartmentNames.GetValueOrDefault(Key(value))
        ?? Regex.Replace(value.Trim(),@"\s+"," ");

    public static string[] OrganizationalUnits(string dn)
    {
        var parts=new List<string>(); var buffer=new StringBuilder(); var escaped=false;
        foreach(var c in dn)
        {
            if(c==','&&!escaped){parts.Add(buffer.ToString());buffer.Clear();continue;}
            buffer.Append(c);
            if(c=='\\'&&!escaped)escaped=true;else escaped=false;
        }
        parts.Add(buffer.ToString());
        return parts.Select(p=>p.Trim()).Where(p=>p.StartsWith("OU=",StringComparison.OrdinalIgnoreCase))
            .Select(p=>Unescape(p[3..])).ToArray();
    }
    private static string Unescape(string value)
    {
        var result=new StringBuilder();
        for(var i=0;i<value.Length;i++)
        {
            if(value[i]!='\\'){result.Append(value[i]);continue;}
            var bytes=new List<byte>();
            while(i+2<value.Length&&value[i]=='\\'&&Uri.IsHexDigit(value[i+1])&&Uri.IsHexDigit(value[i+2]))
            {bytes.Add(Convert.ToByte(value.Substring(i+1,2),16));i+=3;}
            if(bytes.Count>0){result.Append(Encoding.UTF8.GetString(bytes.ToArray()));i--;}
            else if(i+1<value.Length)result.Append(value[++i]);
        }
        return result.ToString();
    }
    public static (string? Department, string? Location, bool Conflict) Resolve(DirectoryExportUser user)
    {
        var units=OrganizationalUnits(user.DistinguishedName);
        var ouLocations=units.Select(CanonicalLocation).Where(x=>x is not null).Distinct().ToArray();
        var officeLocation=CanonicalLocation(user.Office);
        var conflict=ouLocations.Length>1||(ouLocations.Length==1&&officeLocation is not null&&ouLocations[0]!=officeLocation);
        var location=conflict?null:ouLocations.FirstOrDefault()??officeLocation;
        var department=string.IsNullOrWhiteSpace(user.Department)||CanonicalLocation(user.Department) is not null
            ? null : CanonicalDepartment(user.Department);
        if(department is null)
        {
            var usersIndex=Array.FindIndex(units,u=>Key(u)=="USERS");
            if(usersIndex>0)
            {
                var leaf=units[0];
                if(CanonicalLocation(leaf) is null && Key(leaf) is not ("KAVACIK" or "WIFIRADIUS" or "COMPUTERS" or "COMPUTER" or "SERVERS" or "GROUPS"))
                    department=CanonicalDepartment(leaf);
            }
        }
        return (department,location,conflict);
    }
}

public sealed class DirectoryImport(PrintDbContext db)
{
    public async Task<DirectoryImportReport> RunAsync(DirectoryExport export, bool preview, CancellationToken ct=default)
    {
        if(!export.DomainDns.Equals("trnavm.com",StringComparison.OrdinalIgnoreCase)
            ||!export.DomainNetBios.Equals("TRNAVM",StringComparison.OrdinalIgnoreCase)
            ||!export.BaseDn.Equals("DC=trnavm,DC=com",StringComparison.OrdinalIgnoreCase)
            ||export.Users.Count is 0 or >20000)throw new InvalidOperationException("Geçersiz veya beklenen AD alanına ait olmayan aktarım.");
        var guids=new HashSet<string>();var sams=new HashSet<string>();
        foreach(var row in export.Users)
        {
            if(!Guid.TryParse(row.ObjectGuid,out var guid)||guid==Guid.Empty||!guids.Add(guid.ToString("D"))
                ||string.IsNullOrWhiteSpace(row.SamAccountName)||row.SamAccountName.Length>160
                ||row.SamAccountName.Any(char.IsControl)||row.SamAccountName.Contains('\\')
                ||!sams.Add(DirectoryUser.Normalize(row.SamAccountName))
                ||!row.DistinguishedName.EndsWith(","+export.BaseDn,StringComparison.OrdinalIgnoreCase)
                ||row.DistinguishedName.Length>1024 || (row.Department?.Length??0)>256 || (row.Office?.Length??0)>256
                ||(string.IsNullOrWhiteSpace(row.DisplayName)?row.Name:row.DisplayName).Length is 0 or >120)
                throw new InvalidOperationException("AD dosyasında geçersiz veya yinelenen kullanıcı var. Aktarım uygulanmadı.");
        }
        var existing=await db.DirectoryUsers.ToListAsync(ct);
        var departments=await db.Departments.ToListAsync(ct);
        var locations=await db.Locations.ToListAsync(ct);
        var departmentMap=departments.GroupBy(d=>DirectoryOrganization.Key(DirectoryOrganization.CanonicalDepartment(d.Name)))
            .ToDictionary(g=>g.Key,g=>g.Count()==1?g.Single():null);
        var locationMap=locations.GroupBy(l=>DirectoryOrganization.Key(l.Name)).ToDictionary(g=>g.Key,g=>g.Count()==1?g.Single():null);
        var added=0;var updated=0;var skipped=0;var archived=0;var conflicts=0;var noDept=0;var noLoc=0;var locationConflicts=0;var noMail=0;var disabled=0;var addedDepartments=new HashSet<string>();
        await using var transaction=preview?null:await db.Database.BeginTransactionAsync(ct);
        foreach(var row in export.Users)
        {
            if(row.CriticalSystem || Regex.IsMatch(row.Sid,@"-(500|501|502)$")
                ||Regex.IsMatch(row.SamAccountName,@"^(HealthMailbox|SystemMailbox|FederatedEmail|Migration\.|DiscoverySearchMailbox|MSOL_)",RegexOptions.IgnoreCase))
            {skipped++;continue;}
            var guid=Guid.Parse(row.ObjectGuid).ToString("D");var sam=DirectoryUser.Normalize(row.SamAccountName);
            var qualified=export.DomainNetBios.ToUpperInvariant()+"\\"+sam;
            var candidates=existing.Where(u=>u.AdObjectGuid==guid||u.NormalizedAccount==sam||u.NormalizedAccount==qualified).Distinct().ToArray();
            if(candidates.Length>1||(candidates.Length==1&&candidates[0].AdObjectGuid is not null&&candidates[0].AdObjectGuid!=guid))
            {conflicts++;continue;}
            var person=candidates.SingleOrDefault();
            if(person?.DeletedAtUtc is not null){archived++;continue;}
            var info=DirectoryOrganization.Resolve(row); if(info.Conflict)locationConflicts++;
            Department? department=null; Location? location=null;
            if(info.Department is not null && info.Department.Length<=80)
            {
                var key=DirectoryOrganization.Key(info.Department);
                if(!departmentMap.TryGetValue(key,out department))
                {
                    addedDepartments.Add(key);
                    department=new Department{Name=info.Department};departmentMap[key]=department;
                    if(!preview)db.Departments.Add(department);
                }
            }
            if(info.Location is not null)location=locationMap.GetValueOrDefault(DirectoryOrganization.Key(info.Location));
            if((person?.DepartmentId is null)&&department is null)noDept++;
            if((person?.LocationId is null)&&location is null)noLoc++;
            var mail=NotificationLabels.IsEmail(row.Email)?row.Email!.Trim():null;
            if(mail is null&&string.IsNullOrWhiteSpace(person?.Email))noMail++;
            if(!row.Enabled)disabled++;
            if(person is null)added++;else updated++;
            if(preview)continue;
            if(person is null)
            {
                person=new DirectoryUser{Account=row.SamAccountName.Trim(),NormalizedAccount=sam,Role=OrganizationRole.Employee};
                db.DirectoryUsers.Add(person);existing.Add(person);
            }
            person.DisplayName=(string.IsNullOrWhiteSpace(row.DisplayName)?row.Name:row.DisplayName).Trim();
            if(mail is not null)person.Email=mail;
            // Filled manual department/location and all notification roles/manager assignments are preserved.
            if(person.DepartmentId is null&&person.Department is null)person.Department=department;
            if(person.LocationId is null&&person.Location is null)person.Location=location;
            person.IsActive=row.Enabled;
            person.AdObjectGuid=guid;person.AdDomain=export.DomainNetBios.ToUpperInvariant();person.AdSamAccountName=sam;
            person.AdDistinguishedName=row.DistinguishedName;person.AdDepartment=row.Department;person.AdOffice=row.Office;
            person.AdImportedAtUtc=DateTime.UtcNow;
        }
        var report=new DirectoryImportReport(export.Users.Count,added,updated,skipped,conflicts,noDept,noLoc,locationConflicts,noMail,disabled,addedDepartments.Count,preview,archived);
        if(!preview)
        {
            db.AuditEntries.Add(new AuditEntry{OccurredAtUtc=DateTime.UtcNow,Actor="AD aktarımı / "+export.ExportedBy,
                Entity="DirectoryImport",EntityKey=export.DomainDns,Action="AD kullanıcıları içe aktarıldı; yönetici atamaları korunur",AfterJson=JsonSerializer.Serialize(report)});
            await db.SaveChangesAsync(ct); await transaction!.CommitAsync(ct);
        }
        return report;
    }
}
