using Consultorio.Application.Agenda;
using Consultorio.Application.Auth;
using Consultorio.Application.Cadastros;
using Consultorio.Application.Admin;
using Consultorio.Application.Common;
using Consultorio.Application.Pacientes;
using Consultorio.Application.Prontuario;
using Microsoft.Extensions.DependencyInjection;

namespace Consultorio.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<UsuarioService>();
        services.AddScoped<LimitesPlano>();
        // Área /Admin (administrador do sistema)
        services.AddScoped<AdminAuthService>();
        services.AddScoped<ClienteService>();
        services.AddScoped<CobrancaService>();
        services.AddScoped<AdminRelatorioService>();
        services.AddScoped<PerfilAcessoService>();
        services.AddScoped<PacienteService>();
        services.AddScoped<CadastroService>();
        services.AddScoped<AgendaService>();
        services.AddScoped<ProntuarioService>();
        services.AddScoped<AnexoService>();
        services.AddScoped<DocumentoService>();
        return services;
    }
}
