using Consultorio.Application.Abstractions;
using Consultorio.Application.Common;
using Consultorio.Domain.Common;
using Consultorio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Auth;

public record UsuarioDto(int Id, string Nome, string Email, bool Ativo, IReadOnlyList<string> Perfis, DateTime? UltimoAcessoEm,
    IReadOnlyList<int> PerfilIds);
public record UsuarioSalvarRequest(string Nome, string Email, string? Senha, bool Ativo, List<int> PerfilIds);

/// <summary>Usuários da clínica do administrador logado (o filtro global esconde os de outras clínicas).</summary>
public class UsuarioService(IAppDbContext db, ISenhaHasher hasher, IAuditoria auditoria, IUsuarioAtual usuarioAtual, LimitesPlano limites)
{
    public async Task<List<UsuarioDto>> ListarAsync(CancellationToken ct) => await Listar(db.Usuarios).ToListAsync(ct);

    /// <summary>Usuários ativos com a transação liberada por algum perfil ativo (ex.: quem pode ser vinculado a um médico).</summary>
    public async Task<List<UsuarioDto>> ListarComTransacaoAsync(string transacao, CancellationToken ct) =>
        await Listar(db.Usuarios.Where(u => u.Ativo && u.Perfis.Any(up => up.Perfil.Ativo
            && up.Perfil.Permissoes.Any(x => x.Permissao.Codigo == transacao)))).ToListAsync(ct);

    private static IQueryable<UsuarioDto> Listar(IQueryable<Usuario> usuarios) =>
        usuarios.AsNoTracking().OrderBy(u => u.Nome)
            .Select(u => new UsuarioDto(u.Id, u.Nome, u.Email, u.Ativo,
                u.Perfis.Select(p => p.Perfil.Nome).ToList(), u.UltimoAcessoEm, u.Perfis.Select(p => p.PerfilId).ToList()));

    /// <summary>Confere e carrega os perfis escolhidos: ativos e padrão ou exclusivos desta clínica.</summary>
    public static async Task<List<Perfil>> PerfisValidosAsync(IAppDbContext db, int clinicaId, IReadOnlyCollection<int> perfilIds, CancellationToken ct)
    {
        if (perfilIds.Count == 0) throw new DomainException("Informe ao menos um perfil.");
        var perfis = await db.Perfis.Where(p => perfilIds.Contains(p.Id) && p.Ativo && (p.ClinicaId == null || p.ClinicaId == clinicaId)).ToListAsync(ct);
        if (perfis.Count != perfilIds.Distinct().Count()) throw new DomainException("Perfil de acesso inválido para esta clínica.");
        return perfis;
    }

    public async Task<int> SalvarAsync(int? id, UsuarioSalvarRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        // E-mail é o login: único no sistema inteiro, inclusive entre clínicas
        if (await db.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email == email && u.Id != id, ct))
            throw new DomainException("Já existe um usuário com este e-mail (nesta ou em outra clínica).");

        var clinicaId = usuarioAtual.ClinicaId ?? throw new DomainException("Não há clínica definida para o usuário.");
        var existente = id is null ? null
            : await db.Usuarios.Include(u => u.Perfis).FirstOrDefaultAsync(u => u.Id == id, ct)
              ?? throw new NaoEncontradoException("Usuário não encontrado.");
        if (existente is null && string.IsNullOrWhiteSpace(req.Senha))
            throw new DomainException("Informe a senha inicial.");
        // Limite de usuários do plano: conta ao incluir ativo ou ao reativar (antes de alterar qualquer coisa)
        if (req.Ativo && existente is not { Ativo: true })
            await limites.GarantirVagaUsuarioAsync(clinicaId, ct);

        if (!string.IsNullOrWhiteSpace(req.Senha)) ValidarSenha(req.Senha);
        var perfis = await PerfisValidosAsync(db, clinicaId, req.PerfilIds, ct);

        var usuario = existente ?? db.Usuarios.Add(new Usuario { ClinicaId = clinicaId }).Entity;
        if (existente is not null) usuario.AtualizadoEm = DateTime.Now;

        usuario.Nome = req.Nome.Trim();
        usuario.Email = email;
        usuario.Ativo = req.Ativo;
        if (!string.IsNullOrWhiteSpace(req.Senha))
        {
            ValidarSenha(req.Senha);
            usuario.SenhaHash = hasher.Gerar(req.Senha);
        }

        usuario.Perfis.Clear();
        foreach (var p in perfis) usuario.Perfis.Add(new UsuarioPerfil { Perfil = p, PerfilId = p.Id });

        await db.SaveChangesAsync(ct);
        auditoria.Registrar(id is null ? "INCLUSAO" : "ALTERACAO", "seguranca.Usuario", usuario.Id,
            novo: new { usuario.Nome, usuario.Email, usuario.Ativo, Perfis = perfis.Select(p => p.Nome).ToList() });
        await db.SaveChangesAsync(ct);
        return usuario.Id;
    }

    public static void ValidarSenha(string senha)
    {
        if (senha.Length < 8 || !senha.Any(char.IsDigit) || !senha.Any(char.IsLetter))
            throw new DomainException("A senha deve ter ao menos 8 caracteres, com letras e números.");
    }
}
