using Consultorio.Application.Abstractions;
using Consultorio.Application.Auth;
using Consultorio.Application.Common;
using Consultorio.Domain.Common;
using Consultorio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Admin;

public record ClienteDto(int Id, string Nome, string? Cnpj, string? Telefone, string? Email, string? Endereco,
    string? Cidade, string? Uf, short? MaxMedicos, short? MaxUsuarios, string? Observacoes, string Situacao, DateTime CriadoEm,
    bool AcessoDefinitivo, DateOnly? AcessoAte, string? MotivoSituacao = null, DateTime? SituacaoDesde = null,
    decimal? ValorMensalidade = null, byte DiaVencimento = 10, short DiasTolerancia = 5, bool BloqueioPorAtraso = true,
    bool BloqueadoPorAtraso = false)
{
    public bool Ativo => Situacao == SituacoesCliente.Ativo;
    public bool AcessoVencido => !AcessoDefinitivo && !(AcessoAte >= DateOnly.FromDateTime(DateTime.Today));
}

public record ClienteSalvarRequest(string Nome, string? Cnpj, string? Telefone, string? Email, string? Endereco,
    string? Cidade, string? Uf, short? MaxMedicos, short? MaxUsuarios, string? Observacoes, string Situacao,
    bool AcessoDefinitivo, DateOnly? AcessoAte, string? MotivoSituacao = null,
    decimal? ValorMensalidade = null, byte DiaVencimento = 10, short DiasTolerancia = 5, bool BloqueioPorAtraso = true);

/// <summary>Novo usuário de uma clínica criado pelo suporte (normalmente o administrador da clínica).</summary>
public record UsuarioClienteRequest(string Nome, string Email, string Senha, List<int> PerfilIds);

public record UsuarioClienteDto(int Id, string Nome, string Email, IReadOnlyList<string> Perfis, bool Ativo,
    bool Bloqueado, DateTime? UltimoAcessoEm, IReadOnlyList<int> PerfilIds);
public record MedicoClienteDto(int Id, string Nome, string Crm, string UfCrm, bool Ativo, string? UsuarioEmail);
public record ClienteDetalhesDto(ClienteDto Cliente, UsoPlanoDto Uso, int Pacientes, int AgendamentosMes,
    IReadOnlyList<UsuarioClienteDto> Usuarios, IReadOnlyList<MedicoClienteDto> Medicos, int Anexos = 0, long BytesAnexos = 0);

/// <summary>
/// Manutenção dos clientes (clínicas) pela área /Admin: dados, limites do plano, situação (ativo/bloqueado/inativo) e usuários.
/// O administrador do sistema não tem clínica, então todas as consultas aqui usam IgnoreQueryFilters()
/// e informam o ClinicaId explicitamente.
/// </summary>
public class ClienteService(IAppDbContext db, ISenhaHasher hasher, IAuditoria auditoria, LimitesPlano limites, CobrancaService cobrancas)
{
    public async Task<ClienteDto> ObterAsync(int id, CancellationToken ct)
    {
        var c = await db.Clinicas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NaoEncontradoException("Cliente não encontrado.");
        return Dto(c);
    }

