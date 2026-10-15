using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Consultorio.Application;
using Consultorio.Application.Abstractions;
using Consultorio.Application.Admin;
using Consultorio.Application.Auth;
using Consultorio.Domain.Common;
using Consultorio.Infrastructure;
using Consultorio.Web.Infra;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;
using Consultorio.Web;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUsuarioAtual, UsuarioAtual>();

// ---------- Idiomas (Resources/Textos*.resx) ----------
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
// Mensagens de validação e de leitura de formulário também traduzidas (a chave é o texto em português)
builder.Services.AddOptions<MvcOptions>().Configure<IStringLocalizerFactory>((o, f) =>
{
    var t = f.Create(typeof(Textos));
    o.ModelBindingMessageProvider.SetValueMustNotBeNullAccessor(_ => t["Campo obrigatório."]);
    o.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor((v, c) => t["Valor '{0}' inválido.", v]);
    o.ModelBindingMessageProvider.SetValueIsInvalidAccessor(v => t["Valor '{0}' inválido.", v]);
    o.ModelBindingMessageProvider.SetMissingBindRequiredValueAccessor(c => t["Informe {0}.", c]);
    o.ModelBindingMessageProvider.SetMissingKeyOrValueAccessor(() => t["Campo obrigatório."]);
    o.ModelBindingMessageProvider.SetUnknownValueIsInvalidAccessor(c => t["Valor inválido."]);
    o.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(c => t["Informe um número."]);
    o.ModelBindingMessageProvider.SetNonPropertyValueMustBeANumberAccessor(() => t["Informe um número."]);
});
// Atributos sem ErrorMessage ([Required], [MaxLength]...) recebem mensagem em português, traduzida como as demais
builder.Services.PostConfigure<MvcOptions>(o => o.ModelMetadataDetailsProviders.Add(new MensagensValidacao()));

// ---------- Autenticação por cookie ----------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "Consultorio.Auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.LoginPath = "/Conta/Login";
        o.LogoutPath = "/Conta/Sair";
        o.AccessDeniedPath = "/Conta/AcessoNegado";
        o.ExpireTimeSpan = TimeSpan.FromMinutes(config.GetValue("Sessao:MinutosInatividade", 60));
        o.SlidingExpiration = true;
        // Chamadas JavaScript (fetch) recebem 401/403 em vez de redirecionar para a tela de login
        o.Events.OnRedirectToLogin = ctx => RedirecionarOuStatus(ctx.HttpContext, ctx.RedirectUri, StatusCodes.Status401Unauthorized);
        o.Events.OnRedirectToAccessDenied = ctx => RedirecionarOuStatus(ctx.HttpContext, ctx.RedirectUri, StatusCodes.Status403Forbidden);
        // Usuário desativado ou clínica suspensa perde o acesso na próxima requisição, mesmo com cookie válido
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var p = ctx.Principal!;
            var situacao = int.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), out var usuarioId)
                           && int.TryParse(p.FindFirstValue(UsuarioAtual.ClaimClinicaId), out var clinicaId)
                ? await ctx.HttpContext.RequestServices.GetRequiredService<AuthService>()
                    .SessaoValidaAsync(usuarioId, clinicaId, ctx.HttpContext.RequestAborted)
                : null;
            // Data limite do período de acesso, para o aviso no topo das páginas (_Layout)
            if (situacao?.AcessoAte is DateOnly acessoAte) ctx.HttpContext.Items[UsuarioAtual.ItemAcessoAte] = acessoAte;
            if (situacao?.Cobranca is { } cobranca) ctx.HttpContext.Items[UsuarioAtual.ItemCobranca] = cobranca;
            if (situacao is not { Valida: true })
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }
            // Transações dos perfis lidas a cada requisição: o que o administrador do sistema muda vale na próxima página
            var identidade = (ClaimsIdentity)p.Identity!;
            foreach (var c in identidade.FindAll(Transacoes.Claim).ToList()) identidade.RemoveClaim(c);
            identidade.AddClaims(situacao.Transacoes.Select(t => new Claim(Transacoes.Claim, t)));
        };
    })
    // Área /Admin: cookie e tela de login próprios
    .AddCookie(AdminAuth.Esquema, o =>
    {
        o.Cookie.Name = "Consultorio.Admin";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.LoginPath = "/Admin/Conta/Login";
        o.LogoutPath = "/Admin/Conta/Sair";
        o.AccessDeniedPath = "/Admin/Conta/Login";
        o.ExpireTimeSpan = TimeSpan.FromMinutes(config.GetValue("Sessao:MinutosInatividadeAdmin", 30));
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx => RedirecionarOuStatus(ctx.HttpContext, ctx.RedirectUri, StatusCodes.Status401Unauthorized);
        o.Events.OnRedirectToAccessDenied = ctx => RedirecionarOuStatus(ctx.HttpContext, ctx.RedirectUri, StatusCodes.Status403Forbidden);
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var valido = int.TryParse(ctx.Principal!.FindFirstValue(UsuarioAtual.ClaimAdminId), out var adminId)
                && await ctx.HttpContext.RequestServices.GetRequiredService<AdminAuthService>()
                    .SessaoValidaAsync(adminId, ctx.HttpContext.RequestAborted);
            if (!valido)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(AdminAuth.Esquema);
            }
        };
    });
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(AdminAuth.Politica, p => p
        .AddAuthenticationSchemes(AdminAuth.Esquema)
        .RequireAuthenticatedUser()
        .RequireClaim(UsuarioAtual.ClaimAdminId));
    // Uma política por transação: [Authorize(Policy = Transacoes.AgendaVer)]
    foreach (var t in Transacoes.Todas)
        o.AddPolicy(t.Codigo, p => p.RequireAuthenticatedUser().RequireClaim(Transacoes.Claim, t.Codigo));
});

builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");

// Não converte acentos em entidades HTML (&#xE7;) nas páginas geradas
builder.Services.AddSingleton(HtmlEncoder.Create(UnicodeRanges.All));

// Rotina de pagamentos: gera as mensalidades do mês e bloqueia/reativa clientes por atraso (de hora em hora)
if (config.GetValue("Cobrancas:RotinaAutomatica", true)) builder.Services.AddHostedService<RotinaCobrancas>();

// Tentativas de login por minuto por IP (clínicas e área admin). Configurável em "Seguranca:LoginsPorMinuto".
var loginsPorMinuto = builder.Configuration.GetValue("Seguranca:LoginsPorMinuto", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = loginsPorMinuto, Window = TimeSpan.FromMinutes(1) }));
});

// ---------- MVC ----------
builder.Services.AddControllersWithViews(o =>
    {
        o.Conventions.Add(new LoginDaClinicaConvention());           // telas das clínicas exigem login, exceto [AllowAnonymous]
        o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()); // proteção CSRF em todos os POST
        o.Filters.Add<TratamentoErrosFilter>();
        // Formulários usam ponto como separador decimal (input type=number) e datas ISO, independentemente do idioma
        o.ValueProviderFactories.RemoveType<FormValueProviderFactory>();
        o.ValueProviderFactories.Insert(0, new FormularioInvarianteValueProviderFactory());
    })
    .AddDataAnnotationsLocalization(o => o.DataAnnotationLocalizerProvider = (_, f) => f.Create(typeof(Textos)))
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Erro");
    app.UseHsts();
}

// Idioma: português por padrão; o escolhido pelo usuário fica no cookie de idioma (gravado também no cadastro dele
// e reaplicado no login). Datas e números seguem o idioma; a moeda continua a do sistema (Regional:SimboloMoeda).
var localizacao = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(Idiomas.Padrao),
    SupportedCultures = Idiomas.Culturas,
    SupportedUICultures = Idiomas.Culturas
};
localizacao.RequestCultureProviders = [new QueryStringRequestCultureProvider(), new CookieRequestCultureProvider()];
app.UseRequestLocalization(localizacao);
Exibicao.Textos = app.Services.GetRequiredService<IStringLocalizerFactory>().Create(typeof(Textos));
Exibicao.SimboloMoeda = config["Regional:SimboloMoeda"] ?? "R$";

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute("areas", "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapGet("/saude", () => Results.Ok(new { status = "ok", horario = DateTime.Now })).AllowAnonymous();

await BootstrapAdmin.SincronizarTransacoesAsync(app.Services, app.Logger);
await BootstrapAdmin.ExecutarAsync(app.Services, config, app.Logger);

app.Run();

static Task RedirecionarOuStatus(HttpContext ctx, string redirectUri, int status)
{
    if (Requisicao.EhAjax(ctx.Request))
        ctx.Response.StatusCode = status;
    else
        ctx.Response.Redirect(redirectUri);
    return Task.CompletedTask;
}

// Permite testes de integração (WebApplicationFactory)
public partial class Program;
