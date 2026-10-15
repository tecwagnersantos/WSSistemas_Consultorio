using System.Text.Json;
using Consultorio.Application.Abstractions;
using Consultorio.Domain.Entities;
using Consultorio.Infrastructure.Persistence;

namespace Consultorio.Infrastructure.Security;

public class Auditoria(AppDbContext db, IUsuarioAtual usuario) : IAuditoria
{
    public void Registrar(string acao, string entidade, object? entidadeId, object? anterior = null, object? novo = null, int? usuarioId = null, int? clinicaId = null,
        int? administradorSistemaId = null)
    {
        db.LogsAuditoria.Add(new LogAuditoria
        {
            ClinicaId = clinicaId ?? usuario.ClinicaId,
            UsuarioId = usuarioId ?? usuario.UsuarioId,
            AdministradorSistemaId = administradorSistemaId ?? usuario.AdministradorSistemaId,
            Acao = acao,
            Entidade = entidade,
            EntidadeId = entidadeId?.ToString(),
            DadosAnteriores = Serializar(anterior),
            DadosNovos = Serializar(novo),
            EnderecoIp = usuario.EnderecoIp,
            OcorridoEm = DateTime.Now
        });
    }

    private static string? Serializar(object? o) => o switch
    {
        null => null,
        string s => s,
        _ => JsonSerializer.Serialize(o)
    };
}