    public async Task<ClienteDetalhesDto> DetalhesAsync(int id, CancellationToken ct)
    {
        var cliente = await ObterAsync(id, ct);
        var uso = await limites.UsoAsync(id, ct);
        var iniMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var pacientes = await db.Pacientes.IgnoreQueryFilters().CountAsync(p => p.ClinicaId == id && p.Ativo, ct);
        var agendamentos = await db.Agendamentos.IgnoreQueryFilters().CountAsync(a => a.ClinicaId == id && a.Inicio >= iniMes && a.Inicio < iniMes.AddMonths(1), ct);
        var agora = DateTime.Now;
        var usuarios = await db.Usuarios.IgnoreQueryFilters().AsNoTracking().Where(u => u.ClinicaId == id)
            .OrderByDescending(u => u.Ativo).ThenBy(u => u.Nome)
            .Select(u => new UsuarioClienteDto(u.Id, u.Nome, u.Email, u.Perfis.Select(p => p.Perfil.Nome).ToList(), u.Ativo,
                u.BloqueadoAte > agora, u.UltimoAcessoEm, u.Perfis.Select(p => p.PerfilId).ToList()))
            .ToListAsync(ct);
        var medicos = await db.Medicos.IgnoreQueryFilters().AsNoTracking().Where(m => m.ClinicaId == id)
            .OrderByDescending(m => m.Ativo).ThenBy(m => m.Nome)
            .Select(m => new MedicoClienteDto(m.Id, m.Nome, m.Crm, m.UfCrm, m.Ativo,
                db.Usuarios.IgnoreQueryFilters().Where(u => u.Id == m.UsuarioId).Select(u => u.Email).FirstOrDefault()))
            .ToListAsync(ct);
        // Espaço em disco dos anexos (inclui os removidos da lista, que continuam guardados). Só contagem e soma: nada do conteúdo.
        var anexos = await db.Anexos.IgnoreQueryFilters().Where(a => a.ClinicaId == id)
            .GroupBy(a => 1).Select(g => new { Qtd = g.Count(), Bytes = g.Sum(a => a.TamanhoBytes) }).FirstOrDefaultAsync(ct);
        return new ClienteDetalhesDto(cliente, uso, pacientes, agendamentos, usuarios, medicos, anexos?.Qtd ?? 0, anexos?.Bytes ?? 0);
    }

