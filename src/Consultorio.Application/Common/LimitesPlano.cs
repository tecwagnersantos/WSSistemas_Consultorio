using Consultorio.Application.Abstractions;
using Consultorio.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Common;

/// <summary>Uso do plano da clínica: ativos x limite contratado (NULL = sem limite).</summary>
public record UsoPlanoDto(int UsuariosAtivos, short? MaxUsuarios, int MedicosAtivos, short? MaxMedicos);

/// <summary>
/// Limites de médicos e usuários ativos por clínica, configurados pelo administrador do sistema (área /Admin).
/// Contam só os registros ativos; inativar libera a vaga.
/// </summary>
public class LimitesPlano(IAppDbContext db)
{
    public async Task<UsoPlanoDto> UsoAsync(int clinicaId, CancellationToken ct)
    {
        var c = await db.Clinicas.AsNoTracking().Where(x => x.Id == clinicaId)
            .Select(x => new { x.MaxUsuarios, x.MaxMedicos }).FirstAsync(ct);
        var usuarios = await db.Usuarios.IgnoreQueryFilters().CountAsync(u => u.ClinicaId == clinicaId && u.Ativo, ct);
        var medicos = await db.Medicos.IgnoreQueryFilters().CountAsync(m => m.ClinicaId == clinicaId && m.Ativo, ct);
        return new UsoPlanoDto(usuarios, c.MaxUsuarios, medicos, c.MaxMedicos);
    }

    public async Task GarantirVagaUsuarioAsync(int clinicaId, CancellationToken ct)
    {
        var uso = await UsoAsync(clinicaId, ct);
        if (uso.MaxUsuarios is short max && uso.UsuariosAtivos >= max)
            throw new DomainException("O plano desta clínica permite {0} usuário(s) ativo(s) e o limite foi atingido. " +
                                      "Inative um usuário ou peça ao suporte para aumentar o limite.", max);
    }

    public async Task GarantirVagaMedicoAsync(int clinicaId, CancellationToken ct)
    {
        var uso = await UsoAsync(clinicaId, ct);
        if (uso.MaxMedicos is short max && uso.MedicosAtivos >= max)
            throw new DomainException("O plano desta clínica permite {0} médico(s) ativo(s) e o limite foi atingido. " +
                                      "Inative um médico ou peça ao suporte para aumentar o limite.", max);
    }
}
