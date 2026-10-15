using Consultorio.Application.Admin;
using Consultorio.Application.Abstractions;
using Consultorio.Domain.Common;
using Consultorio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Auth;

public record LoginRequest(string Email, string Senha);
public record UsuarioLogadoDto(int Id, string Nome, string Email, IReadOnlyList<string> Perfis, int? MedicoId,
    int ClinicaId, string ClinicaNome, string? Idioma = null);

/// <summary>
/// Resultado da conferência da sessão. <c>AcessoAte</c> só vem preenchido em clínica ainda no período de acesso (não definitiva).
/// <c>Transacoes</c>: o que os perfis do usuário liberam agora (mudanças feitas na área /Admin valem na próxima página).
/// </summary>
public record SituacaoSessao(bool Valida, DateOnly? AcessoAte, IReadOnlyList<string> Transacoes, AvisoCobrancaDto? Cobranca = null)
{
    public static readonly SituacaoSessao Invalida = new(false, null, []);
}

/// <summary>
/// Valida as credenciais do usuário. A sessão em si (cookie de autenticação) é criada pela camada Web.
/// </summary>
public class AuthService(IAppDbContext db, ISenhaHasher hasher, IAuditoria auditoria, CobrancaService cobrancas)
{
    private const int MaxTentativas = 5;
    private static readonly TimeSpan TempoBloqueio = TimeSpan.FromMinutes(15);

    public async Task<UsuarioLogadoDto> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        var email = (req.Email ?? "").Trim().ToLowerInvariant();
        // Antes do login não há clínica definida: o e-mail é único no sistema inteiro e identifica a clínica
        var usuario = await db.Usuarios.IgnoreQueryFilters()
            .Include(u => u.Clinica).Include(u => u.Perfis).ThenInclude(p => p.Perfil)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (usuario is null || !usuario.Ativo)
            throw new DomainException("E-mail ou senha inválidos.");
        // Garante a regra de atraso mesmo que a rotina periódica ainda não tenha rodado hoje (ex.: site parado no IIS)
        await cobrancas.VerificarAtrasoAsync(usuario.ClinicaId, ct);
        if (usuario.Clinica.Situacao == SituacoesCliente.Bloqueado)
            throw new DomainException(usuario.Clinica.BloqueadoPorAtraso
                ? "O acesso desta clínica está bloqueado por pagamento em atraso. Entre em contato com o suporte."
                : "O acesso desta clínica está bloqueado. Entre em contato com o suporte.");
        if (usuario.Clinica.Situacao != SituacoesCliente.Ativo)
            throw new DomainException("Esta clínica está inativa no sistema. Entre em contato com o suporte.");
        if (usuario.Clinica.AcessoVencido(DateOnly.FromDateTime(DateTime.Today)))
            throw new DomainException("O período de acesso desta clínica terminou em {0:d}. Entre em contato com o suporte.", usuario.Clinica.AcessoAte);
        if (usuario.BloqueadoAte > DateTime.Now)
            throw new DomainException("Usuário bloqueado temporariamente por excesso de tentativas. Tente novamente em alguns minutos.");

        if (!hasher.Verificar(usuario.SenhaHash, req.Senha ?? ""))
        {
            usuario.TentativasFalhas++;
            if (usuario.TentativasFalhas >= MaxTentativas)
            {
                usuario.BloqueadoAte = DateTime.Now.Add(TempoBloqueio);
                usuario.TentativasFalhas = 0;
            }
            await db.SaveChangesAsync(ct);
            throw new DomainException("E-mail ou senha inválidos.");
        }

        usuario.TentativasFalhas = 0;
        usuario.BloqueadoAte = null;
        usuario.UltimoAcessoEm = DateTime.Now;
        auditoria.Registrar("LOGIN", "seguranca.Usuario", usuario.Id, usuarioId: usuario.Id, clinicaId: usuario.ClinicaId);
        await db.SaveChangesAsync(ct);

