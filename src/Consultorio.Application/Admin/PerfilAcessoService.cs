using Consultorio.Application.Abstractions;
using Consultorio.Application.Common;
using Consultorio.Domain.Common;
using Consultorio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Admin;

public record PerfilResumoDto(int Id, string Nome, string? Descricao, int? ClinicaId, string? Cliente, bool Ativo,
    int Transacoes, int Usuarios);
public record PerfilDto(int Id, string Nome, string? Descricao, int? ClinicaId, bool Ativo, IReadOnlyList<string> Transacoes);
public record PerfilSalvarRequest(string Nome, string? Descricao, int? ClinicaId, bool Ativo, List<string> Transacoes);
/// <summary>Perfil que pode ser dado a um usuário da clínica (padrão ou exclusivo dela).</summary>
public record PerfilOpcaoDto(int Id, string Nome, string? Descricao, bool Exclusivo);

/// <summary>
/// Perfis de acesso e suas transações (área /Admin). Perfil sem cliente vale para todas as clínicas;
/// com cliente, só aparece para os usuários daquela clínica.
/// </summary>
public class PerfilAcessoService(IAppDbContext db, IAuditoria auditoria)
{
    public async Task<List<PerfilResumoDto>> ListarAsync(int? clinicaId, CancellationToken ct) =>
        await (from p in db.Perfis.AsNoTracking()
               where clinicaId == null || p.ClinicaId == null || p.ClinicaId == clinicaId
               orderby p.ClinicaId != null, p.Nome
               select new PerfilResumoDto(p.Id, p.Nome, p.Descricao, p.ClinicaId,
                   db.Clinicas.Where(c => c.Id == p.ClinicaId).Select(c => c.Nome).FirstOrDefault(), p.Ativo,
                   p.Permissoes.Count(),
                   db.Usuarios.IgnoreQueryFilters().Count(u => u.Ativo && u.Perfis.Any(up => up.PerfilId == p.Id))))
            .ToListAsync(ct);

