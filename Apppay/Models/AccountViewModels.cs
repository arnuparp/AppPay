using System.ComponentModel.DataAnnotations;

namespace Apppay.Models
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "กรุณาระบุชื่อที่ใช้แสดง")]
        [StringLength(100)]
        [Display(Name = "ชื่อที่ใช้แสดง")]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessage = "กรุณาระบุอีเมล")]
        [EmailAddress(ErrorMessage = "รูปแบบอีเมลไม่ถูกต้อง")]
        [Display(Name = "อีเมล")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "กรุณาระบุรหัสผ่าน")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "รหัสผ่านต้องมีอย่างน้อย {2} ตัวอักษร")]
        [DataType(DataType.Password)]
        [Display(Name = "รหัสผ่าน")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "ยืนยันรหัสผ่าน")]
        [Compare("Password", ErrorMessage = "รหัสผ่านไม่ตรงกัน")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class LoginViewModel
    {
        [Required(ErrorMessage = "กรุณาระบุอีเมล")]
        [EmailAddress(ErrorMessage = "รูปแบบอีเมลไม่ถูกต้อง")]
        [Display(Name = "อีเมล")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "กรุณาระบุรหัสผ่าน")]
        [DataType(DataType.Password)]
        [Display(Name = "รหัสผ่าน")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "จำฉันไว้")]
        public bool RememberMe { get; set; }

        public string? ReturnUrl { get; set; }
    }
}
