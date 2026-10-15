using Consultorio.Application.Abstractions;
using Consultorio.Application.Common;
using Consultorio.Domain.Common;
using Consultorio.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Consultorio.Application.Admin;

public record CobrancaDto(int Id, int ClinicaId, string Cliente, string Descricao, DateOnly? Competencia, DateOnly Vencimento, decimal Valor,
    DateOnly? PagoEm, decimal? ValorPago, string? FormaPagamento, string? Observacoes, bool Cancelada, DateTime CriadoEm)
{
    public string Status => Cancelada ? StatusCobranca.Cancelada : PagoEm is not null ? StatusCobranca.Paga
        : Vencimento < DateOnly.FromDateTime(DateTime.Today) ? StatusCobranca.Atrasada : StatusCobranca.Aberta;
    public int DiasAtraso => Status == StatusCobranca.Atrasada ? DateOnly.FromDateTime(DateTime.Today).DayNumber - Vencimento.DayNumber : 0;
}

public record NovaCobrancaRequest(string Descricao, DateOnly Vencimento, decimal Valor, DateOnly? Competencia = null);
public record PagamentoRequest(DateOnly PagoEm, decimal ValorPago, string FormaPagamento, string? Observacoes);

/// <summary>Totais do período (pelo vencimento) e o que está em aberto/atrasado hoje.</summary>
public record ResumoCobrancasDto(decimal Previsto, decimal Recebido, decimal EmAberto, decimal EmAtraso, int CobrancasAtrasadas, int ClientesEmAtraso);

/// <summary>Aviso de mensalidade para o topo das páginas da clínica.</summary>
public record AvisoCobrancaDto(string Descricao, DateOnly Vencimento, decimal Valor, DateOnly? BloqueiaEm);