    public async Task<PerfilDto> ObterAsync(int id, CancellationToken ct) =>
        await db.Perfis.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new PerfilDto(p.Id, p.Nome, p.Descricao, p.ClinicaId, p.Ativo, p.Permissoes.Select(x => x.Permissao.Codigo).ToList()))
            .FirstOrDefaultAsync(ct) ?? throw new NaoEncontradoException("Perfil não encontrado.");

    public async Task<int> SalvarAsync(int? id, PerfilSalvarRequest req, CancellationToken ct)
    {
        var nome = req.Nome.Trim();
        if (nome.Length == 0) throw new DomainException("Informe o nome do perfil.");
        if (req.Transacoes.Count == 0) throw new DomainException("Marque ao menos uma transação.");
        var invalidas = req.Transacoes.Where(t => !Transacoes.Existe(t)).ToList();
        if (invalidas.Count > 0) throw new DomainException("Transação desconhecida: " + string.Join(", ", invalidas));
        if (req.ClinicaId is int cid && !await db.Clinicas.AnyAsync(c => c.Id == cid, ct))
            throw new NaoEncontradoException("Cliente não encontrado.");
        // Nome único entre os perfis que um mesmo usuário poderia ver (padrões + exclusivos do cliente)
        if (await db.Perfis.AnyAsync(p => p.Id != id && p.Nome == nome
                && (req.ClinicaId == null || p.ClinicaId == null || p.ClinicaId == req.ClinicaId), ct))
            throw new DomainException("Já existe um perfil com este nome para este cliente (ou um perfil padrão com o mesmo nome).");

        var perfil = id is null ? null
            : await db.Perfis.Include(p => p.Permissoes).ThenInclude(x => x.Permissao).FirstOrDefaultAsync(p => p.Id == id, ct)
              ?? throw new NaoEncontradoException("Perfil não encontrado.");
        if (perfil is not null && perfil.ClinicaId != req.ClinicaId
            && await db.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.ClinicaId != req.ClinicaId && u.Perfis.Any(up => up.PerfilId == perfil.Id), ct))
            throw new DomainException("Este perfil já está em usuários de outros clientes; não dá para torná-lo exclusivo de um cliente.");

        var anterior = perfil is null ? null
            : new { perfil.Nome, perfil.ClinicaId, perfil.Ativo, Transacoes = perfil.Permissoes.Select(x => x.Permissao.Codigo).OrderBy(c => c).ToList() };
        perfil ??= db.Perfis.Add(new Perfil()).Entity;
        perfil.Nome = nome;
        perfil.Descricao = string.IsNullOrWhiteSpace(req.Descricao) ? null : req.Descricao.Trim();
        perfil.ClinicaId = req.ClinicaId;
        perfil.Ativo = req.Ativo;
        if (id is not null) perfil.AtualizadoEm = DateTime.Now;

        var permissoes = await db.Permissoes.Where(x => req.Transacoes.Contains(x.Codigo)).ToListAsync(ct);
        if (permissoes.Count != req.Transacoes.Distinct().Count())
            throw new DomainException("Transações ainda não cadastradas no banco. Reinicie o site para sincronizar.");
        perfil.Permissoes.Clear();
        foreach (var p in permissoes) perfil.Permissoes.Add(new PerfilPermissao { PermissaoId = p.Id, Permissao = p });

        await db.SaveChangesAsync(ct);
        auditoria.Registrar(id is null ? "INCLUSAO" : "ALTERACAO", "seguranca.Perfil", perfil.Id, anterior,
            new { perfil.Nome, perfil.ClinicaId, perfil.Ativo, Transacoes = req.Transacoes.Distinct().OrderBy(c => c).ToList() });
        await db.SaveChangesAsync(ct);
        return perfil.Id;
    }

    /// <summary>Perfis ativos que podem ser atribuídos a usuários da clínica.</summary>
    public static IQueryable<Perfil> DisponiveisPara(IAppDbContext db, int clinicaId) =>
        db.Perfis.Where(p => p.Ativo && (p.ClinicaId == null || p.ClinicaId == clinicaId));

    public async Task<List<PerfilOpcaoDto>> OpcoesAsync(int clinicaId, CancellationToken ct) =>
        await DisponiveisPara(db, clinicaId).OrderBy(p => p.ClinicaId != null).ThenBy(p => p.Nome)
            .Select(p => new PerfilOpcaoDto(p.Id, p.Nome, p.Descricao, p.ClinicaId != null)).ToListAsync(ct);

    /// <summary>Algum dos perfis (ativos) libera a transação?</summary>
    public Task<bool> LiberaAsync(IReadOnlyCollection<int> perfilIds, string transacao, CancellationToken ct) =>
        db.Perfis.AnyAsync(p => perfilIds.Contains(p.Id) && p.Ativo && p.Permissoes.Any(x => x.Permissao.Codigo == transacao), ct);

    /// <summary>Perfis padrão ativos (servem para qualquer cliente, inclusive um que ainda está sendo criado).</summary>
    public async Task<List<PerfilOpcaoDto>> OpcoesPadraoAsync(CancellationToken ct) =>
        await db.Perfis.Where(p => p.Ativo && p.ClinicaId == null).OrderBy(p => p.Nome)
            .Select(p => new PerfilOpcaoDto(p.Id, p.Nome, p.Descricao, false)).ToListAsync(ct);

    /// <summary>
    /// Garante no banco todas as transações do catálogo e, em banco sem perfis, cria os perfis padrão.
    /// Roda ao iniciar o site; não altera perfis já existentes.
    /// </summary>
    public static async Task SincronizarAsync(IAppDbContext db, CancellationToken ct = default)
    {
        var existentes = await db.Permissoes.ToListAsync(ct);
        foreach (var t in Transacoes.Todas)
        {
            var p = existentes.FirstOrDefault(x => x.Codigo == t.Codigo);
            if (p is null) db.Permissoes.Add(new Permissao { Codigo = t.Codigo, Descricao = t.Nome });
            else if (p.Descricao != t.Nome) p.Descricao = t.Nome;
        }
        await db.SaveChangesAsync(ct);

        if (await db.Perfis.AnyAsync(ct)) return;
        var todas = await db.Permissoes.ToDictionaryAsync(x => x.Codigo, ct);
        foreach (var (nome, codigos) in Transacoes.Padrao)
        {
            var perfil = new Perfil { Nome = nome };
            foreach (var c in codigos) perfil.Permissoes.Add(new PerfilPermissao { PermissaoId = todas[c].Id, Permissao = todas[c] });
            db.Perfis.Add(perfil);
        }
        await db.SaveChangesAsync(ct);
    }
}
