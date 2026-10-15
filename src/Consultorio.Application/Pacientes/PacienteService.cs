using System.ComponentModel.DataAnnotations;
using Consultorio.Application.Abstractions;
using Consultorio.Application.Common;
using Consultorio.Domain.Common;
using Consultorio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Pacientes;

public record PacienteResumoDto(int Id, string Nome, string? NomeSocial, string? Cpf, DateOnly DataNascimento, string? Celular, bool Ativo);

public record PacientePlanoDto(int? Id, int PlanoId, string? Plano, string? Convenio, string NumeroCarteirinha, DateOnly? ValidadeCarteirinha, bool Titular, bool Ativo);

public class PacienteDto
{
    public int Id { get; set; }
    [Required, MaxLength(150)] public string Nome { get; set; } = "";
    [MaxLength(150)] public string? NomeSocial { get; set; }
    [RegularExpression(@"^\d{11}$", ErrorMessage = "CPF deve ter 11 dígitos.")] public string? Cpf { get; set; }
    [RegularExpression(@"^\d{15}$", ErrorMessage = "CNS deve ter 15 dígitos.")] public string? Cns { get; set; }
    [Required] public DateOnly DataNascimento { get; set; }
    [Required, RegularExpression("^[MFI]$")] public string Sexo { get; set; } = "I";
    [EmailAddress, MaxLength(254)] public string? Email { get; set; }
    [MaxLength(20)] public string? Telefone { get; set; }
    [MaxLength(20)] public string? Celular { get; set; }
    [RegularExpression(@"^\d{8}$")] public string? Cep { get; set; }
    [MaxLength(150)] public string? Logradouro { get; set; }
    [MaxLength(10)] public string? Numero { get; set; }
    [MaxLength(60)] public string? Complemento { get; set; }
    [MaxLength(80)] public string? Bairro { get; set; }
    [MaxLength(80)] public string? Cidade { get; set; }
    [StringLength(2, MinimumLength = 2)] public string? Uf { get; set; }
    [MaxLength(150)] public string? NomeResponsavel { get; set; }
    [RegularExpression(@"^\d{11}$")] public string? CpfResponsavel { get; set; }
    [MaxLength(500)] public string? Observacoes { get; set; }
    public bool ConsentimentoLgpd { get; set; }
    public bool Ativo { get; set; } = true;
    public List<PacientePlanoDto> Planos { get; set; } = [];
}