/// <summary>
/// Controle de pagamentos dos clientes (área /Admin): mensalidades e cobranças avulsas, registro do pagamento,
/// estorno, cancelamento e prorrogação. Cobrança sem pagamento depois do vencimento + tolerância bloqueia o cliente
/// (situação BLOQUEADO, marcada como automática) e o pagamento reativa sozinho. Bloqueios feitos à mão pelo suporte
/// nunca são desfeitos aqui.
/// </summary>
public class CobrancaService(IAppDbContext db, IAuditoria auditoria)
{
    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Today);

    private IQueryable<Cobranca> Todas => db.Cobrancas.IgnoreQueryFilters();

    public async Task<List<CobrancaDto>> ListarAsync(int? clinicaId, string? status, DateOnly? de, DateOnly? ate, CancellationToken ct)
    {
        var hoje = Hoje;
        var q = Todas.AsNoTracking().Where(c => clinicaId == null || c.ClinicaId == clinicaId);
        if (de is not null) q = q.Where(c => c.Vencimento >= de);
        if (ate is not null) q = q.Where(c => c.Vencimento <= ate);
        q = status switch
        {
            StatusCobranca.Paga => q.Where(c => !c.Cancelada && c.PagoEm != null),
            StatusCobranca.Cancelada => q.Where(c => c.Cancelada),
            StatusCobranca.Atrasada => q.Where(c => !c.Cancelada && c.PagoEm == null && c.Vencimento < hoje),
            StatusCobranca.Aberta => q.Where(c => !c.Cancelada && c.PagoEm == null && c.Vencimento >= hoje),
            "PENDENTES" => q.Where(c => !c.Cancelada && c.PagoEm == null),
            _ => q
        };
        return await (from c in q
                      join cl in db.Clinicas on c.ClinicaId equals cl.Id
                      orderby c.Vencimento descending, cl.Nome
                      select new CobrancaDto(c.Id, c.ClinicaId, cl.Nome, c.Descricao, c.Competencia, c.Vencimento, c.Valor,
                          c.PagoEm, c.ValorPago, c.FormaPagamento, c.Observacoes, c.Cancelada, c.CriadoEm))
            .ToListAsync(ct);
    }

    public async Task<CobrancaDto> ObterAsync(int id, CancellationToken ct) =>
        (await (from c in Todas.AsNoTracking().Where(c => c.Id == id)
                join cl in db.Clinicas on c.ClinicaId equals cl.Id
                select new CobrancaDto(c.Id, c.ClinicaId, cl.Nome, c.Descricao, c.Competencia, c.Vencimento, c.Valor,
                    c.PagoEm, c.ValorPago, c.FormaPagamento, c.Observacoes, c.Cancelada, c.CriadoEm)).FirstOrDefaultAsync(ct))
        ?? throw new NaoEncontradoException("Cobrança não encontrada.");

    /// <summary>Resumo das cobranças com vencimento no período. Somas em memória (poucos registros; o SQLite dos testes não soma decimal).</summary>
    public async Task<ResumoCobrancasDto> ResumoAsync(DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var hoje = Hoje;
        var periodo = await Todas.AsNoTracking().Where(c => !c.Cancelada && c.Vencimento >= de && c.Vencimento <= ate)
            .Select(c => new { c.Valor, c.ValorPago }).ToListAsync(ct);
        var pendentes = await Todas.AsNoTracking().Where(c => !c.Cancelada && c.PagoEm == null)
            .Select(c => new { c.ClinicaId, c.Valor, c.Vencimento }).ToListAsync(ct);
        var atrasadas = pendentes.Where(c => c.Vencimento < hoje).ToList();
        return new ResumoCobrancasDto(periodo.Sum(c => c.Valor), periodo.Sum(c => c.ValorPago ?? 0),
            pendentes.Where(c => c.Vencimento >= hoje).Sum(c => c.Valor), atrasadas.Sum(c => c.Valor),
            atrasadas.Count, atrasadas.Select(c => c.ClinicaId).Distinct().Count());
    }

    public async Task<int> CriarAsync(int clinicaId, NovaCobrancaRequest req, CancellationToken ct)
    {
        var clinica = await db.Clinicas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clinicaId, ct)
                      ?? throw new NaoEncontradoException("Cliente não encontrado.");
        if (string.IsNullOrWhiteSpace(req.Descricao)) throw new DomainException("Informe a descrição da cobrança.");
        if (req.Valor <= 0) throw new DomainException("O valor da cobrança deve ser maior que zero.");
        var competencia = req.Competencia is DateOnly d ? new DateOnly(d.Year, d.Month, 1) : (DateOnly?)null;
        if (competencia is not null && await Todas.AnyAsync(c => c.ClinicaId == clinicaId && c.Competencia == competencia && !c.Cancelada, ct))
            throw new DomainException("Já existe mensalidade de {0:MM/yyyy} para {1}.", competencia, clinica.Nome);

        var c = new Cobranca
        {
            ClinicaId = clinicaId, Descricao = req.Descricao.Trim(), Competencia = competencia,
            Vencimento = req.Vencimento, Valor = Math.Round(req.Valor, 2)
        };
        db.Cobrancas.Add(c);
        await db.SaveChangesAsync(ct);
        auditoria.Registrar("INCLUSAO", "financeiro.Cobranca", c.Id, novo: new { c.Descricao, c.Competencia, c.Vencimento, c.Valor }, clinicaId: clinicaId);
        await db.SaveChangesAsync(ct);
        await VerificarAtrasoAsync(clinicaId, ct);
        return c.Id;
    }

    public async Task RegistrarPagamentoAsync(int id, PagamentoRequest req, CancellationToken ct)
    {
        if (req.ValorPago <= 0) throw new DomainException("Informe o valor pago.");
        if (req.PagoEm > Hoje) throw new DomainException("A data do pagamento não pode ser futura.");
        if (!FormasPagamento.Todas.Any(f => f.Codigo == req.FormaPagamento)) throw new DomainException("Informe a forma de pagamento.");
        var c = await CobrancaAsync(id, ct);
        if (c.Cancelada) throw new DomainException("Cobrança cancelada não recebe pagamento.");
        if (c.Paga) throw new DomainException("Esta cobrança já está paga. Estorne o pagamento para registrar outro.");
        c.PagoEm = req.PagoEm;
        c.ValorPago = Math.Round(req.ValorPago, 2);
        c.FormaPagamento = req.FormaPagamento;
        if (!string.IsNullOrWhiteSpace(req.Observacoes)) c.Observacoes = req.Observacoes.Trim();
        c.AtualizadoEm = DateTime.Now;
        auditoria.Registrar("PAGAMENTO", "financeiro.Cobranca", c.Id, novo: new { c.PagoEm, c.ValorPago, c.FormaPagamento }, clinicaId: c.ClinicaId);
        await db.SaveChangesAsync(ct);
        await VerificarAtrasoAsync(c.ClinicaId, ct);
    }

    public async Task EstornarPagamentoAsync(int id, string? motivo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(motivo)) throw new DomainException("Informe o motivo do estorno.");
        var c = await CobrancaAsync(id, ct);
        if (!c.Paga) throw new DomainException("Esta cobrança não tem pagamento registrado.");
        var anterior = new { c.PagoEm, c.ValorPago, c.FormaPagamento };
        c.PagoEm = null; c.ValorPago = null; c.FormaPagamento = null;
        c.AtualizadoEm = DateTime.Now;
        auditoria.Registrar("ESTORNO", "financeiro.Cobranca", c.Id, anterior, new { Motivo = motivo.Trim() }, clinicaId: c.ClinicaId);
        await db.SaveChangesAsync(ct);
        await VerificarAtrasoAsync(c.ClinicaId, ct);
    }

    public async Task CancelarAsync(int id, string? motivo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(motivo)) throw new DomainException("Informe o motivo do cancelamento.");
        var c = await CobrancaAsync(id, ct);
        if (c.Paga) throw new DomainException("Cobrança paga não pode ser cancelada. Estorne o pagamento antes.");
        if (c.Cancelada) return;
        c.Cancelada = true;
        c.Observacoes = Juntar(c.Observacoes, $"Cancelada: {motivo.Trim()}");
        c.AtualizadoEm = DateTime.Now;
        auditoria.Registrar("CANCELAMENTO", "financeiro.Cobranca", c.Id, novo: new { Motivo = motivo.Trim() }, clinicaId: c.ClinicaId);
        await db.SaveChangesAsync(ct);
        await VerificarAtrasoAsync(c.ClinicaId, ct);
    }

    /// <summary>Muda o vencimento de uma cobrança em aberto (dá prazo ao cliente; se ele estava bloqueado por atraso, é reativado).</summary>
    public async Task ProrrogarAsync(int id, DateOnly novoVencimento, CancellationToken ct)
    {
        var c = await CobrancaAsync(id, ct);
        if (c.Paga || c.Cancelada) throw new DomainException("Só cobrança em aberto pode ser prorrogada.");
        if (novoVencimento < Hoje) throw new DomainException("O novo vencimento não pode estar no passado.");
        var anterior = new { c.Vencimento };
        c.Vencimento = novoVencimento;
        c.AtualizadoEm = DateTime.Now;
        auditoria.Registrar("PRORROGACAO", "financeiro.Cobranca", c.Id, anterior, new { c.Vencimento }, clinicaId: c.ClinicaId);
        await db.SaveChangesAsync(ct);
        await VerificarAtrasoAsync(c.ClinicaId, ct);
    }

    /// <summary>
    /// Gera a mensalidade da competência para cada cliente não inativo com valor de mensalidade definido.
    /// Não repete competência já gerada (nem a cancelada). <paramref name="automatico"/>: rotina do sistema, que só
    /// gera mensalidade ainda não vencida, para nunca nascer uma cobrança já atrasada (que bloquearia o cliente na hora).
    /// </summary>
    public async Task<int> GerarMensalidadesAsync(DateOnly competencia, bool automatico, CancellationToken ct)
    {
        competencia = new DateOnly(competencia.Year, competencia.Month, 1);
        var hoje = Hoje;
        var clientes = await db.Clinicas.AsNoTracking()
            .Where(c => c.ValorMensalidade != null && c.Situacao != SituacoesCliente.Inativo
                        && !Todas.Any(x => x.ClinicaId == c.Id && x.Competencia == competencia))
            .Select(c => new { c.Id, c.ValorMensalidade, c.DiaVencimento }).ToListAsync(ct);
        var criadas = 0;
        foreach (var cl in clientes.Where(c => c.ValorMensalidade > 0))
        {
            var dia = Math.Clamp((int)cl.DiaVencimento, 1, DateTime.DaysInMonth(competencia.Year, competencia.Month));
            var vencimento = new DateOnly(competencia.Year, competencia.Month, dia);
            if (automatico && vencimento < hoje) continue;
            var c = new Cobranca
            {
                ClinicaId = cl.Id, Descricao = $"Mensalidade {competencia:MM/yyyy}", Competencia = competencia,
                Vencimento = vencimento, Valor = cl.ValorMensalidade!.Value
            };
            db.Cobrancas.Add(c);
            await db.SaveChangesAsync(ct);
            auditoria.Registrar("INCLUSAO", "financeiro.Cobranca", c.Id, novo: new { c.Descricao, c.Vencimento, c.Valor, Automatico = automatico }, clinicaId: cl.Id);
            criadas++;
        }
        await db.SaveChangesAsync(ct);
        return criadas;
    }

    /// <summary>Consulta das cobranças que já passaram da tolerância sem pagamento (as que bloqueiam o cliente).</summary>
    public static IQueryable<Cobranca> EmAtraso(IAppDbContext db, int clinicaId, int diasTolerancia)
    {
        var limite = Hoje.AddDays(-diasTolerancia);
        return db.Cobrancas.IgnoreQueryFilters()
            .Where(c => c.ClinicaId == clinicaId && !c.Cancelada && c.PagoEm == null && c.Vencimento < limite);
    }

    /// <summary>
    /// Aplica a regra de atraso a um cliente: bloqueia (automático) quem passou da tolerância e reativa quem foi
    /// bloqueado pelo sistema e já está em dia. Devolve true quando a situação mudou.
    /// </summary>
    public async Task<bool> VerificarAtrasoAsync(int clinicaId, CancellationToken ct)
    {
        var c = await db.Clinicas.FirstOrDefaultAsync(x => x.Id == clinicaId, ct);
        if (c is null) return false;
        var atrasada = c.BloqueioPorAtraso
            ? await EmAtraso(db, c.Id, c.DiasTolerancia).OrderBy(x => x.Vencimento).Select(x => new { x.Descricao, x.Vencimento }).FirstOrDefaultAsync(ct)
            : null;
        var anterior = new { c.Situacao, c.MotivoSituacao };
        if (atrasada is not null && c.Situacao == SituacoesCliente.Ativo)
        {
            c.Situacao = SituacoesCliente.Bloqueado;
            c.MotivoSituacao = $"Pagamento em atraso: {atrasada.Descricao} venceu em {atrasada.Vencimento:dd/MM/yyyy} (bloqueio automático)";
            c.BloqueadoPorAtraso = true;
        }
        else if (atrasada is null && c.Situacao == SituacoesCliente.Bloqueado && c.BloqueadoPorAtraso)
        {
            c.Situacao = SituacoesCliente.Ativo;
            c.MotivoSituacao = null;
            c.BloqueadoPorAtraso = false;
        }
        else return false;
        c.SituacaoDesde = DateTime.Now;
        c.AtualizadoEm = DateTime.Now;
        auditoria.Registrar("SITUACAO", "seguranca.Clinica", c.Id, anterior, new { c.Situacao, c.MotivoSituacao, Automatico = true }, clinicaId: c.Id);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Rotina periódica: gera as mensalidades do mês e aplica a regra de atraso em todos os clientes.</summary>
    public async Task<(int Geradas, int Alterados)> ProcessarAsync(CancellationToken ct)
    {
        var geradas = await GerarMensalidadesAsync(Hoje, automatico: true, ct);
        var ids = await db.Clinicas.AsNoTracking()
            .Where(c => c.BloqueadoPorAtraso || (c.BloqueioPorAtraso && c.Situacao == SituacoesCliente.Ativo && Todas.Any(x => x.ClinicaId == c.Id)))
            .Select(c => c.Id).ToListAsync(ct);
        var alterados = 0;
        foreach (var id in ids)
            if (await VerificarAtrasoAsync(id, ct)) alterados++;
        return (geradas, alterados);
    }

    /// <summary>Cobrança em aberto mais antiga que vence nos próximos 5 dias ou já venceu (aviso para a clínica).</summary>
    public async Task<AvisoCobrancaDto?> AvisoAsync(int clinicaId, CancellationToken ct)
    {
        var limite = Hoje.AddDays(5);
        var c = await (from x in Todas.AsNoTracking()
                       join cl in db.Clinicas on x.ClinicaId equals cl.Id
                       where x.ClinicaId == clinicaId && !x.Cancelada && x.PagoEm == null && x.Vencimento <= limite
                       orderby x.Vencimento
                       select new { x.Descricao, x.Vencimento, x.Valor, cl.BloqueioPorAtraso, cl.DiasTolerancia }).FirstOrDefaultAsync(ct);
        return c is null ? null
            : new AvisoCobrancaDto(c.Descricao, c.Vencimento, c.Valor, c.BloqueioPorAtraso ? c.Vencimento.AddDays(c.DiasTolerancia + 1) : null);
    }

    private async Task<Cobranca> CobrancaAsync(int id, CancellationToken ct) =>
        await Todas.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NaoEncontradoException("Cobrança não encontrada.");

    private static string Juntar(string? atual, string novo) =>
        (string.IsNullOrWhiteSpace(atual) ? novo : $"{atual} | {novo}") is var s && s.Length > 300 ? s[..300] : s;
}
