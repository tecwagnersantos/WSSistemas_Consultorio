using Consultorio.Application.Abstractions;
using Consultorio.Application.Auth;
using Consultorio.Application.Common;
using Consultorio.Domain.Common;
using Consultorio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Admin;

public record AdminLogadoDto(int Id, string Nome, string Email, string? Idioma = null);
public record AdministradorDto(int Id, string Nome, string Email, bool Ativo, DateTime? UltimoAcessoEm);
public record AdministradorSalvarRequest(string Nome, string Email, string? Senha, bool Ativo);

/// <summary>
/// Login e cadastro dos administradores do sistema (área /Admin). Tabela, senha e cookie
/// separados dos usuários das clínicas: um login de clínica não abre a área admin e vice-versa.
/// </summary>
public class AdminAuthService(IAppDbContext db, ISenhaHasher hasher, IAuditoria auditoria)
{
    private const int MaxTentativas = 5;
    private static readonly TimeSpan TempoBloqueio = TimeSpan.FromMinutes(15);

    public async Task<AdminLogadoDto> LoginAsync(string email, string senha, CancellationToken ct)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        var a = await db.AdministradoresSistema.FirstOrDefaultAsync(x => x.Email == email, ct);
        if (a is null || !a.Ativo) throw new DomainException("E-mail ou senha inválidos.");
        if (a.BloqueadoAte > DateTime.Now)
            throw new DomainException("Acesso bloqueado temporariamente por excesso de tentativas. Tente novamente em alguns minutos.");

        if (!hasher.Verificar(a.SenhaHash, senha ?? ""))
        {
            a.TentativasFalhas++;
            if (a.TentativasFalhas >= MaxTentativas) { a.BloqueadoAte = DateTime.Now.Add(TempoBloqueio); a.TentativasFalhas = 0; }
            await db.SaveChangesAsync(ct);
            throw new DomainException("E-mail ou senha inválidos.");
        }

        a.TentativasFalhas = 0; a.BloqueadoAte = null; a.UltimoAcessoEm = DateTime.Now;
        auditoria.Registrar("LOGIN", "seguranca.AdministradorSistema", a.Id, administradorSistemaId: a.Id);
        await db.SaveChangesAsync(ct);
        return new AdminLogadoDto(a.Id, a.Nome, a.Email, a.Idioma);
    }

    public async Task DefinirIdiomaAsync(int id, string? idioma, CancellationToken ct)
    {
        var a = await db.AdministradoresSistema.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null || a.Idioma == idioma) return;
        a.Idioma = idioma;
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> SessaoValidaAsync(int id, CancellationToken ct) =>
        db.AdministradoresSistema.AnyAsync(a => a.Id == id && a.Ativo, ct);

    public async Task RegistrarLogoutAsync(int id, CancellationToken ct)
    {
        auditoria.Registrar("LOGOUT", "seguranca.AdministradorSistema", id, administradorSistemaId: id);
        await db.SaveChangesAsync(ct);
    }

    public async Task AlterarSenhaAsync(int id, string senhaAtual, string novaSenha, CancellationToken ct)
    {
        var a = await db.AdministradoresSistema.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NaoEncontradoException("Administrador não encontrado.");
        if (!hasher.Verificar(a.SenhaHash, senhaAtual)) throw new DomainException("A senha atual não confere.");
        UsuarioService.ValidarSenha(novaSenha);
        a.SenhaHash = hasher.Gerar(novaSenha);
        a.AtualizadoEm = DateTime.Now;
        auditoria.Registrar("ALTERACAO_SENHA", "seguranca.AdministradorSistema", a.Id);
        await db.SaveChangesAsync(ct);
    }

    public Task<List<AdministradorDto>> ListarAsync(CancellationToken ct) =>
        db.AdministradoresSistema.AsNoTracking().OrderBy(a => a.Nome)
            .Select(a => new AdministradorDto(a.Id, a.Nome, a.Email, a.Ativo, a.UltimoAcessoEm)).ToListAsync(ct);

    public async Task<int> SalvarAsync(int? id, AdministradorSalvarRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (await db.AdministradoresSistema.AnyAsync(a => a.Email == email && a.Id != id, ct))
            throw new DomainException("Já existe um administrador com este e-mail.");

        AdministradorSistema a;
        if (id is null)
        {
            if (string.IsNullOrWhiteSpace(req.Senha)) throw new DomainException("Informe a senha inicial.");
            a = new AdministradorSistema();
            db.AdministradoresSistema.Add(a);
        }
        else
        {
            a = await db.AdministradoresSistema.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NaoEncontradoException("Administrador não encontrado.");
            a.AtualizadoEm = DateTime.Now;
            if (!req.Ativo && a.Ativo && !await db.AdministradoresSistema.AnyAsync(x => x.Id != id && x.Ativo, ct))
                throw new DomainException("Mantenha ao menos um administrador do sistema ativo.");
        }
        a.Nome = req.Nome.Trim(); a.Email = email; a.Ativo = req.Ativo;
        if (!string.IsNullOrWhiteSpace(req.Senha))
        {
            UsuarioService.ValidarSenha(req.Senha);
            a.SenhaHash = hasher.Gerar(req.Senha);
            a.TentativasFalhas = 0; a.BloqueadoAte = null;
        }
        await db.SaveChangesAsync(ct);
        auditoria.Registrar(id is null ? "INCLUSAO" : "ALTERACAO", "seguranca.AdministradorSistema", a.Id, novo: new { a.Nome, a.Email, a.Ativo });
        await db.SaveChangesAsync(ct);
        return a.Id;
    }
}
