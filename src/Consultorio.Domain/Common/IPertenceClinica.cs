namespace Consultorio.Domain.Common;

/// <summary>
/// Registro que pertence a uma clínica (cliente). O AppDbContext filtra automaticamente as consultas
/// pela clínica do usuário logado e preenche ClinicaId nas inclusões.
/// </summary>
public interface IPertenceClinica
{
    int ClinicaId { get; set; }
}
