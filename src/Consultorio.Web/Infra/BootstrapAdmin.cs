using Consultorio.Application.Abstractions;
using Consultorio.Application.Admin;
using Consultorio.Domain.Entities;
using Consultorio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Web.Infra;

/// <summary>
/// Cria o primeiro Administrador do sistema (área /Admin) quando a tabela seguranca.AdministradorSistema
/// está vazia e as chaves Bootstrap:AdminEmail / Bootstrap:AdminSenha estão configuradas.
/// Ele entra em /Admin, cadastra os clientes (clínicas) e o administrador de cada um.
/// Depois do primeiro acesso, troque a senha e remova essas chaves da configuração.
/// </summary>
public static class BootstrapAdmin
{
    /// <summary>Cadastra no banco as transações novas do catálogo e, em banco vazio, os perfis padrão.</summary>
    public static async Task SincronizarTransacoesAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        try
        {
            await PerfilAcessoService.SincronizarAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Não foi possível sincronizar as transações e perfis de acesso (verifique a connection string e se o banco foi criado).");
        }
    }

    public static async Task ExecutarAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        var email = config["Bootstrap:AdminEmail"];
        var senha = config["Bootstrap:AdminSenha"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(senha)) return;

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            if (await db.AdministradoresSistema.AnyAsync()) return;
            var hasher = scope.ServiceProvider.GetRequiredService<ISenhaHasher>();
            var admin = new AdministradorSistema { Nome = "Administrador do sistema", Email = email.Trim().ToLowerInvariant(), SenhaHash = hasher.Gerar(senha) };
            db.AdministradoresSistema.Add(admin);
            await db.SaveChangesAsync();
            logger.LogInformation("Administrador do sistema criado: {Email}. Entre em /Admin e cadastre os clientes.", admin.Email);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Não foi possível criar o administrador do sistema (verifique a connection string e se o banco foi criado).");
        }
    }
}
