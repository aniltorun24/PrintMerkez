using System.ComponentModel.DataAnnotations;
using PrintMerkez.Core;
namespace PrintMerkez.Web.Pages.Users;
public sealed class UserEditInput
{
    [Required(ErrorMessage="Hesap gerekli."),StringLength(160)] public string Account { get; set; }="";
    [Required(ErrorMessage="Ad soyad gerekli."),StringLength(120)] public string DisplayName { get; set; }="";
    public int? DepartmentId { get; set; }
    public int? LocationId { get; set; }
    [StringLength(254),EmailAddress(ErrorMessage="Geçerli bir e-posta adresi girin.")] public string? Email { get; set; }
    [EnumDataType(typeof(OrganizationRole))] public OrganizationRole Role { get; set; }
    public bool IsActive { get; set; }=true;
}