public class PacienteService(IAppDbContext db, IAuditoria auditoria)
{
    public async Task<PaginaResultado<PacienteResumoDto>> PesquisarAsync(string? termo, int pagina, int tamanho, CancellationToken ct)
    {
        tamanho = Math.Clamp(tamanho, 1, 100);
        pagina = Math.Max(pagina, 1);
        var q = db.Pacientes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(termo))
        {
            var t = termo.Trim();
            var digitos = new string(t.Where(char.IsDigit).ToArray());
            q = digitos.Length == 11
                ? q.Where(p => p.Cpf == digitos)
                : q.Where(p => p.Nome.Contains(t) || (p.NomeSocial != null && p.NomeSocial.Contains(t)) || (p.Celular != null && p.Celular.Contains(t)));
        }
        var total = await q.CountAsync(ct);
        var itens = await q.OrderBy(p => p.Nome).Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(p => new PacienteResumoDto(p.Id, p.Nome, p.NomeSocial, p.Cpf, p.DataNascimento, p.Celular, p.Ativo))
            .ToListAsync(ct);
        return new(itens, total, pagina, tamanho);
    }

    public async Task<PacienteDto> ObterAsync(int id, CancellationToken ct)
    {
        var p = await db.Pacientes.AsNoTracking()
            .Include(x => x.Planos).ThenInclude(pp => pp.Plano).ThenInclude(pl => pl.Convenio)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NaoEncontradoException("Paciente não encontrado.");

        auditoria.Registrar("LEITURA", "cadastro.Paciente", id);
        await db.SaveChangesAsync(ct);

        return new PacienteDto
        {
            Id = p.Id, Nome = p.Nome, NomeSocial = p.NomeSocial, Cpf = p.Cpf, Cns = p.Cns,
            DataNascimento = p.DataNascimento, Sexo = p.Sexo, Email = p.Email, Telefone = p.Telefone,
            Celular = p.Celular, Cep = p.Cep, Logradouro = p.Logradouro, Numero = p.Numero,
            Complemento = p.Complemento, Bairro = p.Bairro, Cidade = p.Cidade, Uf = p.Uf,
            NomeResponsavel = p.NomeResponsavel, CpfResponsavel = p.CpfResponsavel, Observacoes = p.Observacoes,
            ConsentimentoLgpd = p.ConsentimentoLgpdEm != null, Ativo = p.Ativo,
            Planos = p.Planos.Select(pp => new PacientePlanoDto(pp.Id, pp.PlanoId, pp.Plano.Nome, pp.Plano.Convenio.Nome,
                pp.NumeroCarteirinha, pp.ValidadeCarteirinha, pp.Titular, pp.Ativo)).ToList()
        };
    }

    /// <summary>Carteirinhas ativas do paciente (usado na tela de agendamento).</summary>
    public Task<List<PacientePlanoDto>> CarteirinhasAsync(int pacienteId, CancellationToken ct) =>
        db.PacientePlanos.AsNoTracking().Where(pp => pp.PacienteId == pacienteId && pp.Ativo)
            .Select(pp => new PacientePlanoDto(pp.Id, pp.PlanoId, pp.Plano.Nome, pp.Plano.Convenio.Nome,
                pp.NumeroCarteirinha, pp.ValidadeCarteirinha, pp.Titular, pp.Ativo))
            .ToListAsync(ct);

    public async Task<int> SalvarAsync(int? id, PacienteDto dto, CancellationToken ct)
    {
        if (dto.DataNascimento > DateOnly.FromDateTime(DateTime.Today))
            throw new DomainException("Data de nascimento no futuro.");
        if (dto.Cpf != null && await db.Pacientes.AnyAsync(p => p.Cpf == dto.Cpf && p.Id != id, ct))
            throw new DomainException("Já existe paciente com este CPF.");
        var planos = dto.Planos.Select(x => x.PlanoId).Distinct().ToList();
        if (await db.Planos.CountAsync(x => planos.Contains(x.Id), ct) != planos.Count)
            throw new DomainException("Plano de convênio não encontrado.");

        Paciente p;
        string? anterior = null;
        if (id is null)
        {
            p = new Paciente();
            db.Pacientes.Add(p);
        }
        else
        {
            p = await db.Pacientes.Include(x => x.Planos).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NaoEncontradoException("Paciente não encontrado.");
            anterior = System.Text.Json.JsonSerializer.Serialize(new { p.Nome, p.Cpf, p.DataNascimento, p.Celular, p.Email });
            p.AtualizadoEm = DateTime.Now;
        }

        p.Nome = dto.Nome.Trim(); p.NomeSocial = dto.NomeSocial; p.Cpf = dto.Cpf; p.Cns = dto.Cns;
        p.DataNascimento = dto.DataNascimento; p.Sexo = dto.Sexo; p.Email = dto.Email; p.Telefone = dto.Telefone;
        p.Celular = dto.Celular; p.Cep = dto.Cep; p.Logradouro = dto.Logradouro; p.Numero = dto.Numero;
        p.Complemento = dto.Complemento; p.Bairro = dto.Bairro; p.Cidade = dto.Cidade; p.Uf = dto.Uf?.ToUpperInvariant();
        p.NomeResponsavel = dto.NomeResponsavel; p.CpfResponsavel = dto.CpfResponsavel; p.Observacoes = dto.Observacoes;
        p.Ativo = dto.Ativo;
        if (dto.ConsentimentoLgpd && p.ConsentimentoLgpdEm is null) p.ConsentimentoLgpdEm = DateTime.Now;

        // Carteirinhas: atualiza as existentes, inclui novas e inativa as removidas (não apaga: podem estar em agendamentos)
        foreach (var existente in p.Planos)
        {
            var d = dto.Planos.FirstOrDefault(x => x.Id == existente.Id);
            if (d is null) { existente.Ativo = false; continue; }
            existente.PlanoId = d.PlanoId; existente.NumeroCarteirinha = d.NumeroCarteirinha;
            existente.ValidadeCarteirinha = d.ValidadeCarteirinha; existente.Titular = d.Titular; existente.Ativo = d.Ativo;
        }
        foreach (var d in dto.Planos.Where(x => x.Id is null or 0))
            p.Planos.Add(new PacientePlano { PlanoId = d.PlanoId, NumeroCarteirinha = d.NumeroCarteirinha,
                ValidadeCarteirinha = d.ValidadeCarteirinha, Titular = d.Titular, Ativo = d.Ativo });

        await db.SaveChangesAsync(ct);
        auditoria.Registrar(id is null ? "INCLUSAO" : "ALTERACAO", "cadastro.Paciente", p.Id, anterior,
            new { p.Nome, p.Cpf, p.DataNascimento, p.Celular, p.Email });
        await db.SaveChangesAsync(ct);
        return p.Id;
    }
}
