using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MyStore.Data;
using MyStore.Helpers;
using MyStore.Models;

namespace MyStore.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ILogger<LoginModel> _logger;
        private readonly ApplicationDbContext _context;

        public LoginModel(SignInManager<IdentityUser> signInManager,
            ILogger<LoginModel> logger,
            UserManager<IdentityUser> userManager,
            ApplicationDbContext context)
        {
            _signInManager = signInManager;
            _logger = logger;
            _userManager = userManager;
            _context = context;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        public string ReturnUrl { get; set; }

        [TempData]
        public string ErrorMessage { get; set; }

        // گۆڕاوەکان بۆ هەڵگرتنی زانیاری دوکان
        public string StoreName { get; set; } = "MyStore";
        public string StoreLogo { get; set; } = "";

        public class InputModel
        {
            [Required(ErrorMessage = "تکایە ئیمەیڵ بنووسە")]
            [EmailAddress(ErrorMessage = "شێوازی ئیمەیڵ هەڵەیە")]
            public string Email { get; set; }

            [Required(ErrorMessage = "تکایە پاسۆرد بنووسە")]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            [Display(Name = "لەبیرت بمێنم؟")]
            public bool RememberMe { get; set; }
        }

        // فەنکشنێک بۆ دەرهێنانی زانیاری دوکان لە داتابەیس
        private async Task LoadStoreInfoAsync()
        {
            try
            {
                var settings = await _context.SystemSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
                if (settings.ContainsKey(SettingsKeys.StoreName))
                {
                    StoreName = settings[SettingsKeys.StoreName];
                }
                if (settings.ContainsKey(SettingsKeys.StoreLogo))
                {
                    StoreLogo = settings[SettingsKeys.StoreLogo];
                }
            }
            catch
            {
                StoreName = "سیستەمی فرۆشتن";
            }
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            await LoadStoreInfoAsync(); // زانیارییەکان لۆد دەکەین پێش ئەوەی پەڕەکە پیشان بدەین

            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl ??= Url.Content("~/");

            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                // ١. سەرەتا هەوڵ دەدەین بەکارهێنەرەکە بەپێی ئیمەیڵ بدۆزینەوە
                var user = await _userManager.FindByEmailAsync(Input.Email);

                // ٢. ئەگەر بە ئیمەیڵ دۆزرایەوە، ئەوا ناوی بەکارهێنەرەکەی (UserName) وەردەگرین
                // ئەگەر نەدۆزرایەوە، وادادەنێین ئەوەی نووسراوە خۆی UserNameـە
                var userNameToSignIn = user != null ? user.UserName : Input.Email;

                // ٣. ئێستا هەوڵ دەدەین بچینە ژوورەوە
                var result = await _signInManager.PasswordSignInAsync(userNameToSignIn, Input.Password, Input.RememberMe, lockoutOnFailure: false);

                if (result.Succeeded)
                {
                    _logger.LogInformation("User logged in.");
                    await AuditHelper.UserLoggedIn(_context, User, HttpContext, userNameToSignIn ?? Input.Email);

                    // --- [بەشی نوێ: ئاڕاستەکردن بەپێی ڕۆڵی بەکارهێنەر] ---
                    
                    // پێویستە سەرەتا دڵنیا بینەوە کە یوزەرەکە دۆزراوەتەوە
                    if (user == null)
                    {
                         // ئەگەر بە ئیمەیڵ نەدۆزرابوویەوە (واتە یوزەرنەیمی نووسیبوو)، دەبێت بە یوزەرنەیم بیدۆزینەوە
                         user = await _userManager.FindByNameAsync(userNameToSignIn);
                    }

                    if (user != null)
                    {
                        var roles = await _userManager.GetRolesAsync(user);
                        
                        // ئەگەر یوزەرەکە تەنها ڕۆڵی Cashier ی هەبوو، یان ڕۆڵی سەرەکی Cashier بوو 
                        // ئەگەر returnUrl دیاری نەکرابوو یان یەکسان بوو بە ناوەڕۆکی سەرەکی "~/"
                        // ئەوا ڕاستەوخۆ دەینێرین بۆ پەڕەی POS
                        if (roles.Contains("Cashier") && !roles.Contains("Admin") && !roles.Contains("Manager"))
                        {
                            // ئەگەر لە شوێنێکی ترەوە نەهاتووە، یان هەوڵی نەداوە بچێتە شوێنێکی تر
                            if (returnUrl == Url.Content("~/") || string.IsNullOrEmpty(returnUrl))
                            {
                                return LocalRedirect("~/sales/create"); // ئاڕاستەی پەڕەی پۆسی دەکەین
                            }
                        }
                    }
                    // ---------------------------------------------------

                    return LocalRedirect(returnUrl);
                }
                if (result.RequiresTwoFactor)
                {
                    return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
                }
                if (result.IsLockedOut)
                {
                    _logger.LogWarning("User account locked out.");
                    return RedirectToPage("./Lockout");
                }
                else
                {
                    ModelState.AddModelError(string.Empty, "ئیمەیڵ یان پاسۆرد هەڵەیە.");
                }
            }

            // ئەگەر کێشەیەک هەبوو لە فۆڕمەکە، دووبارە زانیاری دوکان لۆد دەکەینەوە بۆ دیزاینەکە
            await LoadStoreInfoAsync();
            return Page();
        }
    }
}