        var perfis = usuario.Perfis.Select(p => p.Perfil.Nome).ToList();
        var medicoId = await db.Medicos.IgnoreQueryFilters()
            .Where(m => m.ClinicaId == usuario.ClinicaId && m.UsuarioId == usuario.Id && m.Ativo)
            .Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);

        return new UsuarioLogadoDto(usuario.Id, usuario.Nome, usuario.Email, perfis, medicoId, usuario.ClinicaId, usuario.Clinica.Nome, usuario.Idioma);
    }

    /// <summary>
    /// Confere a cada requisição se o usuário e a clínica dele continuam ativos e dentro do período de acesso
    /// (cookie emitido antes do bloqueio, da inativação ou do vencimento).
    /// </summary>
    public async Task<SituacaoSessao> SessaoValidaAsync(int usuarioId, int clinicaId, CancellationToken ct)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var clinica = await db.Usuarios.IgnoreQueryFilters()
            .Where(u => u.Id == usuarioId && u.ClinicaId == clinicaId && u.Ativo && u.Clinica.Situacao == SituacoesCliente.Ativo
                        && (u.Clinica.AcessoDefinitivo || u.Clinica.AcessoAte >= hoje))
            .Select(u => new { u.Clinica.AcessoDefinitivo, u.Clinica.AcessoAte })
            .FirstOrDefaultAsync(ct);
        if (clinica is null) return SituacaoSessao.Invalida;
        var transacoes = await TransacoesAsync(usuarioId, clinicaId, ct);
        // Mensalidade vencendo ou vencida: aviso só para quem administra a clínica
        var aviso = transacoes.Contains(Transacoes.UsuariosGerenciar) ? await cobrancas.AvisoAsync(clinicaId, ct) : null;
        return new(true, clinica.AcessoDefinitivo ? null : clinica.AcessoAte, transacoes, aviso);
    }

    /// <summary>Transações liberadas pelos perfis ativos do usuário (padrões ou exclusivos da clínica dele).</summary>
    public Task<List<string>> TransacoesAsync(int usuarioId, int clinicaId, CancellationToken ct) =>
        db.Usuarios.IgnoreQueryFilters().Where(u => u.Id == usuarioId && u.ClinicaId == clinicaId)
            .SelectMany(u => u.Perfis)
            .Where(up => up.Perfil.Ativo && (up.Perfil.ClinicaId == null || up.Perfil.ClinicaId == clinicaId))
            .SelectMany(up => up.Perfil.Permissoes.Select(x => x.Permissao.Codigo))
            .Distinct().ToListAsync(ct);

    /// <summary>Guarda o idioma escolhido pelo usuário (vale nos próximos logins, em qualquer computador).</summary>
    public async Task DefinirIdiomaAsync(int usuarioId, int clinicaId, string? idioma, CancellationToken ct)
    {
        var u = await db.Usuarios.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == usuarioId && x.ClinicaId == clinicaId, ct);
        if (u is null || u.Idioma == idioma) return;
        u.Idioma = idioma;
        await db.SaveChangesAsync(ct);
    }

    public async Task RegistrarLogoutAsync(int usuarioId, CancellationToken ct)
    {
        auditoria.Registrar("LOGOUT", "seguranca.Usuario", usuarioId, usuarioId: usuarioId);
        await db.SaveChangesAsync(ct);
    }

    public async Task AlterarSenhaAsync(int usuarioId, string senhaAtual, string novaSenha, CancellationToken ct)
    {
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId, ct)
            ?? throw new DomainException("Usuário não encontrado.");
        if (!hasher.Verificar(usuario.SenhaHash, senhaAtual))
            throw new DomainException("A senha atual não confere.");
        UsuarioService.ValidarSenha(novaSenha);
        usuario.SenhaHash = hasher.Gerar(novaSenha);
        usuario.AtualizadoEm = DateTime.Now;
        auditoria.Registrar("ALTERACAO_SENHA", "seguranca.Usuario", usuario.Id);
        await db.SaveChangesAsync(ct);
    }
}
