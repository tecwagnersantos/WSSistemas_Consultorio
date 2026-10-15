using Consultorio.Domain.Common;

namespace Consultorio.Domain.Entities;

/// <summary>Cliente do sistema (consultório/clínica). Todos os dados de cadastro, agenda e prontuário pertencem a uma clínica.</summary>
public class Clinica
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string? Cnpj { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Endereco { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }
    /// <summary>Limites contratados (NULL = sem limite). Contam só os ativos.</summary>
    public short? MaxMedicos { get; set; }
    public short? MaxUsuarios { get; set; }
    public string? Observacoes { get; set; }
    /// <summary>false = período de teste/implantação, com acesso até <see cref="AcessoAte"/>; true = versão definitiva liberada.</summary>
    public bool AcessoDefinitivo { get; set; }
    /// <summary>Último dia de acesso enquanto não for definitivo. Depois dele o login da clínica é recusado.</summary>
    public DateOnly? AcessoAte { get; set; }
    /// <summary>Valor da mensalidade (NULL = não gera mensalidades automaticamente).</summary>
    public decimal? ValorMensalidade { get; set; }
    /// <summary>Dia do mês em que vence a mensalidade (1 a 28).</summary>
    public byte DiaVencimento { get; set; } = 10;
    /// <summary>Dias depois do vencimento até o bloqueio automático por falta de pagamento.</summary>
    public short DiasTolerancia { get; set; } = 5;
    /// <summary>Bloqueia o cliente sozinho quando uma cobrança passa da tolerância sem pagamento.</summary>
    public bool BloqueioPorAtraso { get; set; } = true;
    /// <summary>true = o bloqueio atual foi feito pelo sistema por atraso; o pagamento em dia reativa sozinho.</summary>
    public bool BloqueadoPorAtraso { get; set; }
    /// <summary>Situação do cliente (<see cref="SituacoesCliente"/>). Só ATIVO entra no sistema.</summary>
    public string Situacao { get; set; } = SituacoesCliente.Ativo;
    /// <summary>Motivo informado pelo suporte ao bloquear ou inativar (ex.: inadimplência, contrato encerrado).</summary>
    public string? MotivoSituacao { get; set; }
    public DateTime SituacaoDesde { get; set; } = DateTime.Now;
    public DateTime CriadoEm { get; set; } = DateTime.Now;
    public DateTime? AtualizadoEm { get; set; }

    public bool Ativo => Situacao == SituacoesCliente.Ativo;
    public bool AcessoVencido(DateOnly hoje) => !AcessoDefinitivo && !(AcessoAte >= hoje);
}

/// <summary>
/// Situação comercial do cliente. ATIVO: usa o sistema normalmente. BLOQUEADO: suspensão temporária
/// (ex.: inadimplência), volta a ATIVO quando resolvido. INATIVO: deixou de ser cliente (contrato encerrado);
/// os dados ficam guardados, mas o cliente sai das listas e indicadores do dia a dia.
/// Bloqueado e inativo não entram: o login é recusado e as sessões abertas caem na requisição seguinte.
/// </summary>
public static class SituacoesCliente
{
    public const string Ativo = "ATIVO";
    public const string Bloqueado = "BLOQUEADO";
    public const string Inativo = "INATIVO";
    public static readonly IReadOnlyList<string> Todas = [Ativo, Bloqueado, Inativo];

    public static string Nome(string situacao) => situacao switch
    {
        Ativo => "Ativo",
        Bloqueado => "Bloqueado",
        Inativo => "Inativo",
        _ => situacao
    };
}

/// <summary>Usuário de uma clínica (Administrador, Médico, Recepção). Os administradores do sistema ficam em <see cref="AdministradorSistema"/>.</summary>
public class Usuario : IPertenceClinica
{
    public int Id { get; set; }
    public int ClinicaId { get; set; }
    public string Nome { get; set; } = "";
    public string Email { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public bool Ativo { get; set; } = true;
    public byte TentativasFalhas { get; set; }
    public DateTime? BloqueadoAte { get; set; }
    /// <summary>Idioma das telas (pt-BR, en-US, es-ES, fr-FR); NULL = português.</summary>
    public string? Idioma { get; set; }
    public DateTime? UltimoAcessoEm { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.Now;
    public DateTime? AtualizadoEm { get; set; }

    public Clinica Clinica { get; set; } = null!;
    public ICollection<UsuarioPerfil> Perfis { get; set; } = [];
}

/// <summary>
/// Perfil de acesso: conjunto de transações (telas/ações). Sem ClinicaId vale para todos os clientes (perfil padrão);
/// com ClinicaId é exclusivo daquele cliente. Mantido pelo administrador do sistema em /Admin → Perfis de acesso.
/// </summary>
public class Perfil
{
    public int Id { get; set; }
    public int? ClinicaId { get; set; }
    public string Nome { get; set; } = "";
    public string? Descricao { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.Now;
    public DateTime? AtualizadoEm { get; set; }
    public ICollection<PerfilPermissao> Permissoes { get; set; } = [];
}

/// <summary>Transação do sistema (código do catálogo <see cref="Common.Transacoes"/>).</summary>
public class Permissao
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string? Descricao { get; set; }
}

public class PerfilPermissao
{
    public int PerfilId { get; set; }
    public int PermissaoId { get; set; }
    public Permissao Permissao { get; set; } = null!;
}

public class UsuarioPerfil
{
    public int UsuarioId { get; set; }
    public int PerfilId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public Perfil Perfil { get; set; } = null!;
}

public class LogAuditoria
{
    public long Id { get; set; }
    public int? ClinicaId { get; set; }
    public int? UsuarioId { get; set; }
    public int? AdministradorSistemaId { get; set; }
    public string Acao { get; set; } = "";
    public string Entidade { get; set; } = "";
    public string? EntidadeId { get; set; }
    public string? DadosAnteriores { get; set; }
    public string? DadosNovos { get; set; }
    public string? EnderecoIp { get; set; }
    public DateTime OcorridoEm { get; set; } = DateTime.Now;
}

/// <summary>
/// Administrador do sistema (área /Admin): mantém os clientes, limites e administradores.
/// Login e cookie separados dos usuários das clínicas; não acessa agenda nem prontuário.
/// </summary>
public class AdministradorSistema
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Email { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public bool Ativo { get; set; } = true;
    public byte TentativasFalhas { get; set; }
    public DateTime? BloqueadoAte { get; set; }
    public DateTime? UltimoAcessoEm { get; set; }
    /// <summary>Idioma das telas (pt-BR, en-US, es-ES, fr-FR); NULL = português.</summary>
    public string? Idioma { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.Now;
    public DateTime? AtualizadoEm { get; set; }
}