    /// <summary>Cria ou altera o cliente. Na criação, <paramref name="admin"/> (administrador da clínica) é obrigatório.</summary>
    public async Task<int> SalvarAsync(int? id, ClienteSalvarRequest req, UsuarioClienteRequest? admin, CancellationToken ct)
    {
        var cnpj = string.IsNullOrWhiteSpace(req.Cnpj) ? null : new string(req.Cnpj.Where(char.IsDigit).ToArray());
        if (cnpj is not null && cnpj.Length != 14) throw new DomainException("CNPJ deve ter 14 dígitos.");
        if (cnpj is not null && await db.Clinicas.AnyAsync(c => c.Cnpj == cnpj && c.Id != id, ct))
            throw new DomainException("Já existe cliente com este CNPJ.");
        if (req.MaxMedicos < 0 || req.MaxUsuarios < 0) throw new DomainException("Limite não pode ser negativo.");
        if (id is null && admin is null) throw new DomainException("Informe o administrador da nova clínica.");
        if (id is null && req.MaxUsuarios == 0) throw new DomainException("O limite de usuários precisa comportar o administrador da clínica.");
        var situacao = ValidarSituacao(id is null ? SituacoesCliente.Ativo : req.Situacao, req.MotivoSituacao);
        if (req.ValorMensalidade < 0) throw new DomainException("O valor da mensalidade não pode ser negativo.");
        if (req.DiaVencimento is < 1 or > 28) throw new DomainException("O dia de vencimento deve ser de 1 a 28.");
        if (req.DiasTolerancia is < 0 or > 60) throw new DomainException("A tolerância deve ser de 0 a 60 dias.");
        if (!req.AcessoDefinitivo && req.AcessoAte is null)
            throw new DomainException("Informe até quando vai o período de acesso, ou marque a versão definitiva.");

        var atual = id is null ? null
            : await db.Clinicas.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NaoEncontradoException("Cliente não encontrado.");
        // Data já vencida só é recusada quando informada agora (editar outro campo de um cliente vencido continua possível)
        if (!req.AcessoDefinitivo && req.AcessoAte < DateOnly.FromDateTime(DateTime.Today)
            && (atual is null || atual.AcessoDefinitivo || req.AcessoAte != atual.AcessoAte))
            throw new DomainException("A data final do período de acesso já passou.");
        if (atual is not null && situacao == SituacoesCliente.Ativo && !atual.Ativo)
            await GarantirSemAtrasoAsync(atual.Id, req.BloqueioPorAtraso, req.DiasTolerancia, ct);

        Clinica c;
        object? anterior = null;
        if (atual is null)
        {
            c = new Clinica();
            db.Clinicas.Add(c);
        }
        else
        {
            c = atual;
            anterior = new { c.Nome, c.Situacao, c.MotivoSituacao, c.MaxMedicos, c.MaxUsuarios, c.AcessoDefinitivo, c.AcessoAte, c.ValorMensalidade, c.DiaVencimento, c.DiasTolerancia, c.BloqueioPorAtraso };
            c.AtualizadoEm = DateTime.Now;
        }

        c.Nome = req.Nome.Trim(); c.Cnpj = cnpj; c.Telefone = req.Telefone; c.Email = req.Email; c.Endereco = req.Endereco;
        c.Cidade = req.Cidade; c.Uf = req.Uf?.ToUpperInvariant(); c.MaxMedicos = req.MaxMedicos; c.MaxUsuarios = req.MaxUsuarios;
        c.Observacoes = req.Observacoes;
        c.ValorMensalidade = req.ValorMensalidade is > 0 ? Math.Round(req.ValorMensalidade.Value, 2) : null;
        c.DiaVencimento = req.DiaVencimento; c.DiasTolerancia = req.DiasTolerancia; c.BloqueioPorAtraso = req.BloqueioPorAtraso;
        if (atual is null || c.Situacao != situacao || c.MotivoSituacao != Motivo(situacao, req.MotivoSituacao))
            AplicarSituacao(c, situacao, req.MotivoSituacao);
        c.AcessoDefinitivo = req.AcessoDefinitivo;
        c.AcessoAte = req.AcessoDefinitivo ? null : req.AcessoAte;

        // Clínica e administrador nascem juntos: se o administrador falhar, a clínica não fica criada pela metade
        var salvo = await db.EmTransacaoAsync(async () =>
        {
            await db.SaveChangesAsync(ct);
            if (admin is not null) await CriarUsuarioAsync(c.Id, admin, ct);
            auditoria.Registrar(id is null ? "INCLUSAO" : "ALTERACAO", "seguranca.Clinica", c.Id, anterior,
                new { c.Nome, c.Situacao, c.MotivoSituacao, c.MaxMedicos, c.MaxUsuarios, c.AcessoDefinitivo, c.AcessoAte, c.ValorMensalidade, c.DiaVencimento, c.DiasTolerancia, c.BloqueioPorAtraso }, clinicaId: c.Id);
            await db.SaveChangesAsync(ct);
            return c.Id;
        }, ct);
        // Mudou a tolerância ou o bloqueio automático: aplica a regra de atraso já com a configuração nova
        if (id is not null) await cobrancas.VerificarAtrasoAsync(salvo, ct);
        return salvo;
    }

