using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PrintMerkez.Core;
using PrintMerkez.Web.Data;
namespace PrintMerkez.Web.Pages.Notifications;
public class PoliciesModel(PrintDbContext db):PageModel
{
    [BindProperty]public PolicyInput Input{get;set;}=new();
    public int? EditingId{get;private set;}
    public List<UsageAlertPolicy> Policies{get;private set;}=[];
    private async Task LoadAsync()=>Policies=await db.UsageAlertPolicies.AsNoTracking().OrderBy(p=>p.Name).ToListAsync();
    public async Task<IActionResult> OnGetAsync(int? id)
    {
        EditingId=id;await LoadAsync();if(id.HasValue){var p=await db.UsageAlertPolicies.FindAsync(id.Value);if(p is null)return NotFound();Input=new(){Name=p.Name,Threshold=p.Threshold,Source=p.Source,IsActive=p.IsActive};}return Page();
    }
    public async Task<IActionResult> OnPostAsync(int? id)
    {
        EditingId=id;await LoadAsync();Input.Name=Input.Name?.Trim()??"";
        if(await db.UsageAlertPolicies.AnyAsync(p=>p.Name==Input.Name&&(!id.HasValue||p.Id!=id.Value)))ModelState.AddModelError("Input.Name","Bu kural adı kullanılıyor.");
        var policy=id.HasValue?await db.UsageAlertPolicies.FindAsync(id.Value):new UsageAlertPolicy();if(policy is null)return NotFound();
        if(id.HasValue&&policy.Source!=Input.Source&&await db.UsageNotifications.AnyAsync(n=>n.PolicyId==id))ModelState.AddModelError("Input.Source","Bildirim oluşmuş kuralın kaynağı değiştirilemez. Yeni kural ekleyin.");
        if(!ModelState.IsValid)return Page();
        policy.Name=Input.Name;policy.Threshold=Input.Threshold;policy.Source=Input.Source;policy.IsActive=Input.IsActive;policy.Period=UsagePeriod.Monthly;
        if(!id.HasValue)db.UsageAlertPolicies.Add(policy);
        try{await db.SaveChangesAsync();}catch(DbUpdateException){ModelState.AddModelError("","Kural kaydedilemedi; aynı adla başka işlem yapılmış olabilir.");return Page();}
        TempData["Success"]="Aylık uyarı kuralı kaydedildi. Mevcut ayı taramak için bildirim ekranındaki Değerlendir düğmesini kullanabilirsiniz.";return RedirectToPage("Policies");
    }
    public class PolicyInput
    {
        [Required(ErrorMessage="Kural adı gerekli."),StringLength(100)]public string Name{get;set;}="";
        [Range(1,1_000_000_000,ErrorMessage="Eşik 1–1.000.000.000 arasında olmalı.")]public int Threshold{get;set;}=500;
        [EnumDataType(typeof(EventSource))]public EventSource Source{get;set;}=EventSource.WindowsPrintService;
        public bool IsActive{get;set;}=true;
    }
}
