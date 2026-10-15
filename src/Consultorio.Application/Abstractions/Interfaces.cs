using Consultorio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Clinica> Clinicas { get; }
    DbSet<AdministradorSistema> AdministradoresSistema { get; }
    DbSet<Usuario> Usuarios { get; }
    DbSet<Perfil> Perfis { get; }
    DbSet<Permissao> Permissoes { get; }
    DbSet<LogAuditoria> LogsAuditoria { get; }
    DbSet<Especialidade> Especialidades { get; }
    DbSet<Medico> Medicos { get; }
    DbSet<Convenio> Convenios { get; }
    DbSet<Plano> Planos { get; }
    DbSet<Paciente> Pacientes { get; }
    DbSet<PacientePlano> PacientePlanos { get; }
    DbSet<HorarioAtendimento> HorariosAtendimento { get; }
    DbSet<BloqueioAgenda> BloqueiosAgenda { get; }
    DbSet<Agendamento> Agendamentos { get; }
    DbSet<ResumoClinico> ResumosClinicos { get; }
    DbSet<Evolucao> Evolucoes { get; }
    DbSet<Prescricao> Prescricoes { get; }
    DbSet<Anexo> Anexos { get; }
    DbSet<Cobranca> Cobrancas { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Executa a ação em uma transação (com a estratégia de nova tentativa do SQL Server).</summary>
    Task<T> EmTransacaoAsync<T>(Func<Task<T>> acao, CancellationToken ct = default);

    /// <summary>Executa a ação em transação SERIALIZABLE com lock de aplicação (sp_getapplock) pelo recurso informado.</summary>
    Task<T> ExecutarComLockAsync<T>(string recurso, Func<Task<T>> acao, CancellationToken ct = default);
}

/// <summary>Usuário autenticado na requisição atual.</summary>
public interface IUsuarioAtual
{
    int? UsuarioId { get; }
    /// <summary>Clínica (cliente) do usuário logado; NULL na área /Admin ou sem login.</summary>
    int? ClinicaId { get; }
    /// <summary>Administrador do sistema logado na área /Admin (cookie separado); NULL nas telas das clínicas.</summary>
    int? AdministradorSistemaId { get; }
    int? MedicoId { get; }
    /// <summary>O usuário tem a transação (tela/ação) liberada por algum dos seus perfis? Ver <see cref="Domain.Common.Transacoes"/>.</summary>
    bool Pode(string transacao);
    string? EnderecoIp { get; }
}

public interface IAuditoria
{
    /// <summary>Registra em seguranca.LogAuditoria (LGPD). Não chama SaveChanges.
    /// <paramref name="usuarioId"/> e <paramref name="clinicaId"/> só são informados quando ainda não há usuário logado (ex.: no próprio login).</summary>
    void Registrar(string acao, string entidade, object? entidadeId, object? anterior = null, object? novo = null, int? usuarioId = null, int? clinicaId = null,
        int? administradorSistemaId = null);
}

public interface ISenhaHasher
{
    string Gerar(string senha);
    bool Verificar(string hash, string senha);
}

/// <summary>Pasta onde ficam os arquivos dos anexos do prontuário. Os caminhos trocados com a aplicação são relativos a ela.</summary>
public interface IArmazenamentoArquivos
{
    /// <summary>Cria um arquivo novo na pasta da clínica, deixa <paramref name="escrever"/> gravar o conteúdo e devolve o caminho relativo.</summary>
    Task<string> SalvarAsync(int clinicaId, string extensao, Func<Stream, Task> escrever, CancellationToken ct);
    Stream Abrir(string caminho);
    /// <summary>Só para desfazer uma gravação que não chegou ao banco; anexos registrados nunca são apagados.</summary>
    void Excluir(string caminho);
}