    /// <summary>Encerra o período de teste: a clínica passa a ter acesso sem data limite.</summary>
    public async Task LiberarDefinitivoAsync(int id, CancellationToken ct)
    {
        var c = await db.Clinicas.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NaoEncontradoException("Cliente não encontrado.");
        if (c.AcessoDefinitivo) return;
        var anterior = new { c.AcessoDefinitivo, c.AcessoAte };
        c.AcessoDefinitivo = true;
        c.AcessoAte = null;
        c.AtualizadoEm = DateTime.Now;
        auditoria.Registrar("ALTERACAO", "seguranca.Clinica", c.Id, anterior, new { c.AcessoDefinitivo, c.AcessoAte }, clinicaId: c.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Bloqueia, inativa ou reativa o cliente. Bloqueado e inativo deixam de entrar no sistema na hora
    /// (as sessões abertas caem na próxima requisição); os dados continuam guardados.
    /// </summary>
    public async Task AlterarSituacaoAsync(int id, string situacao, string? motivo, CancellationToken ct)
    {
        situacao = ValidarSituacao(situacao, motivo);
        var c = await db.Clinicas.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NaoEncontradoException("Cliente não encontrado.");
        if (c.Situacao == situacao && c.MotivoSituacao == Motivo(situacao, motivo)) return;
        if (situacao == SituacoesCliente.Ativo && !c.Ativo) await GarantirSemAtrasoAsync(c.Id, c.BloqueioPorAtraso, c.DiasTolerancia, ct);
        var anterior = new { c.Situacao, c.MotivoSituacao };
        AplicarSituacao(c, situacao, motivo);
        c.AtualizadoEm = DateTime.Now;
        auditoria.Registrar("SITUACAO", "seguranca.Clinica", c.Id, anterior, new { c.Situacao, c.MotivoSituacao }, clinicaId: c.Id);
        await db.SaveChangesAsync(ct);
    }

    private static string ValidarSituacao(string? situacao, string? motivo)
    {
        var s = (situacao ?? "").Trim().ToUpperInvariant();
        if (!SituacoesCliente.Todas.Contains(s)) throw new DomainException("Situação do cliente inválida.");
        if (s != SituacoesCliente.Ativo && string.IsNullOrWhiteSpace(motivo))
            throw new DomainException(s == SituacoesCliente.Bloqueado ? "Informe o motivo para bloquear o cliente." : "Informe o motivo para inativar o cliente.");
        if (motivo?.Trim().Length > 300) throw new DomainException("O motivo pode ter no máximo 300 caracteres.");
        return s;
    }

    private static string? Motivo(string situacao, string? motivo) =>
        situacao == SituacoesCliente.Ativo ? null : motivo?.Trim();

    /// <summary>Com bloqueio automático ligado, não adianta reativar à mão quem tem pagamento em atraso: a rotina bloquearia de novo.</summary>
    private async Task GarantirSemAtrasoAsync(int clinicaId, bool bloqueioPorAtraso, short diasTolerancia, CancellationToken ct)
    {
        if (!bloqueioPorAtraso) return;
        var atrasada = await CobrancaService.EmAtraso(db, clinicaId, diasTolerancia).OrderBy(x => x.Vencimento)
            .Select(x => new { x.Descricao, x.Vencimento }).FirstOrDefaultAsync(ct);
        if (atrasada is not null)
            throw new DomainException("O cliente tem pagamento em atraso ({0}, vencida em {1:d}). " +
                                      "Registre o pagamento, prorrogue o vencimento ou desligue o bloqueio automático por atraso.", atrasada.Descricao, atrasada.Vencimento);
    }

    /// <summary>Mudança feita pelo suporte: deixa de ser um bloqueio automático por atraso.</summary>
    private static void AplicarSituacao(Clinica c, string situacao, string? motivo)
    {
        c.BloqueadoPorAtraso = false;
        if (c.Situacao != situacao) c.SituacaoDesde = DateTime.Now;
        c.Situacao = situacao;
        c.MotivoSituacao = Motivo(situacao, motivo);
    }

    public async Task<int> NovoUsuarioAsync(int clinicaId, UsuarioClienteRequest req, CancellationToken ct)
    {
        await ObterAsync(clinicaId, ct);
        var id = await CriarUsuarioAsync(clinicaId, req, ct);
        await db.SaveChangesAsync(ct);
        return id;
    }

    public async Task AlterarSituacaoUsuarioAsync(int clinicaId, int usuarioId, bool ativo, CancellationToken ct)
    {
        var u = await UsuarioDaClinicaAsync(clinicaId, usuarioId, ct);
        if (ativo && !u.Ativo) await limites.GarantirVagaUsuarioAsync(clinicaId, ct);
        u.Ativo = ativo;
        u.AtualizadoEm = DateTime.Now;
        auditoria.Registrar("ALTERACAO", "seguranca.Usuario", u.Id, novo: new { u.Ativo }, clinicaId: clinicaId);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Troca os perfis de acesso de um usuário da clínica (vale a partir da próxima página que ele abrir).</summary>
    public async Task AlterarPerfisUsuarioAsync(int clinicaId, int usuarioId, List<int> perfilIds, CancellationToken ct)
    {
        var perfis = await UsuarioService.PerfisValidosAsync(db, clinicaId, perfilIds, ct);
        var u = await db.Usuarios.IgnoreQueryFilters().Include(x => x.Perfis).ThenInclude(x => x.Perfil)
                    .FirstOrDefaultAsync(x => x.Id == usuarioId && x.ClinicaId == clinicaId, ct)
                ?? throw new NaoEncontradoException("Usuário não encontrado neste cliente.");
        var anterior = u.Perfis.Select(p => p.Perfil.Nome).OrderBy(n => n).ToList();
        u.Perfis.Clear();
        foreach (var p in perfis) u.Perfis.Add(new UsuarioPerfil { Perfil = p, PerfilId = p.Id });
        u.AtualizadoEm = DateTime.Now;
        auditoria.Registrar("ALTERACAO", "seguranca.Usuario", u.Id, new { Perfis = anterior },
            new { Perfis = perfis.Select(p => p.Nome).OrderBy(n => n).ToList() }, clinicaId: clinicaId);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Suporte: define nova senha e desbloqueia o usuário da clínica.</summary>
    public async Task RedefinirSenhaUsuarioAsync(int clinicaId, int usuarioId, string novaSenha, CancellationToken ct)
    {
        var u = await UsuarioDaClinicaAsync(clinicaId, usuarioId, ct);
        UsuarioService.ValidarSenha(novaSenha);
        u.SenhaHash = hasher.Gerar(novaSenha);
        u.TentativasFalhas = 0; u.BloqueadoAte = null; u.AtualizadoEm = DateTime.Now;
        auditoria.Registrar("ALTERACAO_SENHA", "seguranca.Usuario", u.Id, clinicaId: clinicaId);
        await db.SaveChangesAsync(ct);
    }

    private async Task<int> CriarUsuarioAsync(int clinicaId, UsuarioClienteRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (await db.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            throw new DomainException("Já existe um usuário com este e-mail.");
        UsuarioService.ValidarSenha(req.Senha);
        await limites.GarantirVagaUsuarioAsync(clinicaId, ct);
        var perfis = await UsuarioService.PerfisValidosAsync(db, clinicaId, req.PerfilIds, ct);

        var u = new Usuario { ClinicaId = clinicaId, Nome = req.Nome.Trim(), Email = email, SenhaHash = hasher.Gerar(req.Senha) };
        foreach (var p in perfis) u.Perfis.Add(new UsuarioPerfil { Perfil = p, PerfilId = p.Id });
        db.Usuarios.Add(u);
        await db.SaveChangesAsync(ct);
        auditoria.Registrar("INCLUSAO", "seguranca.Usuario", u.Id, novo: new { u.Nome, u.Email, Perfis = perfis.Select(p => p.Nome).ToList() }, clinicaId: clinicaId);
        return u.Id;
    }

    private async Task<Usuario> UsuarioDaClinicaAsync(int clinicaId, int usuarioId, CancellationToken ct) =>
        await db.Usuarios.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == usuarioId && u.ClinicaId == clinicaId, ct)
        ?? throw new NaoEncontradoException("Usuário não encontrado neste cliente.");

    private static ClienteDto Dto(Clinica c) => new(c.Id, c.Nome, c.Cnpj, c.Telefone, c.Email, c.Endereco, c.Cidade, c.Uf,
        c.MaxMedicos, c.MaxUsuarios, c.Observacoes, c.Situacao, c.CriadoEm, c.AcessoDefinitivo, c.AcessoAte,
        c.MotivoSituacao, c.SituacaoDesde, c.ValorMensalidade, c.DiaVencimento, c.DiasTolerancia, c.BloqueioPorAtraso, c.BloqueadoPorAtraso);
}
