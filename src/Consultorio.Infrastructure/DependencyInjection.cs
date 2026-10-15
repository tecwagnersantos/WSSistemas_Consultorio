using Consultorio.Application.Abstractions;
using Consultorio.Application.Prontuario;
using Consultorio.Application.Relatorios;
using Consultorio.Infrastructure.Arquivos;
using Consultorio.Infrastructure.Persistence;
using Consultorio.Infrastructure.Relatorios;
using Consultorio.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Consultorio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(
            config.GetConnectionString("Consultorio"),
            sql => sql.EnableRetryOnFailure(3)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddSingleton<ISenhaHasher, SenhaHasher>();
        services.AddScoped<IAuditoria, Auditoria>();
        services.AddScoped<IRelatorioQueries, RelatorioQueries>();

        // Anexos do prontuário: pasta no servidor (relativa = dentro da pasta do site; prefira um caminho absoluto fora dela)
        var pasta = config["Anexos:Pasta"] is { Length: > 0 } p ? p : "App_Data/Anexos";
        if (!Path.IsPathRooted(pasta)) pasta = Path.Combine(config[HostDefaults.ContentRootKey] ?? AppContext.BaseDirectory, pasta);
        services.AddSingleton<IArmazenamentoArquivos>(new ArmazenamentoLocal(pasta));
        services.AddSingleton(new OpcoesAnexos(config.GetValue("Anexos:TamanhoMaximoMB", 20)));
        return services;
    }
}
