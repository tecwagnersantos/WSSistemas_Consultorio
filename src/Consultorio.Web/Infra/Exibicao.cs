using System.Globalization;
using System.Security.Claims;
using Consultorio.Domain.Common;
using Consultorio.Domain.Enums;
using Microsoft.Extensions.Localization;

namespace Consultorio.Web.Infra;

/// <summary>Textos e formatações usados nas telas, no idioma da requisição (CultureInfo.CurrentUICulture).</summary>
public static class Exibicao
{
    /// <summary>Textos traduzidos (Resources/Textos*.resx), definido ao iniciar o site. Lê o idioma da requisição a cada chamada.</summary>
    public static IStringLocalizer? Textos { get; set; }

    /// <summary>Traduz um texto em português (nomes de status, perfis, situações etc.).</summary>
    public static string Tr(string texto) => Textos?[texto].Value ?? texto;
    public static string Tr(string formato, params object?[] args) => Textos?[formato, args!].Value ?? string.Format(formato, args);

    /// <summary>Formatação de números e datas do idioma atual.</summary>
    public static CultureInfo Cultura => CultureInfo.CurrentCulture;

    /// <summary>Símbolo da moeda do sistema (os valores são sempre na mesma moeda, qualquer que seja o idioma).</summary>
    public static string SimboloMoeda { get; set; } = "R$";

    public static string Nome(this StatusAgendamento s) => s switch
    {
        StatusAgendamento.Agendado => Tr("Agendado"),
        StatusAgendamento.Confirmado => Tr("Confirmado"),
        StatusAgendamento.Aguardando => Tr("Na sala de espera"),
        StatusAgendamento.EmAtendimento => Tr("Em atendimento"),
        StatusAgendamento.Atendido => Tr("Atendido"),
        StatusAgendamento.Faltou => Tr("Faltou"),
        StatusAgendamento.Cancelado => Tr("Cancelado"),
        StatusAgendamento.Remarcado => Tr("Remarcado"),
        _ => s.ToString()
    };

    public static string Css(this StatusAgendamento s) => "status-" + s.ToString().ToLowerInvariant();

    public static string Nome(this TipoAtendimento t) => t switch
    {
        TipoAtendimento.Consulta => Tr("Consulta"),
        TipoAtendimento.Retorno => Tr("Retorno"),
        TipoAtendimento.Procedimento => Tr("Procedimento"),
        TipoAtendimento.Teleconsulta => Tr("Teleconsulta"),
        _ => t.ToString()
    };

    /// <summary>Códigos de status usados nas views do banco (relatórios).</summary>
    public static readonly (string Codigo, string Nome)[] StatusRelatorio =
    [
        ("AGENDADO", "Agendado"), ("CONFIRMADO", "Confirmado"), ("AGUARDANDO", "Na sala de espera"),
        ("EM_ATENDIMENTO", "Em atendimento"), ("ATENDIDO", "Atendido"), ("FALTOU", "Faltou"),
        ("CANCELADO", "Cancelado"), ("REMARCADO", "Remarcado")
    ];

    public static string NomeStatusRelatorio(string codigo) =>
        StatusRelatorio.FirstOrDefault(s => s.Codigo == codigo).Nome is { } n ? Tr(n) : codigo;

    public static string NomeTipoRelatorio(string codigo) =>
        codigo.Length == 0 ? "" : Tr(codigo[0] + codigo[1..].ToLowerInvariant());

    /// <summary>Nome do dia da semana (0 = domingo) no idioma atual.</summary>
    public static string DiaSemana(int dia) => Cultura.TextInfo.ToTitleCase(Cultura.DateTimeFormat.GetDayName((DayOfWeek)dia));

    public static string Moeda(decimal? v)
    {
        if (v is null) return "—";
        var nf = (NumberFormatInfo)Cultura.NumberFormat.Clone();
        nf.CurrencySymbol = SimboloMoeda;
        return v.Value.ToString("C", nf);
    }
    public static string Data(DateOnly d) => d.ToString("d", Cultura);
    public static string Data(DateTime d) => d.ToString("d", Cultura);
    public static string DataHora(DateTime d) => d.ToString("d", Cultura) + " " + d.ToString("HH:mm", Cultura);
    public static string Hora(DateTime d) => d.ToString("HH:mm", Cultura);
    /// <summary>Data por extenso ("quarta-feira, 14 de outubro de 2026" / "Wednesday, October 14, 2026").</summary>
    public static string DataLonga(DateTime d) => d.ToString("D", Cultura);
    public static string DataLonga(DateOnly d) => d.ToString("D", Cultura);
    /// <summary>Dia e mês sem o ano, na ordem do idioma (14/10 ou 10/14).</summary>
    public static string DiaMes(DateOnly d) => d.ToString(Cultura.DateTimeFormat.ShortDatePattern
        .Replace("yyyy", "").Replace("yy", "").Trim('/', '.', '-', ' '), Cultura);
    public static string DiaMes(DateTime d) => DiaMes(DateOnly.FromDateTime(d));
    /// <summary>Dia da semana e data ("segunda-feira, 14/10/2026").</summary>
    public static string DiaSemanaData(DateOnly d) => $"{d.ToString("dddd", Cultura)}, {Data(d)}";
    public static string MesAno(DateOnly d) => d.ToString("MMM/yyyy", Cultura);
    public static string MesAno(DateTime d) => d.ToString("MMM/yyyy", Cultura);

    /// <summary>Valor para atributos value de inputs (sempre com ponto decimal).</summary>
    public static string Num(decimal? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";
    public static string Num(short? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";
    public static string Num(byte? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";
    public static string Iso(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
    public static string IsoLocal(DateTime? d) => d?.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) ?? "";

    public static string Cpf(string? cpf) =>
        cpf is { Length: 11 } ? $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}" : cpf ?? "";

    public static int Idade(DateOnly nascimento)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var idade = hoje.Year - nascimento.Year;
        if (nascimento > hoje.AddYears(-idade)) idade--;
        return idade;
    }

    public static string NomePerfil(string perfil) => perfil switch
    {
        Perfis.Administrador => Tr("Administrador"),
        Perfis.Medico => Tr("Médico"),
        Perfis.Recepcao => Tr("Recepção"),
        _ => perfil
    };

    public static string? NomeClinica(this ClaimsPrincipal u) => u.FindFirst(UsuarioAtual.ClaimClinicaNome)?.Value;
    /// <summary>O usuário tem a transação liberada por algum perfil? (<see cref="Transacoes"/>)</summary>
    public static bool Pode(this ClaimsPrincipal u, string transacao) => u.HasClaim(Transacoes.Claim, transacao);
    public static bool PodeAlgum(this ClaimsPrincipal u, params string[] transacoes) => transacoes.Any(u.Pode);
    public static bool TemMedicoVinculado(this ClaimsPrincipal u) => u.HasClaim(c => c.Type == UsuarioAtual.ClaimMedicoId);
}
