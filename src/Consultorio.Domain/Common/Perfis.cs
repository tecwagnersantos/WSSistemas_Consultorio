namespace Consultorio.Domain.Common;

/// <summary>
/// Perfis padrão criados com o banco (seguranca.Perfil, sem ClinicaId = valem para todos os clientes).
/// O acesso não depende do nome do perfil e sim das transações dele (<see cref="Transacoes"/>), configuradas em /Admin → Perfis de acesso.
/// O administrador do sistema não é um perfil: tem tabela, login e cookie próprios (área /Admin).
/// </summary>
public static class Perfis
{
    public const string Administrador = "Administrador";
    public const string Medico = "Medico";
    public const string Recepcao = "Recepcao";

    public static readonly string[] Padrao = [Administrador, Medico, Recepcao];
}
