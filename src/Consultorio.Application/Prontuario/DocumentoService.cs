using System.Globalization;
using Consultorio.Application.Abstractions;
using Consultorio.Application.Common;
using Consultorio.Domain.Common;
using Consultorio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Prontuario;

public record DocumentoDto(int Id, string Tipo, string Titulo, string Texto, short? DiasAfastamento, string? Cid,
    DateTime EmitidoEm, int MedicoId, string Medico, int? EvolucaoId);

public record DocumentoEmitirRequest(string Tipo, string Texto, short? DiasAfastamento, string? Cid, int? EvolucaoId, int? AgendamentoId);

public record DocumentoImpressaoDto(int Id, string Tipo, string Titulo, string Texto, DateTime EmitidoEm,
    string Paciente, string? CpfPaciente, DateOnly DataNascimento, string Medico, string Crm, string UfCrm,
    string Clinica, string? EnderecoClinica, string? CidadeClinica, string? TelefoneClinica);

/// <summary>Modelo disponível para emissão. Id NULL = texto padrão do sistema.</summary>
public record ModeloDocumentoDto(int? Id, string Tipo, string Nome, string Texto, bool Ativo);

/// <summary>
/// Atestados, declarações de comparecimento, solicitações de exames e encaminhamentos.
/// O médico parte de um modelo (da clínica ou o padrão do sistema), ajusta o texto e emite;
/// os campos {paciente}, {dias}, {cid}... são preenchidos na emissão e o texto final fica gravado como foi impresso.
/// </summary>
public class DocumentoService(IAppDbContext db, IUsuarioAtual usuario, IAuditoria auditoria)
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    private int MedicoLogado => usuario.MedicoId
        ?? throw new DomainException("Usuário não está vinculado a um cadastro de médico.");

    public async Task<List<DocumentoDto>> ListarAsync(int pacienteId, CancellationToken ct)
    {
        var lista = await db.Documentos.AsNoTracking().Where(d => d.PacienteId == pacienteId)
            .OrderByDescending(d => d.EmitidoEm).ThenByDescending(d => d.Id)
            .Select(d => new DocumentoDto(d.Id, d.Tipo, d.Titulo, d.Texto, d.DiasAfastamento, d.Cid, d.EmitidoEm,
                d.MedicoId, d.Medico.Nome, d.EvolucaoId))
            .ToListAsync(ct);
        auditoria.Registrar("LEITURA", "prontuario.Documento", $"paciente:{pacienteId}");
        await db.SaveChangesAsync(ct);
        return lista;
    }

    /// <summary>Texto inicial do formulário: o modelo escolhido com os dados do paciente, do médico e da clínica já preenchidos.
    /// {dias} e {cid} continuam como campos até a emissão.</summary>
    public async Task<string> PrepararTextoAsync(int pacienteId, string tipo, int? modeloId, int? agendamentoId, CancellationToken ct)
    {
        if (!TiposDocumento.Valido(tipo)) throw new DomainException("Tipo de documento inválido.");
        var texto = TiposDocumento.TextoPadrao(tipo);
        if (modeloId is int id)
            texto = await db.ModelosDocumento.AsNoTracking().Where(m => m.Id == id && m.Tipo == tipo && m.Ativo)
                .Select(m => m.Texto).FirstOrDefaultAsync(ct) ?? throw new NaoEncontradoException("Modelo não encontrado.");
        var dados = await DadosAsync(pacienteId, MedicoLogado, agendamentoId, DateTime.Now, ct);
        return Preencher(texto, dados, dias: null, cid: null, manterCampos: true);
    }

    public async Task<int> EmitirAsync(int pacienteId, DocumentoEmitirRequest req, CancellationToken ct)
    {
        if (!TiposDocumento.Valido(req.Tipo)) throw new DomainException("Tipo de documento inválido.");
        if (string.IsNullOrWhiteSpace(req.Texto)) throw new DomainException("Informe o texto do documento.");
        var medicoId = MedicoLogado;
        if (req.Tipo == TiposDocumento.Atestado && req.DiasAfastamento is null && req.Texto.Contains("{dias"))
            throw new DomainException("Informe os dias de afastamento.");
        if (req.EvolucaoId is int evo && !await db.Evolucoes.AnyAsync(e => e.Id == evo && e.PacienteId == pacienteId, ct))
            throw new DomainException("Evolução não pertence ao paciente.");

        var agora = DateTime.Now;
        var dados = await DadosAsync(pacienteId, medicoId, req.AgendamentoId, agora, ct);
        var cid = string.IsNullOrWhiteSpace(req.Cid) ? null : req.Cid.Trim().ToUpperInvariant();
        var d = new DocumentoClinico
        {
            PacienteId = pacienteId, MedicoId = medicoId, EvolucaoId = req.EvolucaoId, Tipo = req.Tipo,
            Titulo = TiposDocumento.Titulo(req.Tipo), DiasAfastamento = req.DiasAfastamento, Cid = cid, EmitidoEm = agora,
            Texto = Preencher(req.Texto.Trim(), dados, req.DiasAfastamento, cid, manterCampos: false)
        };
        db.Documentos.Add(d);
        await db.SaveChangesAsync(ct);
        auditoria.Registrar("INCLUSAO", "prontuario.Documento", d.Id, novo: new { d.Tipo, d.DiasAfastamento, d.Cid });
        await db.SaveChangesAsync(ct);
        return d.Id;
    }

    public async Task<DocumentoImpressaoDto> ObterImpressaoAsync(int pacienteId, int documentoId, CancellationToken ct)
    {
        var r = await (from d in db.Documentos.AsNoTracking()
                       join pac in db.Pacientes on d.PacienteId equals pac.Id
                       join m in db.Medicos on d.MedicoId equals m.Id
                       join c in db.Clinicas on d.ClinicaId equals c.Id
                       where d.Id == documentoId && d.PacienteId == pacienteId
                       select new DocumentoImpressaoDto(d.Id, d.Tipo, d.Titulo, d.Texto, d.EmitidoEm,
                           pac.NomeSocial ?? pac.Nome, pac.Cpf, pac.DataNascimento, m.Nome, m.Crm, m.UfCrm,
                           c.Nome, c.Endereco, c.Cidade != null ? c.Cidade + "/" + c.Uf : null, c.Telefone))
            .FirstOrDefaultAsync(ct) ?? throw new NaoEncontradoException("Documento não encontrado.");
        auditoria.Registrar("IMPRESSAO", "prontuario.Documento", documentoId);
        await db.SaveChangesAsync(ct);
        return r;
    }

    // ---------- Modelos ----------

    /// <summary>Modelos da clínica; com <paramref name="incluirPadrao"/>, o texto padrão do sistema vem primeiro em cada tipo.</summary>
    public async Task<List<ModeloDocumentoDto>> ListarModelosAsync(string? tipo, bool somenteAtivos, bool incluirPadrao, CancellationToken ct)
    {
        var q = db.ModelosDocumento.AsNoTracking();
        if (tipo is not null) q = q.Where(m => m.Tipo == tipo);
        if (somenteAtivos) q = q.Where(m => m.Ativo);
        var lista = await q.OrderBy(m => m.Tipo).ThenBy(m => m.Nome)
            .Select(m => new ModeloDocumentoDto(m.Id, m.Tipo, m.Nome, m.Texto, m.Ativo)).ToListAsync(ct);
        if (incluirPadrao)
            lista.InsertRange(0, TiposDocumento.Todos.Where(t => tipo is null || t == tipo)
                .Select(t => new ModeloDocumentoDto(null, t, "Padrão do sistema", TiposDocumento.TextoPadrao(t), true)));
        return lista;
    }

    public async Task<ModeloDocumentoDto> ObterModeloAsync(int id, CancellationToken ct) =>
        await db.ModelosDocumento.AsNoTracking().Where(m => m.Id == id)
            .Select(m => new ModeloDocumentoDto(m.Id, m.Tipo, m.Nome, m.Texto, m.Ativo)).FirstOrDefaultAsync(ct)
        ?? throw new NaoEncontradoException("Modelo não encontrado.");

    public async Task<int> SalvarModeloAsync(ModeloDocumentoDto dto, CancellationToken ct)
    {
        if (!TiposDocumento.Valido(dto.Tipo)) throw new DomainException("Tipo de documento inválido.");
        if (string.IsNullOrWhiteSpace(dto.Nome) || string.IsNullOrWhiteSpace(dto.Texto))
            throw new DomainException("Informe o nome e o texto do modelo.");
        var nome = dto.Nome.Trim();
        if (await db.ModelosDocumento.AnyAsync(m => m.Tipo == dto.Tipo && m.Nome == nome && m.Id != (dto.Id ?? 0), ct))
            throw new DomainException("Já existe um modelo com esse nome para este tipo de documento.");

        ModeloDocumento m;
        if (dto.Id is int id)
        {
            m = await db.ModelosDocumento.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NaoEncontradoException("Modelo não encontrado.");
            m.AtualizadoEm = DateTime.Now;
        }
        else
        {
            m = new ModeloDocumento();
            db.ModelosDocumento.Add(m);
        }
        m.Tipo = dto.Tipo; m.Nome = nome; m.Texto = dto.Texto.Trim(); m.Ativo = dto.Ativo;
        await db.SaveChangesAsync(ct);
        auditoria.Registrar(dto.Id is null ? "INCLUSAO" : "ALTERACAO", "prontuario.ModeloDocumento", m.Id);
        await db.SaveChangesAsync(ct);
        return m.Id;
    }

    // ---------- Preenchimento dos campos ----------

    private sealed record Dados(string Paciente, string? Cpf, DateOnly Nascimento, DateTime Data, DateTime? Chegada,
        string Medico, string Crm, string Clinica, string? Cidade);

    private async Task<Dados> DadosAsync(int pacienteId, int medicoId, int? agendamentoId, DateTime agora, CancellationToken ct)
    {
        var pac = await db.Pacientes.AsNoTracking().Where(p => p.Id == pacienteId)
            .Select(p => new { Nome = p.NomeSocial ?? p.Nome, p.Cpf, p.DataNascimento }).FirstOrDefaultAsync(ct)
            ?? throw new NaoEncontradoException("Paciente não encontrado.");
        var med = await db.Medicos.AsNoTracking().Where(m => m.Id == medicoId).Select(m => new { m.Nome, m.Crm, m.UfCrm }).FirstAsync(ct);
        var cli = await db.Clinicas.AsNoTracking().Where(c => c.Id == usuario.ClinicaId).Select(c => new { c.Nome, c.Cidade }).FirstOrDefaultAsync(ct);

        // Hora de chegada para a declaração de comparecimento: o agendamento informado ou o de hoje
        var agendamentos = db.Agendamentos.AsNoTracking().Where(a => a.PacienteId == pacienteId);
        var ag = agendamentoId is int agId
            ? await agendamentos.Where(a => a.Id == agId).Select(a => new { a.ChegadaEm, a.Inicio }).FirstOrDefaultAsync(ct)
            : await agendamentos.Where(a => a.Inicio >= agora.Date && a.Inicio < agora.Date.AddDays(1) && a.Inicio <= agora)
                .OrderByDescending(a => a.Inicio).Select(a => new { a.ChegadaEm, a.Inicio }).FirstOrDefaultAsync(ct);

        return new Dados(pac.Nome, pac.Cpf, pac.DataNascimento, agora, ag?.ChegadaEm ?? ag?.Inicio,
            med.Nome, $"{med.Crm}/{med.UfCrm}", cli?.Nome ?? "", cli?.Cidade);
    }

    /// <param name="manterCampos">true no formulário: campos ainda sem valor ({dias}, {cid}) continuam no texto para o médico ver.</param>
    private static string Preencher(string texto, Dados d, short? dias, string? cid, bool manterCampos)
    {
        // Sem CID (o paciente não autorizou), a linha que o mostraria sai do documento
        if (!manterCampos && cid is null)
            texto = string.Join('\n', texto.Replace("\r\n", "\n").Split('\n').Where(l => !l.Contains("{cid}")));

        var campos = new Dictionary<string, string?>
        {
            ["{paciente}"] = d.Paciente,
            ["{cpf}"] = d.Cpf is null ? null : FormatarCpf(d.Cpf),
            ["{nascimento}"] = d.Nascimento.ToString("dd/MM/yyyy", PtBr),
            ["{idade}"] = Idade(d.Nascimento, DateOnly.FromDateTime(d.Data)).ToString(PtBr),
            ["{data}"] = d.Data.ToString("dd/MM/yyyy", PtBr),
            ["{data_extenso}"] = d.Data.ToString("d 'de' MMMM 'de' yyyy", PtBr),
            ["{hora_inicio}"] = (d.Chegada ?? d.Data).ToString("HH:mm", PtBr),
            ["{hora_fim}"] = manterCampos ? null : d.Data.ToString("HH:mm", PtBr),
            ["{dias}"] = dias?.ToString(PtBr),
            ["{dias_extenso}"] = dias is short n ? PorExtenso(n) : null,
            ["{cid}"] = cid,
            ["{medico}"] = d.Medico,
            ["{crm}"] = d.Crm,
            ["{clinica}"] = d.Clinica,
            ["{cidade}"] = d.Cidade
        };
        foreach (var (campo, valor) in campos)
        {
            if (valor is null && manterCampos) continue;
            texto = texto.Replace(campo, valor ?? "", StringComparison.OrdinalIgnoreCase);
        }
        return texto;
    }

    private static string FormatarCpf(string cpf) =>
        cpf.Length == 11 ? $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}" : cpf;

    private static int Idade(DateOnly nascimento, DateOnly hoje)
    {
        var idade = hoje.Year - nascimento.Year;
        return nascimento > hoje.AddYears(-idade) ? idade - 1 : idade;
    }

    private static readonly string[] Unidades = ["zero", "um", "dois", "três", "quatro", "cinco", "seis", "sete", "oito", "nove",
        "dez", "onze", "doze", "treze", "quatorze", "quinze", "dezesseis", "dezessete", "dezoito", "dezenove"];
    private static readonly string[] Dezenas = ["", "", "vinte", "trinta", "quarenta", "cinquenta", "sessenta", "setenta", "oitenta", "noventa"];
    private static readonly string[] Centenas = ["", "cento", "duzentos", "trezentos"];

    /// <summary>Número por extenso (0 a 399), para os dias de afastamento.</summary>
    public static string PorExtenso(int n)
    {
        if (n < 20) return Unidades[n];
        if (n < 100) return Dezenas[n / 10] + (n % 10 == 0 ? "" : " e " + Unidades[n % 10]);
        if (n == 100) return "cem";
        if (n < 400) return Centenas[n / 100] + (n % 100 == 0 ? "" : " e " + PorExtenso(n % 100));
        return n.ToString(PtBr);
    }
}
