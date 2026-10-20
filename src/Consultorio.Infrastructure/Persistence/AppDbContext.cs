using System.Data;
using System.Reflection;
using Consultorio.Application.Abstractions;
using Consultorio.Domain.Common;
using Consultorio.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Infrastructure.Persistence;

/// <summary>
/// Mapeia as tabelas criadas por database/01_schema.sql (schemas seguranca, cadastro, agenda, prontuario).
/// O banco é criado pelos scripts SQL; o EF Core não gera o esquema.
/// Banco único para todos os clientes: toda entidade <see cref="IPertenceClinica"/> é filtrada pela clínica
/// do usuário logado (filtro global) e recebe o ClinicaId dele ao ser incluída.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, IUsuarioAtual usuarioAtual) : DbContext(options), IAppDbContext
{
    /// <summary>Clínica usada nos filtros globais. Avaliada a cada consulta (o EF parametriza por instância do contexto).</summary>
    public int? ClinicaAtualId => usuarioAtual.ClinicaId;

    public DbSet<Clinica> Clinicas => Set<Clinica>();
    public DbSet<AdministradorSistema> AdministradoresSistema => Set<AdministradorSistema>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Perfil> Perfis => Set<Perfil>();
    public DbSet<Permissao> Permissoes => Set<Permissao>();
    public DbSet<LogAuditoria> LogsAuditoria => Set<LogAuditoria>();
    public DbSet<Especialidade> Especialidades => Set<Especialidade>();
    public DbSet<Medico> Medicos => Set<Medico>();
    public DbSet<Convenio> Convenios => Set<Convenio>();
    public DbSet<Plano> Planos => Set<Plano>();
    public DbSet<Paciente> Pacientes => Set<Paciente>();
    public DbSet<PacientePlano> PacientePlanos => Set<PacientePlano>();
    public DbSet<HorarioAtendimento> HorariosAtendimento => Set<HorarioAtendimento>();
    public DbSet<BloqueioAgenda> BloqueiosAgenda => Set<BloqueioAgenda>();
    public DbSet<Agendamento> Agendamentos => Set<Agendamento>();
    public DbSet<ResumoClinico> ResumosClinicos => Set<ResumoClinico>();
    public DbSet<Evolucao> Evolucoes => Set<Evolucao>();
    public DbSet<Prescricao> Prescricoes => Set<Prescricao>();
    public DbSet<Anexo> Anexos => Set<Anexo>();
    public DbSet<DocumentoClinico> Documentos => Set<DocumentoClinico>();
    public DbSet<ModeloDocumento> ModelosDocumento => Set<ModeloDocumento>();
    public DbSet<Cobranca> Cobrancas => Set<Cobranca>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ---------- seguranca ----------
        b.Entity<Clinica>(e =>
        {
            e.ToTable("Clinica", "seguranca");
            e.Property(x => x.Cnpj).IsFixedLength().IsUnicode(false);
            e.Property(x => x.Uf).IsFixedLength().IsUnicode(false);
            e.Property(x => x.Telefone).IsUnicode(false);
            e.Property(x => x.Email).IsUnicode(false);
            e.Property(x => x.Situacao).HasMaxLength(10).IsUnicode(false);
            e.Property(x => x.MotivoSituacao).HasMaxLength(300);
            e.Ignore(x => x.Ativo);
            e.Property(x => x.ValorMensalidade).HasPrecision(10, 2);
        });
        b.Entity<Cobranca>(e =>
        {
            e.ToTable("Cobranca", "financeiro");
            e.HasOne<Clinica>().WithMany().HasForeignKey(x => x.ClinicaId);
            e.Property(x => x.Descricao).HasMaxLength(100);
            e.Property(x => x.Valor).HasPrecision(10, 2);
            e.Property(x => x.ValorPago).HasPrecision(10, 2);
            e.Property(x => x.FormaPagamento).IsUnicode(false).HasMaxLength(15);
            e.Property(x => x.Observacoes).HasMaxLength(300);
            e.Ignore(x => x.Paga);
        });
        b.Entity<Usuario>(e =>
        {
            e.ToTable("Usuario", "seguranca");
            // Cada clínica só enxerga os próprios usuários (filtro global abaixo); o login usa IgnoreQueryFilters()
            e.HasOne(x => x.Clinica).WithMany().HasForeignKey(x => x.ClinicaId);
            e.Property(x => x.Email).HasMaxLength(254).IsUnicode(false);
            e.Property(x => x.SenhaHash).HasMaxLength(500).IsUnicode(false);
            e.Property(x => x.Idioma).HasMaxLength(5).IsUnicode(false);
            e.HasMany(x => x.Perfis).WithOne(x => x.Usuario).HasForeignKey(x => x.UsuarioId);
        });
        b.Entity<AdministradorSistema>(e =>
        {
            e.ToTable("AdministradorSistema", "seguranca");
            e.Property(x => x.Email).HasMaxLength(254).IsUnicode(false);
            e.Property(x => x.SenhaHash).HasMaxLength(500).IsUnicode(false);
            e.Property(x => x.Idioma).HasMaxLength(5).IsUnicode(false);
        });
        b.Entity<Perfil>(e =>
        {
            e.ToTable("Perfil", "seguranca");
            e.HasMany(x => x.Permissoes).WithOne().HasForeignKey(x => x.PerfilId);
        });
        b.Entity<Permissao>(e =>
        {
            e.ToTable("Permissao", "seguranca");
            e.Property(x => x.Codigo).IsUnicode(false).HasMaxLength(80);
        });
        b.Entity<PerfilPermissao>(e =>
        {
            e.ToTable("PerfilPermissao", "seguranca");
            e.HasKey(x => new { x.PerfilId, x.PermissaoId });
        });
        b.Entity<UsuarioPerfil>(e =>
        {
            e.ToTable("UsuarioPerfil", "seguranca");
            e.HasKey(x => new { x.UsuarioId, x.PerfilId });
            e.HasQueryFilter(x => x.Usuario.ClinicaId == ClinicaAtualId);
        });
        b.Entity<LogAuditoria>(e =>
        {
            e.ToTable("LogAuditoria", "seguranca");
            e.Property(x => x.Acao).IsUnicode(false);
            e.Property(x => x.Entidade).IsUnicode(false);
            e.Property(x => x.EntidadeId).IsUnicode(false);
            e.Property(x => x.EnderecoIp).IsUnicode(false);
        });

        // ---------- cadastro ----------
        b.Entity<Especialidade>().ToTable("Especialidade", "cadastro");
        b.Entity<Medico>(e =>
        {
            e.ToTable("Medico", "cadastro");
            e.Property(x => x.Crm).IsUnicode(false);
            e.Property(x => x.UfCrm).IsFixedLength().IsUnicode(false);
            e.Property(x => x.Cpf).IsFixedLength().IsUnicode(false);
            e.HasMany(x => x.Especialidades).WithOne().HasForeignKey(x => x.MedicoId);
        });
        b.Entity<MedicoEspecialidade>(e =>
        {
            e.ToTable("MedicoEspecialidade", "cadastro");
            e.HasKey(x => new { x.MedicoId, x.EspecialidadeId });
        });
        b.Entity<Convenio>(e =>
        {
            e.ToTable("Convenio", "cadastro");
            e.HasMany(x => x.Planos).WithOne(x => x.Convenio).HasForeignKey(x => x.ConvenioId);
        });
        b.Entity<Plano>().ToTable("Plano", "cadastro");
        b.Entity<Paciente>(e =>
        {
            e.ToTable("Paciente", "cadastro");
            e.Property(x => x.Cpf).IsFixedLength().IsUnicode(false);
            e.Property(x => x.Cns).IsFixedLength().IsUnicode(false);
            e.Property(x => x.Sexo).IsFixedLength().IsUnicode(false).HasMaxLength(1);
            e.Property(x => x.Uf).IsFixedLength().IsUnicode(false);
            e.Property(x => x.Cep).IsFixedLength().IsUnicode(false);
            e.HasMany(x => x.Planos).WithOne().HasForeignKey(x => x.PacienteId);
        });
        b.Entity<PacientePlano>().ToTable("PacientePlano", "cadastro");

        // ---------- agenda ----------
        b.Entity<HorarioAtendimento>().ToTable("HorarioAtendimento", "agenda");
        b.Entity<BloqueioAgenda>().ToTable("BloqueioAgenda", "agenda");
        b.Entity<Agendamento>(e =>
        {
            e.ToTable("Agendamento", "agenda");
            e.Property(x => x.StatusId).HasConversion<byte>();
            e.Property(x => x.TipoAtendimentoId).HasConversion<byte>();
            e.Property(x => x.Valor).HasPrecision(10, 2);
            e.Property(x => x.VersaoLinha).IsRowVersion();
            e.HasMany(x => x.Historico).WithOne().HasForeignKey(x => x.AgendamentoId);
        });
        b.Entity<AgendamentoHistoricoStatus>(e =>
        {
            e.ToTable("AgendamentoHistoricoStatus", "agenda");
            e.Property(x => x.StatusAnteriorId).HasConversion<byte?>();
            e.Property(x => x.StatusNovoId).HasConversion<byte>();
        });

        // ---------- prontuario ----------
        b.Entity<ResumoClinico>(e =>
        {
            e.ToTable("ResumoClinico", "prontuario");
            e.HasKey(x => x.PacienteId);
            e.Property(x => x.PacienteId).ValueGeneratedNever();
        });
        b.Entity<Evolucao>(e =>
        {
            // A tabela tem trigger (TR_Evolucao_Imutavel): o EF não pode usar OUTPUT sem INTO
            e.ToTable("Evolucao", "prontuario", t => t.HasTrigger("TR_Evolucao_Imutavel"));
            e.Property(x => x.Situacao).IsFixedLength().IsUnicode(false).HasMaxLength(1);
            e.Property(x => x.PesoKg).HasPrecision(5, 2);
            e.Property(x => x.AlturaCm).HasPrecision(5, 1);
            e.Property(x => x.TemperaturaC).HasPrecision(4, 1);
            e.Property(x => x.HashConteudo).HasMaxLength(32);
            e.HasOne(x => x.Medico).WithMany().HasForeignKey(x => x.MedicoId);
        });
        b.Entity<Prescricao>(e =>
        {
            e.ToTable("Prescricao", "prontuario");
            e.Property(x => x.Tipo).IsFixedLength().IsUnicode(false).HasMaxLength(1);
            e.HasMany(x => x.Itens).WithOne().HasForeignKey(x => x.PrescricaoId);
        });
        b.Entity<PrescricaoItem>().ToTable("PrescricaoItem", "prontuario");
        b.Entity<Anexo>(e =>
        {
            e.ToTable("Anexo", "prontuario");
            e.Property(x => x.Tipo).IsUnicode(false).HasMaxLength(20);
            e.Property(x => x.Descricao).HasMaxLength(200);
            e.Property(x => x.NomeArquivo).HasMaxLength(255);
            e.Property(x => x.ContentType).IsUnicode(false).HasMaxLength(100);
            e.Property(x => x.CaminhoArmazenamento).HasMaxLength(500);
            e.Property(x => x.HashSha256).HasMaxLength(32);
        });
        b.Entity<DocumentoClinico>(e =>
        {
            e.ToTable("Documento", "prontuario");
            e.Property(x => x.Tipo).IsUnicode(false).HasMaxLength(20);
            e.Property(x => x.Titulo).HasMaxLength(100);
            e.Property(x => x.Cid).IsUnicode(false).HasMaxLength(10);
            e.HasOne(x => x.Medico).WithMany().HasForeignKey(x => x.MedicoId);
        });
        b.Entity<ModeloDocumento>(e =>
        {
            e.ToTable("ModeloDocumento", "prontuario");
            e.Property(x => x.Tipo).IsUnicode(false).HasMaxLength(20);
            e.Property(x => x.Nome).HasMaxLength(100);
        });

        // DATETIME2(0) na maioria das colunas: o SQL Server arredonda os segundos fracionários
        foreach (var p in b.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)))
        {
            if (p.Name != nameof(LogAuditoria.OcorridoEm)) p.SetColumnType("datetime2(0)");
        }

        // Isolamento por cliente: filtro global em todas as entidades que pertencem a uma clínica
        var filtrar = typeof(AppDbContext).GetMethod(nameof(FiltrarPorClinica), BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var t in b.Model.GetEntityTypes().Select(t => t.ClrType).Where(typeof(IPertenceClinica).IsAssignableFrom).ToList())
            filtrar.MakeGenericMethod(t).Invoke(this, [b]);
    }

    private void FiltrarPorClinica<T>(ModelBuilder b) where T : class, IPertenceClinica =>
        b.Entity<T>().HasQueryFilter(e => e.ClinicaId == ClinicaAtualId);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AplicarClinica();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        AplicarClinica();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    /// <summary>
    /// Preenche ClinicaId nas inclusões e impede gravar dados de outra clínica
    /// (defesa extra; o banco também valida com chaves estrangeiras compostas por ClinicaId).
    /// Sem usuário de clínica logado (área /Admin ou o próprio login) o ClinicaId tem de vir preenchido pelo serviço;
    /// nesses casos as leituras já são restritas pelo filtro global, a não ser com IgnoreQueryFilters() explícito.
    /// </summary>
    private void AplicarClinica()
    {
        var clinica = ClinicaAtualId;
        if (clinica is null)
        {
            if (ChangeTracker.Entries<IPertenceClinica>().Any(e => e.State == EntityState.Added && e.Entity.ClinicaId == 0))
                throw new InvalidOperationException("Informe a clínica do registro.");
            return;
        }
        foreach (var e in ChangeTracker.Entries<IPertenceClinica>())
        {
            switch (e.State)
            {
                case EntityState.Added when e.Entity.ClinicaId == 0:
                    e.Entity.ClinicaId = clinica ?? throw new InvalidOperationException("Não há clínica definida para gravar o registro.");
                    break;
                case EntityState.Added:
                case EntityState.Deleted:
                case EntityState.Modified:
                    if (e.Entity.ClinicaId != clinica || (e.State == EntityState.Modified && e.Property(x => x.ClinicaId).IsModified))
                        throw new InvalidOperationException("Registro de outra clínica.");
                    break;
            }
        }
    }

    public async Task<T> EmTransacaoAsync<T>(Func<Task<T>> acao, CancellationToken ct = default)
    {
        var strategy = Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await Database.BeginTransactionAsync(ct);
            var resultado = await acao();
            await tx.CommitAsync(ct);
            return resultado;
        });
    }

    public async Task<T> ExecutarComLockAsync<T>(string recurso, Func<Task<T>> acao, CancellationToken ct = default)
    {
        var strategy = Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            await Database.ExecuteSqlRawAsync(
                "DECLARE @r INT; EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000; IF @r < 0 THROW 50010, 'Agenda ocupada, tente novamente.', 1;",
                [new SqlParameter("@recurso", recurso)], ct);
            var resultado = await acao();
            await tx.CommitAsync(ct);
            return resultado;
        });
    }
}
